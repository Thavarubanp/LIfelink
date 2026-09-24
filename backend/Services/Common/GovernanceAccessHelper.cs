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
    /// Who is "suspended for access control" (mirrors RestrictedGovernanceModeMiddleware):
    /// a suspended user, staff of a suspended hospital (matched by hospital email), or a doctor whose hospital is suspended.
    /// Doctors only get read-only access to their hospital's governance status; they never appeal or reply.
    /// </summary>
    public static class GovernanceAccessHelper
    {
        public static async Task<bool> IsSuspendedForAccessAsync(AppDbContext context, User user, IReadOnlyCollection<string> roles)
        {
            if (user.IsSuspended) return true;
            var hospital = await GetGovernedHospitalAsync(context, user, roles);
            return hospital?.IsSuspended == true;
        }

        /// <summary>The hospital whose governance applies to this account: staff by email, doctors by their hospital.</summary>
        public static async Task<Hospital?> GetGovernedHospitalAsync(AppDbContext context, User user, IReadOnlyCollection<string> roles)
        {
            if (roles.Contains("HospitalStaff") && !string.IsNullOrWhiteSpace(user.Email))
            {
                var email = user.Email.Trim().ToLower();
                return await context.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Email != null && h.Email.ToLower() == email);
            }
            if (roles.Contains("Doctor"))
            {
                return await context.Doctors.AsNoTracking().Where(d => d.UserId == user.UserId).Select(d => d.Hospital).FirstOrDefaultAsync();
            }
            return null;
        }
    }
}
