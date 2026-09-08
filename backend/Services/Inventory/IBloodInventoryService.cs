using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Inventory;

namespace LifeLink.Services.Inventory
{
    public interface IBloodInventoryService
    {
        Task<InventoryResponseDto> CreateInventoryAsync(CreateInventoryDto dto);
        Task<InventoryResponseDto> UpdateInventoryAsync(Guid id, UpdateInventoryDto dto);
        Task<InventoryResponseDto?> GetInventoryByIdAsync(Guid id);
        Task<IEnumerable<InventoryResponseDto>> GetHospitalInventoryAsync(Guid hospitalId);
        Task<IEnumerable<InventoryResponseDto>> GetAllInventoryAsync();
        Task<bool> DeleteInventoryAsync(Guid id);
        Task<IEnumerable<InventoryResponseDto>> GetLowStockInventoryAsync();
        Task<IEnumerable<InventoryResponseDto>> GetSurplusInventoryAsync();
        Task<IEnumerable<InventoryTransactionResponseDto>> GetInventoryTransactionsAsync(Guid inventoryId);
    }
}
