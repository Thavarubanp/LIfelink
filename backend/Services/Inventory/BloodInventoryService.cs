using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Inventory;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Inventory
{
    /// <summary>
    /// Blood group categories (thresholds and capacity) and packet-level stock. UnitsAvailable is the count of
    /// Available packets and changes only through InventoryLedger: recorded donations, completed transfers,
    /// issuing and expiry. It can never be raised by hand.
    /// </summary>
    public class BloodInventoryService : IBloodInventoryService
    {
        private readonly AppDbContext _context;

        public BloodInventoryService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<InventoryResponseDto> CreateInventoryAsync(CreateInventoryDto dto)
        {
            if (!BloodGroup.IsValid(dto.BloodGroup))
            {
                throw new InvalidOperationException($"Invalid blood group: '{dto.BloodGroup}'. Allowed values are A+, A-, B+, B-, AB+, AB-, O+, O-.");
            }

            if (dto.MinimumThreshold < 0)
            {
                throw new InvalidOperationException("MinimumThreshold cannot be negative.");
            }

            if (dto.MaximumCapacity < dto.MinimumThreshold)
            {
                throw new InvalidOperationException("MaximumCapacity cannot be less than MinimumThreshold.");
            }

            await EnsureHospitalExistsAsync(dto.HospitalId);

            var existing = await _context.BloodInventories
                .FirstOrDefaultAsync(i => i.HospitalId == dto.HospitalId && i.BloodGroup == dto.BloodGroup);

            if (existing != null)
            {
                throw new InvalidOperationException($"Blood inventory for hospital '{dto.HospitalId}' and blood group '{dto.BloodGroup}' already exists.");
            }

            var now = DateTime.UtcNow;
            var inventory = new BloodInventory
            {
                InventoryId = Guid.NewGuid(),
                HospitalId = dto.HospitalId,
                BloodGroup = dto.BloodGroup,
                UnitsAvailable = 0, // stock arrives only as packets from donations or transfers
                MinimumThreshold = dto.MinimumThreshold,
                MaximumCapacity = dto.MaximumCapacity,
                LastUpdated = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.BloodInventories.Add(inventory);
            await _context.SaveChangesAsync();

            return await MapToResponseDtoAsync(inventory.InventoryId);
        }

        /// <summary>
        /// Updates thresholds and capacity. A lower UnitsAvailable issues that many packets (earliest expiry first)
        /// with the audit note as the reason. Raising stock is refused: packets only come from donations or transfers.
        /// </summary>
        public async Task<InventoryResponseDto> UpdateInventoryAsync(Guid id, UpdateInventoryDto dto, Guid? performedByUserId = null)
        {
            var inventory = await _context.BloodInventories.FindAsync(id);
            if (inventory == null)
            {
                throw new KeyNotFoundException($"Blood inventory record with ID '{id}' was not found.");
            }

            if (dto.MinimumThreshold < 0)
            {
                throw new InvalidOperationException("MinimumThreshold cannot be negative.");
            }

            if (dto.MaximumCapacity < dto.MinimumThreshold)
            {
                throw new InvalidOperationException("MaximumCapacity cannot be less than MinimumThreshold.");
            }

            var now = DateTime.UtcNow;
            inventory.MinimumThreshold = dto.MinimumThreshold;
            inventory.MaximumCapacity = dto.MaximumCapacity;
            inventory.UpdatedAt = now;

            if (dto.UnitsAvailable.HasValue && dto.UnitsAvailable.Value != inventory.UnitsAvailable)
            {
                if (dto.UnitsAvailable.Value < 0)
                {
                    throw new InvalidOperationException("UnitsAvailable cannot be negative.");
                }

                if (dto.UnitsAvailable.Value > inventory.UnitsAvailable)
                {
                    throw new InvalidOperationException("Stock can only be added through recorded donations or completed hospital transfers.");
                }

                var reason = dto.AuditNotes?.Trim();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new InvalidOperationException("A reason (audit note) is required when issuing blood from stock.");
                }

                var toIssue = inventory.UnitsAvailable - dto.UnitsAvailable.Value;
                await InventoryLedger.IssuePacketsAsync(_context, inventory.HospitalId, inventory.BloodGroup, toIssue, null, reason, performedByUserId);
            }

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(inventory.InventoryId);
        }

        public async Task<InventoryResponseDto?> GetInventoryByIdAsync(Guid id)
        {
            var inventory = await _context.BloodInventories
                .Include(i => i.Hospital)
                .FirstOrDefaultAsync(i => i.InventoryId == id);

            if (inventory == null) return null;
            return (await MapManyAsync(new[] { inventory })).First();
        }

        public async Task<IEnumerable<InventoryResponseDto>> GetHospitalInventoryAsync(Guid hospitalId)
        {
            var list = await _context.BloodInventories
                .Include(i => i.Hospital)
                .Where(i => i.HospitalId == hospitalId)
                .OrderBy(i => i.BloodGroup)
                .ToListAsync();

            return await MapManyAsync(list);
        }

        public async Task<IEnumerable<InventoryResponseDto>> GetAllInventoryAsync()
        {
            var list = await _context.BloodInventories
                .Include(i => i.Hospital)
                .OrderBy(i => i.HospitalId)
                .ThenBy(i => i.BloodGroup)
                .ToListAsync();

            return await MapManyAsync(list);
        }

        /// <summary>Only an unused category can be deleted; anything with packets or audit history is kept.</summary>
        public async Task<bool> DeleteInventoryAsync(Guid id)
        {
            var inventory = await _context.BloodInventories.FindAsync(id);
            if (inventory == null)
            {
                return false;
            }

            var hasHistory = await _context.InventoryTransactions.AnyAsync(t => t.InventoryId == id) ||
                             await _context.BloodPackets.AnyAsync(p => p.HospitalId == inventory.HospitalId && p.BloodGroup == inventory.BloodGroup);
            if (hasHistory)
            {
                throw new InvalidOperationException("This blood group has stock or audit history and cannot be deleted. Set its threshold to 0 instead.");
            }

            _context.BloodInventories.Remove(inventory);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<InventoryResponseDto>> GetLowStockInventoryAsync()
        {
            var list = await _context.BloodInventories
                .Include(i => i.Hospital)
                .Where(i => i.UnitsAvailable <= i.MinimumThreshold)
                .OrderBy(i => i.UnitsAvailable)
                .ToListAsync();

            return await MapManyAsync(list);
        }

        public async Task<IEnumerable<InventoryResponseDto>> GetSurplusInventoryAsync()
        {
            var list = await _context.BloodInventories
                .Include(i => i.Hospital)
                .Where(i => i.UnitsAvailable >= (i.MaximumCapacity * 0.8))
                .OrderByDescending(i => i.UnitsAvailable)
                .ToListAsync();

            return await MapManyAsync(list);
        }

        public async Task<IEnumerable<InventoryTransactionResponseDto>> GetInventoryTransactionsAsync(Guid inventoryId)
        {
            var list = await _context.InventoryTransactions
                .Where(t => t.InventoryId == inventoryId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return list.Select(MapTransaction);
        }

        /// <summary>
        /// Packets owned by a hospital (optionally one group or status). With packetId, returns that single packet
        /// with its full audit history, including moves between hospitals.
        /// </summary>
        public async Task<IEnumerable<BloodPacketResponseDto>> GetPacketsAsync(Guid? hospitalId, string? bloodGroup, string? status, Guid? packetId)
        {
            var query = _context.BloodPackets.Include(p => p.Hospital).AsQueryable();
            if (packetId.HasValue) query = query.Where(p => p.PacketId == packetId.Value);
            if (hospitalId.HasValue) query = query.Where(p => p.HospitalId == hospitalId.Value);
            if (!string.IsNullOrWhiteSpace(bloodGroup)) query = query.Where(p => p.BloodGroup == bloodGroup);
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);

            var packets = await query
                .OrderBy(p => p.Status == BloodPacketStatus.Available ? 0 : 1)
                .ThenBy(p => p.ExpiryDate)
                .Take(500)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var result = packets.Select(p => new BloodPacketResponseDto
            {
                PacketId = p.PacketId,
                HospitalId = p.HospitalId,
                HospitalName = p.Hospital?.Name ?? string.Empty,
                BloodGroup = p.BloodGroup,
                VolumeMl = p.VolumeMl,
                CollectionDate = p.CollectionDate,
                ExpiryDate = p.ExpiryDate,
                Status = p.Status,
                Source = p.Source,
                SourceReferenceId = p.SourceReferenceId,
                IsExpiringSoon = p.Status == BloodPacketStatus.Available && p.ExpiryDate <= now.AddDays(p.Hospital?.ExpiryAlertDays ?? 5)
            }).ToList();

            if (packetId.HasValue && result.Count == 1)
            {
                var history = await _context.InventoryTransactions
                    .Where(t => t.PacketId == packetId.Value)
                    .OrderBy(t => t.CreatedAt)
                    .ToListAsync();
                result[0].History = history.Select(MapTransaction).ToList();
            }

            return result;
        }

        /// <summary>Background sweep: available packets past their expiry date become Expired.</summary>
        public async Task<int> ProcessExpiredPacketsAsync()
        {
            var count = await InventoryLedger.ExpirePacketsAsync(_context, DateTime.UtcNow);
            if (count > 0)
            {
                await _context.SaveChangesAsync();
            }
            return count;
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

        private async Task<InventoryResponseDto> MapToResponseDtoAsync(Guid inventoryId)
        {
            var inventory = await _context.BloodInventories
                .Include(i => i.Hospital)
                .FirstAsync(i => i.InventoryId == inventoryId);

            return (await MapManyAsync(new[] { inventory })).First();
        }

        /// <summary>Adds expiring-soon counts from each hospital's own alert window (one packet query).</summary>
        private async Task<List<InventoryResponseDto>> MapManyAsync(IEnumerable<BloodInventory> inventories)
        {
            var list = inventories.ToList();
            if (list.Count == 0) return new List<InventoryResponseDto>();

            var now = DateTime.UtcNow;
            var hospitalIds = list.Select(i => i.HospitalId).Distinct().ToList();
            var packets = await _context.BloodPackets
                .Where(p => hospitalIds.Contains(p.HospitalId) && p.Status == BloodPacketStatus.Available)
                .Select(p => new { p.HospitalId, p.BloodGroup, p.ExpiryDate })
                .ToListAsync();

            return list.Select(i =>
            {
                var alertDays = i.Hospital?.ExpiryAlertDays ?? 5;
                var groupPackets = packets.Where(p => p.HospitalId == i.HospitalId && p.BloodGroup == i.BloodGroup).ToList();
                var dto = MapToResponseDto(i);
                dto.ExpiryAlertDays = alertDays;
                dto.ExpiringSoonUnits = groupPackets.Count(p => p.ExpiryDate <= now.AddDays(alertDays));
                dto.NextExpiryDate = groupPackets.Count > 0 ? groupPackets.Min(p => p.ExpiryDate) : null;
                return dto;
            }).ToList();
        }

        private static InventoryResponseDto MapToResponseDto(BloodInventory i)
        {
            return new InventoryResponseDto
            {
                InventoryId = i.InventoryId,
                HospitalId = i.HospitalId,
                HospitalName = i.Hospital != null ? i.Hospital.Name : string.Empty,
                BloodGroup = i.BloodGroup,
                UnitsAvailable = i.UnitsAvailable,
                MinimumThreshold = i.MinimumThreshold,
                MaximumCapacity = i.MaximumCapacity,
                LastUpdated = i.LastUpdated,
                CreatedAt = i.CreatedAt,
                UpdatedAt = i.UpdatedAt
            };
        }

        private static InventoryTransactionResponseDto MapTransaction(InventoryTransaction t) => new()
        {
            TransactionId = t.TransactionId,
            InventoryId = t.InventoryId,
            TransactionType = t.TransactionType,
            Units = t.Units,
            Notes = t.Notes,
            PacketId = t.PacketId,
            ReferenceId = t.ReferenceId,
            PerformedByUserId = t.PerformedByUserId,
            CreatedAt = t.CreatedAt
        };
    }
}
