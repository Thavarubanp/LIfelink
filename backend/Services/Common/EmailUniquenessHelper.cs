using System;
using System.Threading.Tasks;
using LifeLink.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    public static class EmailUniquenessHelper
    {
        public static async Task<bool> IsEmailTakenAsync(AppDbContext context, string rawEmail)
        {
            if (string.IsNullOrWhiteSpace(rawEmail))
                return false;

            var normalizedEmail = rawEmail.Trim().ToLowerInvariant();

            // 1. Check in Users table (User, HospitalStaff, Doctor, Admin)
            var existsInUsers = await context.Users
                .AnyAsync(u => u.Email.ToLower() == normalizedEmail);
            if (existsInUsers) return true;

            // 2. Check in Hospitals table
            var existsInHospitals = await context.Hospitals
                .AnyAsync(h => h.Email != null && h.Email.ToLower() == normalizedEmail);
            if (existsInHospitals) return true;

            // 3. Check in Doctors table
            var existsInDoctors = await context.Doctors
                .AnyAsync(d => d.Email != null && d.Email.ToLower() == normalizedEmail);
            if (existsInDoctors) return true;

            return false;
        }
    }
}
