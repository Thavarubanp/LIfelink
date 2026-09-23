using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Transfer;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Transfer
{
    public class TransferRequestService : ITransferRequestService
    {
        private readonly AppDbContext _context;

        public TransferRequestService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<TransferRequestResponseDto> CreateTransferRequestAsync(TransferRequestCreateDto dto)
        {
            if (dto.SenderHospitalId == dto.ReceiverHospitalId)
            {
                throw new InvalidOperationException("SenderHospitalId and ReceiverHospitalId cannot be the same.");
            }

            if (!BloodGroup.IsValid(dto.BloodGroup))
            {
                throw new InvalidOperationException($"Invalid blood group: '{dto.BloodGroup}'.");
            }

            if (dto.UnitsRequested <= 0)
            {
                throw new InvalidOperationException("UnitsRequested must be greater than zero.");
            }

            await EnsureHospitalExistsAsync(dto.SenderHospitalId);
            await EnsureHospitalExistsAsync(dto.ReceiverHospitalId);

            var now = DateTime.UtcNow;
            var request = new HospitalTransferRequest
            {
                TransferRequestId = Guid.NewGuid(),
                SenderHospitalId = dto.SenderHospitalId,
                ReceiverHospitalId = dto.ReceiverHospitalId,
                BloodGroup = dto.BloodGroup,
                UnitsRequested = dto.UnitsRequested,
                Status = TransferRequestStatus.Pending.ToString(),
                Notes = dto.Notes ?? string.Empty,
                RequestedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.HospitalTransferRequests.Add(request);
            await _context.SaveChangesAsync();

            return await MapToResponseDtoAsync(request.TransferRequestId);
        }

        public async Task<TransferRequestResponseDto?> GetTransferRequestAsync(Guid id)
        {
            var request = await _context.HospitalTransferRequests
                .Include(r => r.SenderHospital)
                .Include(r => r.ReceiverHospital)
                .FirstOrDefaultAsync(r => r.TransferRequestId == id);

            if (request == null) return null;
            return MapToResponseDto(request);
        }

        public async Task<IEnumerable<TransferRequestResponseDto>> GetAllTransferRequestsAsync()
        {
            var list = await _context.HospitalTransferRequests
                .Include(r => r.SenderHospital)
                .Include(r => r.ReceiverHospital)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        public async Task<TransferRequestResponseDto> ApproveTransferRequestAsync(Guid id)
        {
            var request = await _context.HospitalTransferRequests.FindAsync(id);
            if (request == null)
            {
                throw new KeyNotFoundException($"Transfer request with ID '{id}' was not found.");
            }

            if (request.Status == TransferRequestStatus.Completed.ToString())
            {
                throw new InvalidOperationException("Completed transfer requests cannot be modified.");
            }

            if (request.Status == TransferRequestStatus.Rejected.ToString())
            {
                throw new InvalidOperationException("Cannot approve a rejected transfer request.");
            }

            var now = DateTime.UtcNow;
            request.Status = TransferRequestStatus.Approved.ToString();
            request.ApprovedAt = now;
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.TransferRequestId);
        }

        public async Task<TransferRequestResponseDto> RejectTransferRequestAsync(Guid id)
        {
            var request = await _context.HospitalTransferRequests.FindAsync(id);
            if (request == null)
            {
                throw new KeyNotFoundException($"Transfer request with ID '{id}' was not found.");
            }

            if (request.Status == TransferRequestStatus.Approved.ToString())
            {
                throw new InvalidOperationException("Approved transfer requests cannot be rejected.");
            }

            if (request.Status == TransferRequestStatus.Completed.ToString())
            {
                throw new InvalidOperationException("Completed transfer requests cannot be modified.");
            }

            var now = DateTime.UtcNow;
            request.Status = TransferRequestStatus.Rejected.ToString();
            request.RejectedAt = now;
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.TransferRequestId);
        }

        public async Task<TransferRequestResponseDto> CompleteTransferRequestAsync(Guid id)
        {
            var request = await _context.HospitalTransferRequests.FindAsync(id);
            if (request == null)
            {
                throw new KeyNotFoundException($"Transfer request with ID '{id}' was not found.");
            }

            if (request.Status == TransferRequestStatus.Completed.ToString())
            {
                throw new InvalidOperationException("Completed transfer requests cannot be modified.");
            }

            if (request.Status == TransferRequestStatus.Rejected.ToString())
            {
                throw new InvalidOperationException("Cannot complete a rejected transfer request.");
            }

            var now = DateTime.UtcNow;

            // Process Stock Transfer Audit Transactions
            var senderInventory = await _context.BloodInventories
                .FirstOrDefaultAsync(i => i.HospitalId == request.SenderHospitalId && i.BloodGroup == request.BloodGroup);

            if (senderInventory != null)
            {
                senderInventory.UnitsAvailable = Math.Max(0, senderInventory.UnitsAvailable - request.UnitsRequested);
                senderInventory.LastUpdated = now;
                senderInventory.UpdatedAt = now;

                _context.InventoryTransactions.Add(new InventoryTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    InventoryId = senderInventory.InventoryId,
                    TransactionType = TransactionType.TransferOut,
                    Units = request.UnitsRequested,
                    Notes = $"Transfer request '{id}' completed. Transferred out to Hospital '{request.ReceiverHospitalId}'.",
                    CreatedAt = now
                });
            }

            var receiverInventory = await _context.BloodInventories
                .FirstOrDefaultAsync(i => i.HospitalId == request.ReceiverHospitalId && i.BloodGroup == request.BloodGroup);

            if (receiverInventory != null)
            {
                receiverInventory.UnitsAvailable = Math.Min(receiverInventory.MaximumCapacity, receiverInventory.UnitsAvailable + request.UnitsRequested);
                receiverInventory.LastUpdated = now;
                receiverInventory.UpdatedAt = now;

                _context.InventoryTransactions.Add(new InventoryTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    InventoryId = receiverInventory.InventoryId,
                    TransactionType = TransactionType.TransferIn,
                    Units = request.UnitsRequested,
                    Notes = $"Transfer request '{id}' completed. Transferred in from Hospital '{request.SenderHospitalId}'.",
                    CreatedAt = now
                });
            }

            request.Status = TransferRequestStatus.Completed.ToString();
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.TransferRequestId);
        }

        public async Task<IEnumerable<TransferRequestResponseDto>> GetPendingTransferRequestsAsync()
        {
            var list = await _context.HospitalTransferRequests
                .Include(r => r.SenderHospital)
                .Include(r => r.ReceiverHospital)
                .Where(r => r.Status == TransferRequestStatus.Pending.ToString())
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        private async Task EnsureHospitalExistsAsync(Guid hospitalId)
        {
            // An empty ID would make EF generate a fresh key, adding a new "Hospital 00000000" placeholder on every call
            if (hospitalId == Guid.Empty)
                throw new InvalidOperationException("A valid hospital ID is required.");

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

        private async Task<TransferRequestResponseDto> MapToResponseDtoAsync(Guid id)
        {
            var request = await _context.HospitalTransferRequests
                .Include(r => r.SenderHospital)
                .Include(r => r.ReceiverHospital)
                .FirstAsync(r => r.TransferRequestId == id);

            return MapToResponseDto(request);
        }

        private static TransferRequestResponseDto MapToResponseDto(HospitalTransferRequest r)
        {
            return new TransferRequestResponseDto
            {
                TransferRequestId = r.TransferRequestId,
                SenderHospitalId = r.SenderHospitalId,
                SenderHospitalName = r.SenderHospital != null ? r.SenderHospital.Name : string.Empty,
                ReceiverHospitalId = r.ReceiverHospitalId,
                ReceiverHospitalName = r.ReceiverHospital != null ? r.ReceiverHospital.Name : string.Empty,
                BloodGroup = r.BloodGroup,
                UnitsRequested = r.UnitsRequested,
                Status = r.Status,
                Notes = r.Notes,
                RequestedAt = r.RequestedAt,
                ApprovedAt = r.ApprovedAt,
                RejectedAt = r.RejectedAt,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }
    }
}
