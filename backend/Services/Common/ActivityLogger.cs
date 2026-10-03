using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    /// <summary>
    /// Stages activity log entries on the caller's context, so an entry is saved in the same SaveChanges (one transaction)
    /// as the action it describes: a failed action leaves no entry. Sign-in and sign-out are deliberately not recorded.
    /// The actor's role, display name and hospital are resolved from the account (hospital staff are shown as their
    /// hospital; a doctor's entries also belong to the doctor's hospital).
    /// </summary>
    public static class ActivityLogger
    {
        /// <summary>Activity is recorded from this date on (no backfill of older actions).</summary>
        public static readonly DateTime RecordedFrom = new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Filter categories (ActivityLog.EntityType).</summary>
        public static class Types
        {
            public const string Account = "Account";
            public const string BloodRequest = "BloodRequest";
            public const string Donation = "Donation";
            public const string Screening = "Screening";
            public const string Inventory = "Inventory";
            public const string Transfer = "Transfer";
            public const string Emergency = "Emergency";
            public const string Doctor = "Doctor";
            public const string Complaint = "Complaint";
            public const string Appeal = "Appeal";
            public const string Hospital = "Hospital";
            public const string Governance = "Governance";

            public static readonly IReadOnlyList<string> All = new[]
            {
                Account, BloodRequest, Donation, Screening, Inventory, Transfer, Emergency, Doctor, Complaint, Appeal, Hospital, Governance
            };
        }

        private sealed record Actor(Guid? UserId, string Role, string Name, Guid? HospitalId);

        /// <summary>An action by a signed-in account (null actor = the system).</summary>
        public static async Task AddAsync(AppDbContext context, Guid? actorUserId, string action, string entityType, Guid? entityId,
            string summary, Guid? hospitalId = null, Guid? subjectUserId = null)
        {
            var actor = actorUserId.HasValue ? await ResolveUserAsync(context, actorUserId.Value) : new Actor(null, "System", "System", null);
            Stage(context, actor, action, entityType, entityId, summary, hospitalId ?? actor.HospitalId, subjectUserId);
        }

        /// <summary>An action by a hospital's account when only the hospital is known (hospital staff endpoints).</summary>
        public static async Task AddForHospitalAsync(AppDbContext context, Guid hospitalId, string action, string entityType, Guid? entityId,
            string summary, Guid? subjectUserId = null)
        {
            var hospital = context.Hospitals.Local.FirstOrDefault(h => h.HospitalId == hospitalId)
                           ?? await context.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.HospitalId == hospitalId);
            var email = (hospital?.Email ?? string.Empty).Trim().ToLower();
            var staffUserId = email.Length == 0 ? null : await context.Users.AsNoTracking()
                .Where(u => u.Email.ToLower() == email).Select(u => (Guid?)u.UserId).FirstOrDefaultAsync();
            Stage(context, new Actor(staffUserId, "HospitalStaff", hospital?.Name ?? "Hospital", hospitalId),
                action, entityType, entityId, summary, hospitalId, subjectUserId);
        }

        /// <summary>An action by the system (for example the expiry sweep).</summary>
        public static void AddSystem(AppDbContext context, string action, string entityType, Guid? entityId, string summary,
            Guid? hospitalId = null, Guid? subjectUserId = null) =>
            Stage(context, new Actor(null, "System", "System", null), action, entityType, entityId, summary, hospitalId, subjectUserId);

        private static void Stage(AppDbContext context, Actor actor, string action, string entityType, Guid? entityId, string summary,
            Guid? hospitalId, Guid? subjectUserId)
        {
            context.ActivityLogs.Add(new ActivityLog
            {
                OccurredAt = DateTime.UtcNow,
                ActorUserId = actor.UserId,
                ActorRole = actor.Role,
                ActorName = Truncate(actor.Name, 200),
                HospitalId = hospitalId,
                SubjectUserId = subjectUserId,
                Action = Truncate(action, 60),
                EntityType = Truncate(entityType, 40),
                EntityId = entityId,
                Summary = Truncate(summary, 500)
            });
        }

        private static async Task<Actor> ResolveUserAsync(AppDbContext context, Guid userId)
        {
            var user = context.Users.Local.FirstOrDefault(u => u.UserId == userId)
                       ?? await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return new Actor(userId, "User", "Unknown user", null);

            // Roles saved earlier plus roles added in this unit of work (for example at registration)
            var roleIds = context.UserRoles.Local.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToList();
            roleIds.AddRange(await context.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync());
            var roles = await context.Roles.AsNoTracking().Where(r => roleIds.Contains(r.RoleId)).Select(r => r.Name).ToListAsync();
            var fullName = $"{user.FirstName} {user.LastName}".Trim();

            if (roles.Contains("Admin")) return new Actor(userId, "Admin", fullName, null);

            if (roles.Contains("HospitalStaff"))
            {
                var email = (user.Email ?? string.Empty).Trim().ToLower();
                var hospital = context.Hospitals.Local.FirstOrDefault(h => (h.Email ?? string.Empty).ToLower() == email)
                               ?? await context.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Email != null && h.Email.ToLower() == email);
                return new Actor(userId, "HospitalStaff", hospital?.Name ?? fullName, hospital?.HospitalId);
            }

            if (roles.Contains("Doctor"))
            {
                var doctor = context.Doctors.Local.FirstOrDefault(d => d.UserId == userId && d.DeletedAt == null)
                             ?? await context.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.DeletedAt == null);
                var name = doctor != null ? $"Dr. {doctor.FirstName} {doctor.LastName}".Trim() : $"Dr. {fullName}";
                return new Actor(userId, "Doctor", name, doctor?.HospitalId);
            }

            return new Actor(userId, "User", fullName, null);
        }

        private static string Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);
    }
}
