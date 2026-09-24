using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Transfer;
using LifeLink.Services.Common;
using LifeLink.Services.Transfer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    /// <summary>
    /// Inter-Hospital Blood Transfer: a hospital requests blood from, or offers blood to, another approved hospital.
    /// The acting hospital always comes from the signed-in account.
    /// </summary>
    [ApiController]
    [Route("api/transfers")]
    [Authorize(Roles = "HospitalStaff,Admin")]
    public class TransferRequestsController : ControllerBase
    {
        private readonly ITransferRequestService _transferRequestService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AppDbContext _context;

        public TransferRequestsController(ITransferRequestService transferRequestService, ICurrentUserService currentUserService, AppDbContext context)
        {
            _transferRequestService = transferRequestService;
            _currentUserService = currentUserService;
            _context = context;
        }

        /// <summary>Creates a transfer "Request" (ask for blood) or "Offer" (send blood) to another hospital.</summary>
        [HttpPost]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<TransferRequestResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateTransferRequest([FromBody] TransferRequestCreateDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            return await Execute(async hospitalId =>
            {
                var result = await _transferRequestService.CreateTransferRequestAsync(request, hospitalId);
                return CreatedAtAction(nameof(GetTransferRequestById), new { id = result.TransferRequestId },
                    ApiResponse<TransferRequestResponseDto>.Ok(result, "Transfer request created successfully."));
            });
        }

        /// <summary>The signed-in hospital's incoming and outgoing transfers (all transfers for the Admin).</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<TransferRequestResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllTransferRequests()
        {
            var hospitalId = await ScopeAsync();
            if (hospitalId == Guid.Empty) return Forbidden("Your account is not linked to a hospital.");
            var result = await _transferRequestService.GetAllTransferRequestsAsync(hospitalId);
            return Ok(ApiResponse<IEnumerable<TransferRequestResponseDto>>.Ok(result, "Transfer requests retrieved successfully."));
        }

        [HttpGet("pending")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<TransferRequestResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPendingTransferRequests()
        {
            var hospitalId = await ScopeAsync();
            if (hospitalId == Guid.Empty) return Forbidden("Your account is not linked to a hospital.");
            var result = await _transferRequestService.GetPendingTransferRequestsAsync(hospitalId);
            return Ok(ApiResponse<IEnumerable<TransferRequestResponseDto>>.Ok(result, "Pending transfer requests retrieved successfully."));
        }

        /// <summary>A transfer the signed-in hospital takes part in, with the IDs of the packets it moved.</summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<TransferRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTransferRequestById(Guid id)
        {
            var result = await _transferRequestService.GetTransferRequestAsync(id);
            var hospitalId = await ScopeAsync();
            if (result == null || (hospitalId.HasValue && result.SenderHospitalId != hospitalId && result.ReceiverHospitalId != hospitalId))
            {
                return NotFound(ApiResponse<object>.Fail($"Transfer request with ID '{id}' was not found."));
            }

            return Ok(ApiResponse<TransferRequestResponseDto>.Ok(result, "Transfer request retrieved successfully."));
        }

        /// <summary>The counterpart hospital accepts: packets move immediately and the transfer completes.</summary>
        [HttpPut("{id:guid}/approve")]
        [Authorize(Roles = "HospitalStaff")]
        public Task<IActionResult> ApproveTransferRequest(Guid id) =>
            Execute(async hospitalId => Ok(ApiResponse<TransferRequestResponseDto>.Ok(
                await _transferRequestService.ApproveTransferRequestAsync(id, hospitalId, _currentUserService.UserId),
                "Transfer accepted and inventory updated.")));

        /// <summary>The counterpart hospital rejects with a reason; the rejection stays in history.</summary>
        [HttpPut("{id:guid}/reject")]
        [Authorize(Roles = "HospitalStaff")]
        public Task<IActionResult> RejectTransferRequest(Guid id, [FromBody] RejectTransferDto? dto) =>
            Execute(async hospitalId => Ok(ApiResponse<TransferRequestResponseDto>.Ok(
                await _transferRequestService.RejectTransferRequestAsync(id, hospitalId, dto?.Reason),
                "Transfer request rejected.")));

        /// <summary>The creator deletes a pending transfer; it stays in history as Cancelled.</summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "HospitalStaff")]
        public Task<IActionResult> DeleteTransferRequest(Guid id) =>
            Execute(async hospitalId => Ok(ApiResponse<TransferRequestResponseDto>.Ok(
                await _transferRequestService.DeleteTransferRequestAsync(id, hospitalId),
                "Transfer request deleted.")));

        // Admin: no scope (null). Hospital staff: own hospital, or Guid.Empty when the account has no hospital.
        private async Task<Guid?> ScopeAsync()
        {
            if (_currentUserService.Roles.Contains("Admin")) return null;
            return await CallerHospitalResolver.ResolveAsync(_context, _currentUserService) ?? Guid.Empty;
        }

        private async Task<IActionResult> Execute(Func<Guid, Task<IActionResult>> action)
        {
            var hospitalId = await CallerHospitalResolver.ResolveAsync(_context, _currentUserService);
            if (hospitalId == null)
            {
                return Forbidden("Your account is not linked to a hospital.");
            }

            try
            {
                return await action(hospitalId.Value);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbidden(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        private IActionResult Forbidden(string message) =>
            StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(message));
    }
}
