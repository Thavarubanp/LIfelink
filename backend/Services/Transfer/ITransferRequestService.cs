using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Transfer;

namespace LifeLink.Services.Transfer
{
    public interface ITransferRequestService
    {
        Task<TransferRequestResponseDto> CreateTransferRequestAsync(TransferRequestCreateDto dto, Guid actingHospitalId);
        Task<TransferRequestResponseDto?> GetTransferRequestAsync(Guid id);
        Task<IEnumerable<TransferRequestResponseDto>> GetAllTransferRequestsAsync(Guid? hospitalId = null);
        Task<IEnumerable<TransferRequestResponseDto>> GetPendingTransferRequestsAsync(Guid? hospitalId = null);
        Task<TransferRequestResponseDto> ApproveTransferRequestAsync(Guid id, Guid actingHospitalId, Guid? performedByUserId = null);
        Task<TransferRequestResponseDto> RejectTransferRequestAsync(Guid id, Guid actingHospitalId, string? reason);
        Task<TransferRequestResponseDto> DeleteTransferRequestAsync(Guid id, Guid actingHospitalId);
    }
}
