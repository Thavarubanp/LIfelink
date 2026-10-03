using System;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;

namespace LifeLink.Services.Common
{
    /// <summary>
    /// A doctor a hospital may assign to a blood request: its own doctor, active, with a login, who has signed in and
    /// changed the temporary password (doctors "pending first login" cannot be assigned, Phase 4 / 5.1).
    /// </summary>
    public static class DoctorAssignmentRules
    {
        public const string PendingFirstLoginMessage =
            "This doctor hasn't signed in and changed the temporary password yet, so they cannot be assigned. Choose a doctor who has completed their first login.";

        /// <summary>A doctor who has completed the first login may act as the fallback decider (and be assigned).</summary>
        public static bool HasCompletedFirstLogin(Doctor doctor) => !doctor.MustChangePassword;

        public static async Task<Doctor> RequireAssignableDoctorAsync(AppDbContext context, Guid? doctorId, Guid hospitalId, string missingMessage)
        {
            if (doctorId == null || doctorId == Guid.Empty)
            {
                throw new InvalidOperationException(missingMessage);
            }

            var doctor = await context.Doctors.FindAsync(doctorId.Value);
            if (doctor == null || doctor.HospitalId != hospitalId || doctor.DeletedAt != null)
            {
                throw new InvalidOperationException("The selected doctor does not belong to your hospital.");
            }

            if (!doctor.IsActive || doctor.UserId == null)
            {
                throw new InvalidOperationException("The selected doctor does not have an active account.");
            }

            if (doctor.MustChangePassword)
            {
                throw new InvalidOperationException(PendingFirstLoginMessage);
            }

            return doctor;
        }
    }
}
