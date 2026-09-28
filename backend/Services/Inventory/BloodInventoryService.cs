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
    /// Available packets and changes only through InventoryLedger: packets entered by staff, recorded donations,
    /// transfers, issuing, donations to requests and expiry. It is never typed in as a number.
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
                UnitsAvailable = 0, // stock arrives only as packets
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
        /// Updates thresholds and capacity, and issues the packets staff selected (IssuePacketIds) with the audit note
        /// as the reason. The unit count itself cannot be changed: it is the number of Available packets.
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

            if (dto.UnitsAvailable.HasValue && dto.UnitsAvailable.Value != inventory.UnitsAvailable)
            {
                throw new InvalidOperationException(dto.UnitsAvailable.Value > inventory.UnitsAvailable
                    ? "Stock cannot be typed in. Add blood packets instead."
                    : "Select the packets to issue.");
            }

            var now = DateTime.UtcNow;
            inventory.MinimumThreshold = dto.MinimumThreshold;
            inventory.MaximumCapacity = dto.MaximumCapacity;
            inventory.UpdatedAt = now;

            if (dto.IssuePacketIds is { Count: > 0 })
            {
                var reason = dto.AuditNotes?.Trim();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new InvalidOperationException("A reason (audit note) is required when issuing blood from stock.");
                }

                var packets = await InventoryLedger.RequireSelectablePacketsAsync(_context, inventory.HospitalId, dto.IssuePacketIds, inventory.BloodGroup);
                await InventoryLedger.IssuePacketsAsync(_context, packets, null, reason, performedByUserId);
            }

            await InventoryLedger.SavePacketChangesAsync(_context);
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
        /// Staff enter collected blood: one packet per unit, each with its own tracking number, owned and created by
        /// the signed-in hospital. The collected date is mandatory and cannot be in the future.
        /// </summary>
        public async Task<List<BloodPacketResponseDto>> CreatePacketsAsync(Guid hospitalId, CreateBloodPacketsDto dto, Guid? performedByUserId)
        {
            if (!BloodGroup.IsValid(dto.BloodGroup))
            {
                throw new InvalidOperationException("Select a valid blood group (A+, A-, B+, B-, AB+, AB-, O+, O-).");
            }

            if (dto.Quantity < 1 || dto.Quantity > CreateBloodPacketsDto.MaxQuantity)
            {
                throw new InvalidOperationException($"Quantity must be between 1 and {CreateBloodPacketsDto.MaxQuantity}.");
            }

            var hospital = await _context.Hospitals.FindAsync(hospitalId)
                           ?? throw new InvalidOperationException("Your hospital was not found.");
            var collected = PacketDateRules.Validate(dto.CollectionDate, hospital.PacketShelfLifeDays, DateTime.UtcNow);
            var bloodGroup = NormalizeGroup(dto.BloodGroup);

            var packets = await InventoryLedger.AddCollectedPacketsAsync(_context, hospitalId, bloodGroup, dto.Quantity, collected,
                BloodPacketSource.Manual, null, TransactionType.StockAddition, "Collected blood entered by hospital staff", performedByUserId);
            await InventoryLedger.SavePacketChangesAsync(_context);

            var ids = packets.Select(p => p.PacketId).ToList();
            return (await QueryPacketsAsync(_context.BloodPackets.Where(p => ids.Contains(p.PacketId)), hospitalId))
                .OrderBy(p => p.TrackingNumber).ToList();
        }

        /// <summary>
        /// Edits a packet's blood group and collected date. Only the hospital that created it may edit it, and only
        /// while it still owns the packet and the packet is Available (so a transferred packet is read-only for everyone).
        /// </summary>
        public async Task<BloodPacketResponseDto> UpdatePacketAsync(Guid packetId, Guid hospitalId, UpdateBloodPacketDto dto, Guid? performedByUserId)
        {
            var packet = await _context.BloodPackets.FindAsync(packetId)
                         ?? throw new KeyNotFoundException("Blood packet not found.");

            if (packet.CreatedByHospitalId != hospitalId)
            {
                throw new UnauthorizedAccessException("Only the hospital that created this packet can edit it.");
            }

            if (packet.HospitalId != hospitalId)
            {
                throw new InvalidOperationException("This packet is no longer in your inventory and cannot be edited.");
            }

            if (packet.Status != BloodPacketStatus.Available)
            {
                throw new InvalidOperationException($"Only available packets can be edited. This packet is {packet.Status}.");
            }

            if (!BloodGroup.IsValid(dto.BloodGroup))
            {
                throw new InvalidOperationException("Select a valid blood group (A+, A-, B+, B-, AB+, AB-, O+, O-).");
            }

            var shelfLifeDays = await _context.Hospitals.Where(h => h.HospitalId == hospitalId).Select(h => h.PacketShelfLifeDays).FirstAsync();
            var collected = PacketDateRules.Validate(dto.CollectionDate, shelfLifeDays, DateTime.UtcNow);

            await InventoryLedger.ChangePacketDetailsAsync(_context, packet, NormalizeGroup(dto.BloodGroup), collected, shelfLifeDays, performedByUserId);
            await InventoryLedger.SavePacketChangesAsync(_context);

            return (await QueryPacketsAsync(_context.BloodPackets.Where(p => p.PacketId == packetId), hospitalId)).Single();
        }

        private static string NormalizeGroup(string bloodGroup) =>
            BloodGroup.All.First(g => string.Equals(g, bloodGroup.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Packets owned by a hospital (optionally one group or status). With packetId, returns that single packet
        /// with its full audit history, including moves between hospitals. viewerHospitalId decides CanEdit.
        /// </summary>
        public async Task<IEnumerable<BloodPacketResponseDto>> GetPacketsAsync(Guid? hospitalId, string? bloodGroup, string? status, Guid? packetId, Guid? viewerHospitalId = null)
        {
            var query = _context.BloodPackets.AsQueryable();
            if (packetId.HasValue) query = query.Where(p => p.PacketId == packetId.Value);
            if (hospitalId.HasValue) query = query.Where(p => p.HospitalId == hospitalId.Value);
            if (!string.IsNullOrWhiteSpace(bloodGroup)) query = query.Where(p => p.BloodGroup == bloodGroup);
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);

            var result = await QueryPacketsAsync(query, viewerHospitalId);

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

        private async Task<List<BloodPacketResponseDto>> QueryPacketsAsync(IQueryable<BloodPacket> query, Guid? viewerHospitalId)
        {
            var packets = await query
                .Include(p => p.Hospital)
                .Include(p => p.CreatedByHospital)
                .OrderBy(p => p.Status == BloodPacketStatus.Available ? 0 : 1)
                .ThenBy(p => p.ExpiryDate)
                .Take(500)
                .ToListAsync();

            var now = DateTime.UtcNow;
            return packets.Select(p => new BloodPacketResponseDto
            {
                PacketId = p.PacketId,
                TrackingNumber = p.TrackingNumber,
                CreatedByHospitalId = p.CreatedByHospitalId,
                CreatedByHospitalName = p.CreatedByHospital?.Name ?? string.Empty,
                HospitalId = p.HospitalId,
                HospitalName = p.Hospital?.Name ?? string.Empty,
                BloodGroup = p.BloodGroup,
                VolumeMl = p.VolumeMl,
                CollectionDate = p.CollectionDate,
                ExpiryDate = p.ExpiryDate,
                Status = p.Status,
                Source = p.Source,
                SourceReferenceId = p.SourceReferenceId,
                IsExpiringSoon = p.Status == BloodPacketStatus.Available && p.ExpiryDate <= now.AddDays(p.Hospital?.ExpiryAlertDays ?? 5),
                CreatedAt = p.CreatedAt,
                CanEdit = viewerHospitalId.HasValue && p.CreatedByHospitalId == viewerHospitalId && p.HospitalId == viewerHospitalId &&
                          p.Status == BloodPacketStatus.Available
            }).ToList();
        }

        /// <summary>
        /// Background sweep: available packets past their expiry date become Expired. Each hospital + blood group is saved
        /// on its own, so a packet used at the same moment (409) only postpones that group to the next sweep.
        /// </summary>
        public async Task<int> ProcessExpiredPacketsAsync()
        {
            var now = DateTime.UtcNow;
            var groups = await _context.BloodPackets
                .Where(p => p.Status == BloodPacketStatus.Available && p.ExpiryDate <= now)
                .Select(p => new { p.HospitalId, p.BloodGroup })
                .Distinct()
                .ToListAsync();

            var total = 0;
            foreach (var group in groups)
            {
                try
                {
                    var count = await InventoryLedger.ExpirePacketsAsync(_context, now, group.HospitalId, group.BloodGroup);
                    if (count > 0)
                    {
                        await _context.SaveChangesAsync();
                    }
                    total += count;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Someone used a packet of this group at the same moment; the next sweep tries again
                    _context.ChangeTracker.Clear();
                }
            }
            return total;
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
