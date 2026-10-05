using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Notification
{
    /// <summary>Builds Notification rows for the existing notification centre (added by the caller's SaveChanges).</summary>
    public static class NotificationFactory
    {
        public static LifeLink.Entities.Notification ForUser(Guid userId, string recipientRole, string type, string title, string message) => new()
        {
            NotificationId = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            NotificationType = type,
            RecipientRole = recipientRole,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        public static LifeLink.Entities.Notification ForHospital(Guid hospitalId, string type, string title, string message) => new()
        {
            NotificationId = Guid.NewGuid(),
            HospitalId = hospitalId,
            Title = title,
            Message = message,
            NotificationType = type,
            RecipientRole = "HospitalStaff",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        /// <summary>The login of a doctor (null when the doctor or their login was removed).</summary>
        public static Task<Guid?> DoctorUserIdAsync(AppDbContext context, Guid? doctorId) =>
            doctorId == null
                ? Task.FromResult<Guid?>(null)
                : context.Doctors.Where(d => d.DoctorId == doctorId && d.DeletedAt == null).Select(d => d.UserId).FirstOrDefaultAsync();

        /// <summary>Login of a doctor who is currently able to act in the supplied hospital workflow.</summary>
        public static Task<Guid?> EligibleDoctorUserIdAsync(AppDbContext context, Guid? doctorId, Guid? hospitalId = null) =>
            doctorId == null
                ? Task.FromResult<Guid?>(null)
                : context.Doctors
                    .Where(d => d.DoctorId == doctorId && (!hospitalId.HasValue || d.HospitalId == hospitalId.Value) &&
                                d.IsActive && !d.MustChangePassword && d.DeletedAt == null && d.UserId != null &&
                                context.Users.Any(u => u.UserId == d.UserId.Value && u.AccountStatus == AccountStatus.Active &&
                                                       !u.IsSuspended && !u.IsPermanentlyBlocked))
                    .Select(d => d.UserId)
                    .FirstOrDefaultAsync();

        public static string ShortId(Guid id) => id.ToString()[..8];
    }
}
