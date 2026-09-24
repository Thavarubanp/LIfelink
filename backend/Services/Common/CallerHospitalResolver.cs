using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    /// <summary>The hospital a signed-in staff account (by hospital email) or doctor (by doctor record) belongs to.</summary>
    public static class CallerHospitalResolver
    {
        public static async Task<Guid?> ResolveAsync(AppDbContext context, ICurrentUserService currentUser)
        {
            var roles = currentUser.Roles.ToList();
            if (roles.Contains("HospitalStaff") && !string.IsNullOrWhiteSpace(currentUser.Email))
            {
                var email = currentUser.Email.Trim().ToLower();
                return await context.Hospitals
                    .Where(h => h.Email != null && h.Email.ToLower() == email)
                    .Select(h => (Guid?)h.HospitalId)
                    .FirstOrDefaultAsync();
            }

            if (roles.Contains("Doctor") && currentUser.UserId.HasValue)
            {
                return await context.Doctors
                    .Where(d => d.UserId == currentUser.UserId && d.IsActive)
                    .Select(d => (Guid?)d.HospitalId)
                    .FirstOrDefaultAsync();
            }

            return null;
        }
    }
}
