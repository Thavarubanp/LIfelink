using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Verification;
using LifeLink.Entities;
using LifeLink.Services.Notification;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Verification
{
    public class VerificationService : IVerificationService
    {
        private readonly AppDbContext _context;
        private readonly INotificationAgentService _notificationAgent;

        public VerificationService(AppDbContext context, INotificationAgentService notificationAgent)
        {
            _context = context;
            _notificationAgent = notificationAgent;
        }

        public async Task<BloodRequestVerificationResponseDto> ApproveBloodRequestAsync(Guid requestId, ApproveRejectRequestDto dto)
        {
            var doctor = await _context.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null)
            {
                throw new InvalidOperationException($"Doctor with ID {dto.DoctorId} was not found.");
            }

            var verification = await _context.BloodRequestVerifications
                .FirstOrDefaultAsync(v => v.BloodRequestId == requestId || v.VerificationId == requestId);

            if (verification == null)
            {
                verification = new BloodRequestVerification
                {
                    VerificationId = Guid.NewGuid(),
                    BloodRequestId = requestId,
                    DoctorId = dto.DoctorId,
                    Status = VerificationStatus.Approved,
                    Notes = dto.Notes,
                    VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.BloodRequestVerifications.AddAsync(verification);
            }
            else
            {
                verification.DoctorId = dto.DoctorId;
                verification.Status = VerificationStatus.Approved;
                verification.Notes = dto.Notes;
                verification.VerifiedAt = DateTime.UtcNow;
                verification.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            // Trigger NotificationAgentService ONLY upon hospital approval of blood request
            var bloodGroup = !string.IsNullOrWhiteSpace(dto.BloodGroup) ? dto.BloodGroup : "O+";
            var hospitalId = dto.HospitalId ?? doctor.HospitalId;
            var priority = !string.IsNullOrWhiteSpace(dto.Priority) ? dto.Priority : "Normal";

            await _notificationAgent.ProcessRequestApprovalNotificationAsync(verification.BloodRequestId, bloodGroup, hospitalId, priority);

            return new BloodRequestVerificationResponseDto
            {
                VerificationId = verification.VerificationId,
                BloodRequestId = verification.BloodRequestId,
                DoctorId = verification.DoctorId,
                DoctorName = $"{doctor.FirstName} {doctor.LastName}",
                Status = verification.Status.ToString(),
                Notes = verification.Notes,
                VerifiedAt = verification.VerifiedAt,
                CreatedAt = verification.CreatedAt
            };
        }

        public async Task<BloodRequestVerificationResponseDto> RejectBloodRequestAsync(Guid requestId, ApproveRejectRequestDto dto)
        {
            var doctor = await _context.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null)
            {
                throw new InvalidOperationException($"Doctor with ID {dto.DoctorId} was not found.");
            }

            var verification = await _context.BloodRequestVerifications
                .FirstOrDefaultAsync(v => v.BloodRequestId == requestId || v.VerificationId == requestId);

            if (verification == null)
            {
                verification = new BloodRequestVerification
                {
                    VerificationId = Guid.NewGuid(),
                    BloodRequestId = requestId,
                    DoctorId = dto.DoctorId,
                    Status = VerificationStatus.Rejected,
                    Notes = dto.Notes,
                    VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.BloodRequestVerifications.AddAsync(verification);
            }
            else
            {
                verification.DoctorId = dto.DoctorId;
                verification.Status = VerificationStatus.Rejected;
                verification.Notes = dto.Notes;
                verification.VerifiedAt = DateTime.UtcNow;
                verification.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return new BloodRequestVerificationResponseDto
            {
                VerificationId = verification.VerificationId,
                BloodRequestId = verification.BloodRequestId,
                DoctorId = verification.DoctorId,
                DoctorName = $"{doctor.FirstName} {doctor.LastName}",
                Status = verification.Status.ToString(),
                Notes = verification.Notes,
                VerifiedAt = verification.VerifiedAt,
                CreatedAt = verification.CreatedAt
            };
        }

        public async Task<DonorVerificationResponseDto> ApproveDonorVerificationAsync(Guid id, ApproveRejectRequestDto dto)
        {
            var doctor = await _context.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null)
            {
                throw new InvalidOperationException($"Doctor with ID {dto.DoctorId} was not found.");
            }

            var verification = await _context.DonorVerifications
                .FirstOrDefaultAsync(v => v.AcceptanceId == id || v.DonorVerificationId == id);

            if (verification == null)
            {
                verification = new DonorVerification
                {
                    DonorVerificationId = Guid.NewGuid(),
                    AcceptanceId = id,
                    DoctorId = dto.DoctorId,
                    Status = VerificationStatus.Approved,
                    MedicalReportSummary = dto.MedicalReportSummary,
                    Notes = dto.Notes,
                    VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.DonorVerifications.AddAsync(verification);
            }
            else
            {
                verification.DoctorId = dto.DoctorId;
                verification.Status = VerificationStatus.Approved;
                verification.MedicalReportSummary = dto.MedicalReportSummary;
                verification.Notes = dto.Notes;
                verification.VerifiedAt = DateTime.UtcNow;
                verification.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return new DonorVerificationResponseDto
            {
                DonorVerificationId = verification.DonorVerificationId,
                AcceptanceId = verification.AcceptanceId,
                DoctorId = verification.DoctorId,
                DoctorName = $"{doctor.FirstName} {doctor.LastName}",
                Status = verification.Status.ToString(),
                MedicalReportSummary = verification.MedicalReportSummary,
                Notes = verification.Notes,
                VerifiedAt = verification.VerifiedAt,
                CreatedAt = verification.CreatedAt
            };
        }

        public async Task<DonorVerificationResponseDto> RejectDonorVerificationAsync(Guid id, ApproveRejectRequestDto dto)
        {
            var doctor = await _context.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null)
            {
                throw new InvalidOperationException($"Doctor with ID {dto.DoctorId} was not found.");
            }

            var verification = await _context.DonorVerifications
                .FirstOrDefaultAsync(v => v.AcceptanceId == id || v.DonorVerificationId == id);

            if (verification == null)
            {
                verification = new DonorVerification
                {
                    DonorVerificationId = Guid.NewGuid(),
                    AcceptanceId = id,
                    DoctorId = dto.DoctorId,
                    Status = VerificationStatus.Rejected,
                    MedicalReportSummary = dto.MedicalReportSummary,
                    Notes = dto.Notes,
                    VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.DonorVerifications.AddAsync(verification);
            }
            else
            {
                verification.DoctorId = dto.DoctorId;
                verification.Status = VerificationStatus.Rejected;
                verification.MedicalReportSummary = dto.MedicalReportSummary;
                verification.Notes = dto.Notes;
                verification.VerifiedAt = DateTime.UtcNow;
                verification.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return new DonorVerificationResponseDto
            {
                DonorVerificationId = verification.DonorVerificationId,
                AcceptanceId = verification.AcceptanceId,
                DoctorId = verification.DoctorId,
                DoctorName = $"{doctor.FirstName} {doctor.LastName}",
                Status = verification.Status.ToString(),
                MedicalReportSummary = verification.MedicalReportSummary,
                Notes = verification.Notes,
                VerifiedAt = verification.VerifiedAt,
                CreatedAt = verification.CreatedAt
            };
        }

        public async Task<List<BloodRequestVerificationResponseDto>> GetBloodRequestVerificationsAsync()
        {
            var list = await _context.BloodRequestVerifications
                .Include(v => v.Doctor)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            return list.Select(v => new BloodRequestVerificationResponseDto
            {
                VerificationId = v.VerificationId,
                BloodRequestId = v.BloodRequestId,
                DoctorId = v.DoctorId,
                DoctorName = v.Doctor != null ? $"{v.Doctor.FirstName} {v.Doctor.LastName}" : string.Empty,
                Status = v.Status.ToString(),
                Notes = v.Notes,
                VerifiedAt = v.VerifiedAt,
                CreatedAt = v.CreatedAt
            }).ToList();
        }

        public async Task<List<DonorVerificationResponseDto>> GetDonorVerificationsAsync()
        {
            var list = await _context.DonorVerifications
                .Include(v => v.Doctor)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            return list.Select(v => new DonorVerificationResponseDto
            {
                DonorVerificationId = v.DonorVerificationId,
                AcceptanceId = v.AcceptanceId,
                DoctorId = v.DoctorId,
                DoctorName = v.Doctor != null ? $"{v.Doctor.FirstName} {v.Doctor.LastName}" : string.Empty,
                Status = v.Status.ToString(),
                MedicalReportSummary = v.MedicalReportSummary,
                Notes = v.Notes,
                VerifiedAt = v.VerifiedAt,
                CreatedAt = v.CreatedAt
            }).ToList();
        }
    }
}
