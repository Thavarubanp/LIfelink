using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Auth;
using LifeLink.Entities;
using LifeLink.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Idle timeout: each sign-in is a server-side session; only user activity moves it on, it ends after the idle
    /// timeout or on sign-out, and a late request can never revive it.
    /// </summary>
    public class SessionTimeoutTests
    {
        private static AppDbContext NewDb() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static readonly SessionSettings TenMinutes = new() { IdleTimeout = TimeSpan.FromMinutes(10), WarningPeriod = TimeSpan.FromMinutes(1) };

        private static async Task<(AppDbContext Db, User User, SessionService Sessions)> SeedAsync()
        {
            var db = NewDb();
            var user = new User { UserId = Guid.NewGuid(), FirstName = "Sam", LastName = "User", Email = "sam@h.org", PasswordHash = "x" };
            await db.Users.AddAsync(user);
            await db.SaveChangesAsync();
            return (db, user, new SessionService(db, TenMinutes));
        }

        private static async Task SetLastActivityAsync(AppDbContext db, Guid sessionId, DateTime at)
        {
            var session = await db.UserSessions.SingleAsync(s => s.SessionId == sessionId);
            session.LastActivityAt = at;
            await db.SaveChangesAsync();
            db.Entry(session).State = EntityState.Detached;
        }

        private static Task<UserSession> LoadAsync(AppDbContext db, Guid sessionId) =>
            db.UserSessions.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);

        [Fact]
        public async Task Sign_In_Starts_A_Session_And_The_Token_Carries_Its_Id_And_The_Settings_Are_Returned()
        {
            var db = NewDb();
            db.Database.EnsureCreated(); // seeds the roles
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TEST_SIGNING_KEY_FOR_UNIT_TESTS_ONLY_32_BYTES_MINIMUM",
                ["Jwt:Issuer"] = "LifeLinkAPI",
                ["Jwt:Audience"] = "LifeLinkApp"
            }).Build();
            var hasher = new PasswordHasherService();
            var user = new User { UserId = Guid.NewGuid(), FirstName = "Sam", LastName = "User", Email = "sam@h.org", AccountStatus = AccountStatus.Active };
            user.PasswordHash = hasher.HashPassword(user, "Secret#123");
            await db.Users.AddAsync(user);
            await db.UserRoles.AddAsync(new UserRole { UserId = user.UserId, RoleId = 1 });
            await db.SaveChangesAsync();
            var auth = new AuthService(db, hasher, new JwtService(config), new PasswordResetService(db), new Mock<IEmailService>().Object,
                sessions: new SessionService(db, TenMinutes));

            var result = await auth.LoginAsync(new LoginRequestDto { Email = "sam@h.org", Password = "Secret#123" });

            var session = await db.UserSessions.SingleAsync();
            var sid = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken).Claims.Single(c => c.Type == SessionSettings.SessionClaim).Value;
            Assert.Equal(session.SessionId.ToString(), sid);
            Assert.Equal(user.UserId, session.UserId);
            Assert.Null(session.EndedAt);
            Assert.Equal(10, result.User.SessionIdleTimeoutMinutes);
            Assert.Equal(1, result.User.SessionWarningMinutes);
        }

        [Fact]
        public async Task User_Activity_Keeps_The_Session_Alive_But_Background_Requests_Do_Not()
        {
            var (db, user, sessions) = await SeedAsync();
            var session = await sessions.StartAsync(user.UserId);
            var nineMinutesAgo = DateTime.UtcNow.AddMinutes(-9);

            await SetLastActivityAsync(db, session.SessionId, nineMinutesAgo);
            Assert.Equal(SessionState.Active, await sessions.CheckAsync(session.SessionId, user.UserId, recordActivity: false));
            Assert.Equal(nineMinutesAgo, (await LoadAsync(db, session.SessionId)).LastActivityAt, TimeSpan.FromSeconds(1)); // polling: unchanged

            Assert.Equal(SessionState.Active, await sessions.CheckAsync(session.SessionId, user.UserId, recordActivity: true));
            Assert.True((await LoadAsync(db, session.SessionId)).LastActivityAt > DateTime.UtcNow.AddSeconds(-5)); // user action: moved on
        }

        [Fact]
        public async Task After_The_Idle_Timeout_The_Session_Ends_And_Its_Token_Is_Refused()
        {
            var (db, user, sessions) = await SeedAsync();
            var session = await sessions.StartAsync(user.UserId);
            await SetLastActivityAsync(db, session.SessionId, DateTime.UtcNow.AddMinutes(-10).AddSeconds(-1));

            Assert.Equal(SessionState.Idle, await sessions.CheckAsync(session.SessionId, user.UserId, recordActivity: false));

            var ended = await LoadAsync(db, session.SessionId);
            Assert.NotNull(ended.EndedAt);
            Assert.Equal(SessionEndReasons.Idle, ended.EndReason);
            Assert.Equal(SessionState.Ended, await sessions.CheckAsync(session.SessionId, user.UserId, recordActivity: true));
        }

        [Fact]
        public async Task A_Late_Request_Cannot_Revive_A_Session_That_Has_Already_Timed_Out()
        {
            var (db, user, sessions) = await SeedAsync();
            var session = await sessions.StartAsync(user.UserId);
            await SetLastActivityAsync(db, session.SessionId, DateTime.UtcNow.AddMinutes(-11));

            // Even a request marked as user activity (for example "Stay signed in" after the timeout)
            Assert.Equal(SessionState.Idle, await sessions.CheckAsync(session.SessionId, user.UserId, recordActivity: true));
            Assert.NotNull((await LoadAsync(db, session.SessionId)).EndedAt);
        }

        [Fact]
        public async Task Sign_Out_Ends_The_Session_For_Every_Tab_And_Any_Copy_Of_The_Token()
        {
            var (db, user, sessions) = await SeedAsync();
            var session = await sessions.StartAsync(user.UserId);

            await sessions.EndAsync(session.SessionId, SessionEndReasons.SignedOut);

            Assert.Equal(SessionState.Ended, await sessions.CheckAsync(session.SessionId, user.UserId, recordActivity: true));
            Assert.Equal(SessionEndReasons.SignedOut, (await LoadAsync(db, session.SessionId)).EndReason);
        }

        [Fact]
        public async Task A_Session_Id_Of_Another_User_Or_An_Unknown_Id_Is_Refused()
        {
            var (_, user, sessions) = await SeedAsync();
            var session = await sessions.StartAsync(user.UserId);

            Assert.Equal(SessionState.Ended, await sessions.CheckAsync(session.SessionId, Guid.NewGuid(), recordActivity: false));
            Assert.Equal(SessionState.Ended, await sessions.CheckAsync(Guid.NewGuid(), user.UserId, recordActivity: false));
        }

        [Fact]
        public void The_Timeout_And_Warning_Come_From_Configuration()
        {
            SessionSettings Read(params (string Key, string Value)[] values) => SessionSettings.FromConfiguration(new ConfigurationBuilder()
                .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build());

            var defaults = Read();
            Assert.Equal(TimeSpan.FromMinutes(10), defaults.IdleTimeout);
            Assert.Equal(TimeSpan.FromMinutes(1), defaults.WarningPeriod);

            var custom = Read(("Session:IdleTimeoutMinutes", "1"), ("Session:WarningMinutes", "0.33"));
            Assert.Equal(TimeSpan.FromMinutes(1), custom.IdleTimeout);
            Assert.Equal(TimeSpan.FromMinutes(0.33), custom.WarningPeriod);

            Assert.Throws<InvalidOperationException>(() => Read(("Session:IdleTimeoutMinutes", "0")));
            Assert.Throws<InvalidOperationException>(() => Read(("Session:IdleTimeoutMinutes", "5"), ("Session:WarningMinutes", "5")));
        }
    }
}
