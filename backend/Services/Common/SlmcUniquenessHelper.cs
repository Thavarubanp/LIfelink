using System;
using System.Threading.Tasks;
using LifeLink.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LifeLink.Services.Common
{
    /// <summary>
    /// Doctor SLMC numbers (Doctor.LicenseNumber) are unique system-wide. They are stored trimmed and
    /// upper-cased, so the unique index on Doctors.LicenseNumber also rejects case/whitespace variants.
    /// </summary>
    public static class SlmcUniquenessHelper
    {
        public const string DuplicateMessage = "A doctor with this SLMC number already exists.";

        public static string Normalize(string? rawSlmc) => (rawSlmc ?? string.Empty).Trim().ToUpperInvariant();

        public static async Task<bool> IsSlmcTakenAsync(AppDbContext context, string rawSlmc, Guid? excludeDoctorId = null)
        {
            var normalized = Normalize(rawSlmc);
            if (normalized.Length == 0)
                return false;

            return await context.Doctors.AnyAsync(d =>
                d.LicenseNumber.Trim().ToUpper() == normalized &&
                (excludeDoctorId == null || d.DoctorId != excludeDoctorId));
        }

        /// <summary>True when a save failed on the SLMC unique index (e.g. two concurrent requests).</summary>
        public static bool IsSlmcUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg &&
            pg.ConstraintName != null &&
            pg.ConstraintName.Contains("LicenseNumber", StringComparison.OrdinalIgnoreCase);
    }
}
