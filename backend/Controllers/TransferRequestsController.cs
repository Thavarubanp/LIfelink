using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Transfer;
using LifeLink.Services.Transfer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/transfers")]
    [Authorize(Roles = "HospitalStaff,Admin")]
    public class TransferRequestsController : ControllerBase
    {
        private readonly ITransferRequestService _transferRequestService;

        public TransferRequestsController(ITransferRequestService transferRequestService)
        {
            _transferRequestService = transferRequestService;
        }

        /// <summary>
        /// Creates a new hospital-to-hospital transfer request.
        /// </summary>
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

            try
            {
                var result = await _transferRequestService.CreateTransferRequestAsync(request);
                return CreatedAtAction(nameof(GetTransferRequestById), new { id = result.TransferRequestId }, ApiResponse<TransferRequestResponseDto>.Ok(result, "Transfer request created successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Retrieves all hospital transfer requests.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<TransferRequestResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllTransferRequests()
        {
            var result = await _transferRequestService.GetAllTransferRequestsAsync();
            return Ok(ApiResponse<IEnumerable<TransferRequestResponseDto>>.Ok(result, "Transfer requests retrieved successfully."));
        }

        /// <summary>
        /// Retrieves pending hospital transfer requests.
        /// </summary>
        [HttpGet("pending")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<TransferRequestResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPendingTransferRequests()
        {
            var result = await _transferRequestService.GetPendingTransferRequestsAsync();
            return Ok(ApiResponse<IEnumerable<TransferRequestResponseDto>>.Ok(result, "Pending transfer requests retrieved successfully."));
        }

        /// <summary>
        /// Retrieves a specific hospital transfer request by ID.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<TransferRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTransferRequestById(Guid id)
        {
            var result = await _transferRequestService.GetTransferRequestAsync(id);
            if (result == null)
            {
                return NotFound(ApiResponse<object>.Fail($"Transfer request with ID '{id}' was not found."));
            }

            return Ok(ApiResponse<TransferRequestResponseDto>.Ok(result, "Transfer request retrieved successfully."));
        }

        /// <summary>
        /// Approves a hospital transfer request.
        /// </summary>
        [HttpPut("{id:guid}/approve")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<TransferRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ApproveTransferRequest(Guid id)
        {
            try
            {
                var result = await _transferRequestService.ApproveTransferRequestAsync(id);
                return Ok(ApiResponse<TransferRequestResponseDto>.Ok(result, "Transfer request approved."));
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

        /// <summary>
        /// Rejects a hospital transfer request.
        /// </summary>
        [HttpPut("{id:guid}/reject")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<TransferRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RejectTransferRequest(Guid id)
        {
            try
            {
                var result = await _transferRequestService.RejectTransferRequestAsync(id);
                return Ok(ApiResponse<TransferRequestResponseDto>.Ok(result, "Transfer request rejected."));
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

        /// <summary>
        /// Completes a hospital transfer request and adjusts inventory stock.
        /// </summary>
        [HttpPut("{id:guid}/complete")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<TransferRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CompleteTransferRequest(Guid id)
        {
            try
            {
                var result = await _transferRequestService.CompleteTransferRequestAsync(id);
                return Ok(ApiResponse<TransferRequestResponseDto>.Ok(result, "Transfer request completed and inventory updated."));
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
    }
}
