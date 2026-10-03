using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LifeLink.Services.Auth
{
    /// <summary>Idle timeout settings: Session:IdleTimeoutMinutes (default 10) and Session:WarningMinutes (default 1).</summary>
    public sealed class SessionSettings
    {
        public const string ActivityHeader = "X-LifeLink-Activity";
        public const string EndedHeader = "X-Session-Ended";
        public const string SessionClaim = "sid";

        public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);
        public TimeSpan WarningPeriod { get; init; } = TimeSpan.FromMinutes(1);

        public static SessionSettings Default { get; } = new();

        public static SessionSettings FromConfiguration(IConfiguration configuration)
        {
            var idle = configuration.GetValue("Session:IdleTimeoutMinutes", 10.0);
            var warning = configuration.GetValue("Session:WarningMinutes", 1.0);
            if (idle <= 0) throw new InvalidOperationException("Session:IdleTimeoutMinutes must be greater than zero.");
            if (warning < 0 || warning >= idle) throw new InvalidOperationException("Session:WarningMinutes must be at least 0 and less than Session:IdleTimeoutMinutes.");
            return new SessionSettings { IdleTimeout = TimeSpan.FromMinutes(idle), WarningPeriod = TimeSpan.FromMinutes(warning) };
        }
    }

    public enum SessionState
    {
        Active,
        Idle,  // no activity for longer than the idle timeout: ended now
        Ended  // signed out, ended earlier, or unknown
    }

    public interface ISessionService
    {
        SessionSettings Settings { get; }
        Task<UserSession> StartAsync(Guid userId);
        Task<SessionState> CheckAsync(Guid sessionId, Guid userId, bool recordActivity);
        Task EndAsync(Guid sessionId, string reason);
    }

    /// <summary>
    /// Server-side sessions for the idle timeout. Every authenticated request checks its session; only requests the
    /// frontend marks as user activity (X-LifeLink-Activity: 1, never background polling) move LastActivityAt on.
    /// Updates are conditional (still open and not yet idle at this moment), so a late request can never revive a
    /// session that has already timed out, and a sign-out at the same moment always wins.
    /// </summary>
    public class SessionService : ISessionService
    {
        private readonly AppDbContext _context;

        public SessionService(AppDbContext context, SessionSettings settings)
        {
            _context = context;
            Settings = settings;
        }

        public SessionSettings Settings { get; }

        public async Task<UserSession> StartAsync(Guid userId)
        {
            var now = DateTime.UtcNow;
            var session = new UserSession { SessionId = Guid.NewGuid(), UserId = userId, CreatedAt = now, LastActivityAt = now };
            _context.UserSessions.Add(session);
            await _context.SaveChangesAsync();
            _context.Entry(session).State = EntityState.Detached;
            return session;
        }

        public async Task<SessionState> CheckAsync(Guid sessionId, Guid userId, bool recordActivity)
        {
            var now = DateTime.UtcNow;
            var session = await _context.UserSessions.AsNoTracking()
                .Where(s => s.SessionId == sessionId && s.UserId == userId)
                .Select(s => new { s.EndedAt, s.LastActivityAt })
                .FirstOrDefaultAsync();

            if (session == null || session.EndedAt != null)
            {
                return SessionState.Ended;
            }

            var idleCutoff = now - Settings.IdleTimeout;
            if (session.LastActivityAt <= idleCutoff)
            {
                await EndOpenSessionAsync(sessionId, SessionEndReasons.Idle, now);
                return SessionState.Idle;
            }

            if (recordActivity)
            {
                var updated = await RecordActivityAsync(sessionId, idleCutoff, now);
                if (updated == 0)
                {
                    // Signed out or timed out between the read and this update
                    return SessionState.Ended;
                }
            }

            return SessionState.Active;
        }

        public Task EndAsync(Guid sessionId, string reason) => EndOpenSessionAsync(sessionId, reason, DateTime.UtcNow);

        private async Task EndOpenSessionAsync(Guid sessionId, string reason, DateTime now)
        {
            if (_context.Database.IsRelational())
            {
                await _context.UserSessions
                    .Where(s => s.SessionId == sessionId && s.EndedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.EndedAt, now).SetProperty(s => s.EndReason, reason));
                return;
            }

            await UpdateTrackedAsync(s => s.SessionId == sessionId && s.EndedAt == null, s => { s.EndedAt = now; s.EndReason = reason; });
        }

        private async Task<int> RecordActivityAsync(Guid sessionId, DateTime idleCutoff, DateTime now)
        {
            if (_context.Database.IsRelational())
            {
                return await _context.UserSessions
                    .Where(s => s.SessionId == sessionId && s.EndedAt == null && s.LastActivityAt > idleCutoff)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastActivityAt, now));
            }

            return await UpdateTrackedAsync(s => s.SessionId == sessionId && s.EndedAt == null && s.LastActivityAt > idleCutoff,
                s => s.LastActivityAt = now);
        }

        // In-memory test databases have no ExecuteUpdate: same condition, applied through the change tracker
        private async Task<int> UpdateTrackedAsync(System.Linq.Expressions.Expression<Func<UserSession, bool>> condition, Action<UserSession> apply)
        {
            var sessions = await _context.UserSessions.Where(condition).ToListAsync();
            foreach (var s in sessions) apply(s);
            if (sessions.Count > 0) await _context.SaveChangesAsync();
            foreach (var s in sessions) _context.Entry(s).State = EntityState.Detached;
            return sessions.Count;
        }
    }
}
