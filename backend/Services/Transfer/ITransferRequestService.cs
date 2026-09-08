using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Transfer;

namespace LifeLink.Services.Transfer
{
    public interface ITransferRequestService
    {
        Task<TransferRequestResponseDto> CreateTransferRequestAsync(TransferRequestCreateDto dto);
        Task<TransferRequestResponseDto?> GetTransferRequestAsync(Guid id);
        Task<IEnumerable<TransferRequestResponseDto>> GetAllTransferRequestsAsync();
        Task<TransferRequestResponseDto> ApproveTransferRequestAsync(Guid id);
        Task<TransferRequestResponseDto> RejectTransferRequestAsync(Guid id);
        Task<TransferRequestResponseDto> CompleteTransferRequestAsync(Guid id);
        Task<IEnumerable<TransferRequestResponseDto>> GetPendingTransferRequestsAsync();
    }
}
