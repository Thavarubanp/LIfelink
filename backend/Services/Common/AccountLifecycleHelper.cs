using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    /// <summary>
    /// Permanent block and self-delete for donor/patient (User) accounts. Nothing historical is deleted:
    /// blood requests, matches, donations, complaints, appeals and audit rows keep pointing at the retained,
    /// anonymized user row. Only personal details, access (roles, reset tokens) and notifications are removed.
    /// </summary>
    public static class AccountLifecycleHelper
    {
        public const string DeletedUserName = "Deleted User";

        /// <summary>Only plain donor/patient accounts (no Admin, HospitalStaff or Doctor role) can be blocked or self-deleted.</summary>
        public static async Task EnsureDonorPatientAccountAsync(AppDbContext context, Guid userId, string action)
        {
            var roles = await context.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.Role.Name).ToListAsync();
            if (roles.Any(r => r is "Admin" or "HospitalStaff" or "Doctor"))
            {
                throw new InvalidOperationException($"Only donor/patient accounts can be {action}.");
            }
        }

        public static bool IsRemoved(User user) => user.AccountStatus is AccountStatus.Blocked or AccountStatus.Deleted;

        /// <summary>
        /// Permanently blocked: name kept (visible with a "Permanently Blocked" badge), email kept internally so it can
        /// never register again; contact details, roles, reset tokens and notifications removed; login refused by status.
        /// </summary>
        public static async Task PermanentlyBlockAsync(AppDbContext context, User user)
        {
            await RemoveAccessAsync(context, user);
            user.PhoneNumber = string.Empty;
            user.Address = string.Empty;
            user.Gender = string.Empty;
            user.DateOfBirth = null;
            user.IsPermanentlyBlocked = true;
            user.AccountStatus = AccountStatus.Blocked;
        }

        /// <summary>
        /// Self-delete: personal data and login removed; the email is replaced by a unique placeholder so the same
        /// email can register again. The row stays so historical records remain intact (shown as "Deleted User").
        /// </summary>
        public static async Task DeleteAccountAsync(AppDbContext context, User user)
        {
            await RemoveAccessAsync(context, user);
            user.FirstName = "Deleted";
            user.LastName = "User";
            user.Email = $"deleted-{user.UserId:N}@deleted.lifelink.invalid";
            user.PhoneNumber = string.Empty;
            user.Address = string.Empty;
            user.Gender = string.Empty;
            user.DateOfBirth = null;
            user.PasswordHash = string.Empty;
            user.AccountStatus = AccountStatus.Deleted;
        }

        private static async Task RemoveAccessAsync(AppDbContext context, User user)
        {
            context.UserRoles.RemoveRange(await context.UserRoles.Where(ur => ur.UserId == user.UserId).ToListAsync());
            context.PasswordResetTokens.RemoveRange(await context.PasswordResetTokens.Where(t => t.UserId == user.UserId).ToListAsync());
            context.Notifications.RemoveRange(await context.Notifications.Where(n => n.UserId == user.UserId).ToListAsync());

            // Their open appeal threads end with the account
            foreach (var appeal in await context.Appeals.Where(a => a.UserId == user.UserId && a.Status != AppealStatus.CLOSED && a.Status != AppealStatus.APPROVED).ToListAsync())
            {
                appeal.Status = AppealStatus.CLOSED;
            }

            user.IsSuspended = false;
            user.SuspendedUntil = null;
            user.SuspensionReason = null;
            user.BloodGroup = null;
            user.LastDonationDate = null;
            user.UpdatedAt = DateTime.UtcNow;
        }
    }
}
