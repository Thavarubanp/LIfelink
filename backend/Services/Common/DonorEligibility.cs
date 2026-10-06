using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    /// <summary>
    /// Who may take part in donor workflows (accept, screening, approval, donation, donor alerts).
    /// Governance rules apply as they are: only Active, non-suspended, non-blocked accounts that are plain
    /// donor-capable interactive accounts (User or Admin, including the legacy no-role User fallback), but never
    /// HospitalStaff or Doctor accounts. Admin authority does not bypass any clinical or lifecycle rule.
    /// </summary>
    public static class DonorEligibility
    {
        public const int DonationIntervalDays = 120;
        public const int MinimumAge = 18;
        public const int MaximumAge = 60;

        public static readonly string[] DonorCapableRoles = { "User", "Admin" };
        public static readonly string[] NonDonorRoles = { "HospitalStaff", "Doctor" };

        public static bool IsActiveAccount(User user) =>
            user.AccountStatus == AccountStatus.Active && !user.IsSuspended && !user.IsPermanentlyBlocked;

        public static DateTime? NextEligibleDate(User user) =>
            user.LastDonationDate?.AddDays(DonationIntervalDays);

        public static bool IsIntervalSatisfied(User user, DateTime now) =>
            user.LastDonationDate == null || user.LastDonationDate.Value.AddDays(DonationIntervalDays) <= now;

        public static int? Age(DateTime? dateOfBirth, DateTime now)
        {
            if (dateOfBirth == null) return null;
            var dob = dateOfBirth.Value.Date;
            var age = now.Year - dob.Year;
            if (dob > now.Date.AddYears(-age)) age--;
            return age;
        }

        // Unknown date of birth is allowed here; the screening questionnaire checks age again
        public static bool IsAgeEligible(DateTime? dateOfBirth, DateTime now)
        {
            var age = Age(dateOfBirth, now);
            return age == null || (age >= MinimumAge && age <= MaximumAge);
        }

        public static IQueryable<Guid> NonDonorUserIds(AppDbContext context) =>
            context.UserRoles.Where(ur => NonDonorRoles.Contains(ur.Role.Name)).Select(ur => ur.UserId);

        /// <summary>Throws unless the account may take part in donor workflows right now.</summary>
        public static async Task<User> RequireEligibleDonorAccountAsync(AppDbContext context, Guid userId)
        {
            var user = await context.Users.FindAsync(userId)
                       ?? throw new InvalidOperationException("Donor account was not found.");

            if (!IsActiveAccount(user))
            {
                throw new InvalidOperationException("This account is not active and cannot take part in blood donation.");
            }

            var roles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.Role.Name)
                .ToListAsync();

            if (roles.Any(role => NonDonorRoles.Contains(role)) ||
                (roles.Count > 0 && !roles.Any(role => DonorCapableRoles.Contains(role))))
            {
                throw new InvalidOperationException("Only donor accounts can donate blood.");
            }

            return user;
        }

        /// <summary>A recorded donation confirms the tested blood group; after that the donor cannot change it.</summary>
        public static Task<bool> IsBloodGroupConfirmedAsync(AppDbContext context, Guid userId) =>
            context.Acceptances.AnyAsync(a => a.DonorUserId == userId && a.Status == AcceptanceStatus.Matched);
    }
}
