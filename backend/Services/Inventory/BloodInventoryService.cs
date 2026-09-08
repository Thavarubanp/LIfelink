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

            if (dto.UnitsAvailable < 0)
            {
                throw new InvalidOperationException("UnitsAvailable cannot be negative.");
            }

            if (dto.MinimumThreshold < 0)
            {
                throw new InvalidOperationException("MinimumThreshold cannot be negative.");
            }

            if (dto.MaximumCapacity < dto.MinimumThreshold)
            {
                throw new InvalidOperationException("MaximumCapacity cannot be less than MinimumThreshold.");
            }

            if (dto.UnitsAvailable > dto.MaximumCapacity)
            {
                throw new InvalidOperationException($"UnitsAvailable ({dto.UnitsAvailable}) cannot exceed MaximumCapacity ({dto.MaximumCapacity}).");
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
                UnitsAvailable = dto.UnitsAvailable,
                MinimumThreshold = dto.MinimumThreshold,
                MaximumCapacity = dto.MaximumCapacity,
                LastUpdated = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            var transaction = new InventoryTransaction
            {
                TransactionId = Guid.NewGuid(),
                InventoryId = inventory.InventoryId,
                TransactionType = TransactionType.InitialStock,
                Units = dto.UnitsAvailable,
                Notes = "Initial inventory creation",
                CreatedAt = now
            };

            _context.BloodInventories.Add(inventory);
            _context.InventoryTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            return await MapToResponseDtoAsync(inventory.InventoryId);
        }

        public async Task<InventoryResponseDto> UpdateInventoryAsync(Guid id, UpdateInventoryDto dto)
        {
            var inventory = await _context.BloodInventories.FindAsync(id);
            if (inventory == null)
            {
                throw new KeyNotFoundException($"Blood inventory record with ID '{id}' was not found.");
            }

            if (dto.UnitsAvailable < 0)
            {
                throw new InvalidOperationException("UnitsAvailable cannot be negative.");
            }

            if (dto.MinimumThreshold < 0)
            {
                throw new InvalidOperationException("MinimumThreshold cannot be negative.");
            }

            if (dto.MaximumCapacity < dto.MinimumThreshold)
            {
                throw new InvalidOperationException("MaximumCapacity cannot be less than MinimumThreshold.");
            }

            if (dto.UnitsAvailable > dto.MaximumCapacity)
            {
                throw new InvalidOperationException($"UnitsAvailable ({dto.UnitsAvailable}) cannot exceed MaximumCapacity ({dto.MaximumCapacity}).");
            }

            var unitDelta = dto.UnitsAvailable - inventory.UnitsAvailable;
            var now = DateTime.UtcNow;

            inventory.UnitsAvailable = dto.UnitsAvailable;
            inventory.MinimumThreshold = dto.MinimumThreshold;
            inventory.MaximumCapacity = dto.MaximumCapacity;
            inventory.LastUpdated = now;
            inventory.UpdatedAt = now;

            if (unitDelta != 0)
            {
                var transactionType = unitDelta > 0 ? TransactionType.StockAddition : TransactionType.StockDeduction;
                var transaction = new InventoryTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    InventoryId = inventory.InventoryId,
                    TransactionType = transactionType,
                    Units = Math.Abs(unitDelta),
                    Notes = dto.AuditNotes ?? $"Stock updated by {unitDelta} units.",
                    CreatedAt = now
                };
                _context.InventoryTransactions.Add(transaction);
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
            return MapToResponseDto(inventory);
        }

        public async Task<IEnumerable<InventoryResponseDto>> GetHospitalInventoryAsync(Guid hospitalId)
        {
            var list = await _context.BloodInventories
                .Include(i => i.Hospital)
                .Where(i => i.HospitalId == hospitalId)
                .OrderBy(i => i.BloodGroup)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        public async Task<IEnumerable<InventoryResponseDto>> GetAllInventoryAsync()
        {
            var list = await _context.BloodInventories
                .Include(i => i.Hospital)
                .OrderBy(i => i.HospitalId)
                .ThenBy(i => i.BloodGroup)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        public async Task<bool> DeleteInventoryAsync(Guid id)
        {
            var inventory = await _context.BloodInventories.FindAsync(id);
            if (inventory == null)
            {
                return false;
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

            return list.Select(MapToResponseDto);
        }

        public async Task<IEnumerable<InventoryResponseDto>> GetSurplusInventoryAsync()
        {
            var list = await _context.BloodInventories
                .Include(i => i.Hospital)
                .Where(i => i.UnitsAvailable >= (i.MaximumCapacity * 0.8))
                .OrderByDescending(i => i.UnitsAvailable)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        public async Task<IEnumerable<InventoryTransactionResponseDto>> GetInventoryTransactionsAsync(Guid inventoryId)
        {
            var list = await _context.InventoryTransactions
                .Where(t => t.InventoryId == inventoryId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return list.Select(t => new InventoryTransactionResponseDto
            {
                TransactionId = t.TransactionId,
                InventoryId = t.InventoryId,
                TransactionType = t.TransactionType,
                Units = t.Units,
                Notes = t.Notes,
                CreatedAt = t.CreatedAt
            });
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

        private async Task<InventoryResponseDto> MapToResponseDtoAsync(Guid inventoryId)
        {
            var inventory = await _context.BloodInventories
                .Include(i => i.Hospital)
                .FirstAsync(i => i.InventoryId == inventoryId);

            return MapToResponseDto(inventory);
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
    }
}
