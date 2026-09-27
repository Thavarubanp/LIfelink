using System;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;

namespace LifeLink.Services.Common
{
    /// <summary>A doctor a hospital may assign to a blood request: its own doctor, active, with a login.</summary>
    public static class DoctorAssignmentRules
    {
        public static async Task<Doctor> RequireAssignableDoctorAsync(AppDbContext context, Guid? doctorId, Guid hospitalId, string missingMessage)
        {
            if (doctorId == null || doctorId == Guid.Empty)
            {
                throw new InvalidOperationException(missingMessage);
            }

            var doctor = await context.Doctors.FindAsync(doctorId.Value);
            if (doctor == null || doctor.HospitalId != hospitalId)
            {
                throw new InvalidOperationException("The selected doctor does not belong to your hospital.");
            }

            if (!doctor.IsActive || doctor.UserId == null)
            {
                throw new InvalidOperationException("The selected doctor does not have an active account.");
            }

            return doctor;
        }
    }
}
