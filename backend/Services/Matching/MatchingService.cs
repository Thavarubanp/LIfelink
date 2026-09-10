using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Matching;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Matching
{
    public class MatchingService : IMatchingService
    {
        private readonly AppDbContext _context;

        public MatchingService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<DonorPatientMatchResponseDto> CreateMatchAsync(CreateMatchDto dto)
        {
            var doctor = await _context.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null)
            {
                throw new InvalidOperationException($"Doctor with ID {dto.DoctorId} was not found.");
            }

            var donorUser = await _context.Users.FindAsync(dto.DonorUserId);

            // Check if emergency request exists (Student 3 table integration if applicable)
            var emergencyReq = await _context.EmergencyRequests
                .FirstOrDefaultAsync(e => e.EmergencyRequestId == dto.BloodRequestId);

            if (emergencyReq != null)
            {
                // Remove request from public dashboard
                emergencyReq.Status = EmergencyRequestStatus.Completed.ToString();
                emergencyReq.UpdatedAt = DateTime.UtcNow;
            }

            var match = new DonorPatientMatch
            {
                MatchId = Guid.NewGuid(),
                BloodRequestId = dto.BloodRequestId,
                DonorUserId = dto.DonorUserId,
                DoctorId = dto.DoctorId,
                Status = MatchStatus.Matched,
                IsRemovedFromPublicDashboard = true,
                Notes = dto.Notes,
                MatchedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _context.DonorPatientMatches.AddAsync(match);
            await _context.SaveChangesAsync();

            var donorName = donorUser != null ? $"{donorUser.FirstName} {donorUser.LastName}" : null;

            return new DonorPatientMatchResponseDto
            {
                MatchId = match.MatchId,
                BloodRequestId = match.BloodRequestId,
                DonorUserId = match.DonorUserId,
                DonorName = donorName,
                DoctorId = match.DoctorId,
                DoctorName = $"{doctor.FirstName} {doctor.LastName}",
                Status = match.Status.ToString(),
                IsRemovedFromPublicDashboard = match.IsRemovedFromPublicDashboard,
                Notes = match.Notes,
                MatchedAt = match.MatchedAt,
                CreatedAt = match.CreatedAt
            };
        }

        public async Task<List<DonorPatientMatchResponseDto>> GetMatchesAsync()
        {
            var list = await _context.DonorPatientMatches
                .Include(m => m.Doctor)
                .Include(m => m.DonorUser)
                .OrderByDescending(m => m.MatchedAt)
                .ToListAsync();

            return list.Select(m => new DonorPatientMatchResponseDto
            {
                MatchId = m.MatchId,
                BloodRequestId = m.BloodRequestId,
                DonorUserId = m.DonorUserId,
                DonorName = m.DonorUser != null ? $"{m.DonorUser.FirstName} {m.DonorUser.LastName}" : null,
                DoctorId = m.DoctorId,
                DoctorName = m.Doctor != null ? $"{m.Doctor.FirstName} {m.Doctor.LastName}" : string.Empty,
                Status = m.Status.ToString(),
                IsRemovedFromPublicDashboard = m.IsRemovedFromPublicDashboard,
                Notes = m.Notes,
                MatchedAt = m.MatchedAt,
                CreatedAt = m.CreatedAt
            }).ToList();
        }

        public async Task<DonorPatientMatchResponseDto?> GetMatchByIdAsync(Guid matchId)
        {
            var m = await _context.DonorPatientMatches
                .Include(match => match.Doctor)
                .Include(match => match.DonorUser)
                .FirstOrDefaultAsync(match => match.MatchId == matchId);

            if (m == null) return null;

            return new DonorPatientMatchResponseDto
            {
                MatchId = m.MatchId,
                BloodRequestId = m.BloodRequestId,
                DonorUserId = m.DonorUserId,
                DonorName = m.DonorUser != null ? $"{m.DonorUser.FirstName} {m.DonorUser.LastName}" : null,
                DoctorId = m.DoctorId,
                DoctorName = m.Doctor != null ? $"{m.Doctor.FirstName} {m.Doctor.LastName}" : string.Empty,
                Status = m.Status.ToString(),
                IsRemovedFromPublicDashboard = m.IsRemovedFromPublicDashboard,
                Notes = m.Notes,
                MatchedAt = m.MatchedAt,
                CreatedAt = m.CreatedAt
            };
        }
    }
}
