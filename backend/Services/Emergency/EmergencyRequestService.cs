using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Emergency;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Emergency
{
    public class EmergencyRequestService : IEmergencyRequestService
    {
        private readonly AppDbContext _context;

        public EmergencyRequestService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<EmergencyRequestResponseDto> CreateEmergencyRequestAsync(EmergencyRequestCreateDto dto)
        {
            if (!BloodGroup.IsValid(dto.BloodGroup))
            {
                throw new InvalidOperationException($"Invalid blood group: '{dto.BloodGroup}'.");
            }

            if (dto.UnitsRequired <= 0)
            {
                throw new InvalidOperationException("UnitsRequired must be greater than zero.");
            }

            if (!Enum.TryParse<EmergencyPriority>(dto.Priority, true, out var parsedPriority))
            {
                throw new InvalidOperationException($"Invalid priority: '{dto.Priority}'. Allowed values: Low, Medium, High, Critical.");
            }

            await EnsureHospitalExistsAsync(dto.HospitalId);

            var now = DateTime.UtcNow;
            var request = new EmergencyRequest
            {
                EmergencyRequestId = Guid.NewGuid(),
                HospitalId = dto.HospitalId,
                BloodGroup = dto.BloodGroup,
                UnitsRequired = dto.UnitsRequired,
                Priority = parsedPriority.ToString(),
                Status = EmergencyRequestStatus.Pending.ToString(),
                Reason = dto.Reason,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.EmergencyRequests.Add(request);
            await _context.SaveChangesAsync();

            return await MapToResponseDtoAsync(request.EmergencyRequestId);
        }

        public async Task<EmergencyRequestResponseDto?> GetEmergencyRequestAsync(Guid id)
        {
            var request = await _context.EmergencyRequests
                .Include(r => r.Hospital)
                .FirstOrDefaultAsync(r => r.EmergencyRequestId == id);

            if (request == null) return null;
            return MapToResponseDto(request);
        }

        public async Task<IEnumerable<EmergencyRequestResponseDto>> GetAllEmergencyRequestsAsync()
        {
            var list = await _context.EmergencyRequests
                .Include(r => r.Hospital)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        public async Task<EmergencyRequestResponseDto> ApproveEmergencyRequestAsync(Guid id)
        {
            var request = await _context.EmergencyRequests.FindAsync(id);
            if (request == null)
            {
                throw new KeyNotFoundException($"Emergency request with ID '{id}' was not found.");
            }

            if (request.Status == EmergencyRequestStatus.Completed.ToString() || request.Status == EmergencyRequestStatus.Rejected.ToString())
            {
                throw new InvalidOperationException($"Cannot approve an emergency request with status '{request.Status}'.");
            }

            var now = DateTime.UtcNow;
            request.Status = EmergencyRequestStatus.Approved.ToString();
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.EmergencyRequestId);
        }

        public async Task<EmergencyRequestResponseDto> RejectEmergencyRequestAsync(Guid id)
        {
            var request = await _context.EmergencyRequests.FindAsync(id);
            if (request == null)
            {
                throw new KeyNotFoundException($"Emergency request with ID '{id}' was not found.");
            }

            if (request.Status == EmergencyRequestStatus.Completed.ToString())
            {
                throw new InvalidOperationException("Cannot reject a completed emergency request.");
            }

            var now = DateTime.UtcNow;
            request.Status = EmergencyRequestStatus.Rejected.ToString();
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.EmergencyRequestId);
        }

        public async Task<EmergencyRequestResponseDto> CompleteEmergencyRequestAsync(Guid id)
        {
            var request = await _context.EmergencyRequests.FindAsync(id);
            if (request == null)
            {
                throw new KeyNotFoundException($"Emergency request with ID '{id}' was not found.");
            }

            if (request.Status == EmergencyRequestStatus.Rejected.ToString())
            {
                throw new InvalidOperationException("Emergency requests cannot be completed if status is rejected.");
            }

            if (request.Status == EmergencyRequestStatus.Completed.ToString())
            {
                throw new InvalidOperationException("Emergency request is already completed.");
            }

            var now = DateTime.UtcNow;
            request.Status = EmergencyRequestStatus.Completed.ToString();
            request.UpdatedAt = now;

            // Optional audit deduction: if inventory exists for this hospital & blood group, deduct stock if available
            var inventory = await _context.BloodInventories
                .FirstOrDefaultAsync(i => i.HospitalId == request.HospitalId && i.BloodGroup == request.BloodGroup);

            if (inventory != null && inventory.UnitsAvailable >= request.UnitsRequired)
            {
                inventory.UnitsAvailable -= request.UnitsRequired;
                inventory.LastUpdated = now;
                inventory.UpdatedAt = now;

                _context.InventoryTransactions.Add(new InventoryTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    InventoryId = inventory.InventoryId,
                    TransactionType = TransactionType.EmergencyDispatch,
                    Units = request.UnitsRequired,
                    Notes = $"Emergency request '{id}' completed and dispatched.",
                    CreatedAt = now
                });
            }

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.EmergencyRequestId);
        }

        public async Task<IEnumerable<EmergencyRequestResponseDto>> GetCriticalEmergencyRequestsAsync()
        {
            var list = await _context.EmergencyRequests
                .Include(r => r.Hospital)
                .Where(r => r.Priority == EmergencyPriority.Critical.ToString())
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        private async Task EnsureHospitalExistsAsync(Guid hospitalId)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            if (hospital == null)
            {
                var now = DateTime.UtcNow;
                hospital = new Hospital
                {
                    HospitalId = hospitalId,
                    Name = $"Hospital {hospitalId.ToString()[..8]}",
                    LicenseNumber = $"LIC-{hospitalId.ToString()[..6].ToUpper()}",
                    Address = "Default Address",
                    ContactNumber = "+1000000000",
                    Email = $"hospital_{hospitalId.ToString()[..8]}@lifelink.org",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.Hospitals.Add(hospital);
                await _context.SaveChangesAsync();
            }
        }

        private async Task<EmergencyRequestResponseDto> MapToResponseDtoAsync(Guid id)
        {
            var request = await _context.EmergencyRequests
                .Include(r => r.Hospital)
                .FirstAsync(r => r.EmergencyRequestId == id);

            return MapToResponseDto(request);
        }

        private static EmergencyRequestResponseDto MapToResponseDto(EmergencyRequest r)
        {
            return new EmergencyRequestResponseDto
            {
                EmergencyRequestId = r.EmergencyRequestId,
                HospitalId = r.HospitalId,
                HospitalName = r.Hospital != null ? r.Hospital.Name : string.Empty,
                BloodGroup = r.BloodGroup,
                UnitsRequired = r.UnitsRequired,
                Priority = r.Priority,
                Status = r.Status,
                Reason = r.Reason,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }
    }
}
