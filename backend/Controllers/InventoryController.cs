using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Inventory;
using LifeLink.Services.Common;
using LifeLink.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "HospitalStaff,Admin,InternalAgent")]
    public class InventoryController : ControllerBase
    {
        private readonly IBloodInventoryService _inventoryService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AppDbContext _context;

        public InventoryController(IBloodInventoryService inventoryService, ICurrentUserService currentUserService, AppDbContext context)
        {
            _inventoryService = inventoryService;
            _currentUserService = currentUserService;
            _context = context;
        }

        private Task<Guid?> CallerHospitalIdAsync() => CallerHospitalResolver.ResolveAsync(_context, _currentUserService);

        private async Task<bool> OwnsInventoryAsync(Guid inventoryId)
        {
            var hospitalId = await CallerHospitalIdAsync();
            return hospitalId.HasValue && await _context.BloodInventories.AnyAsync(i => i.InventoryId == inventoryId && i.HospitalId == hospitalId);
        }

        /// <summary>
        /// Creates a blood group category (thresholds only) for the signed-in hospital. Stock arrives as packets.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<InventoryResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CreateInventory([FromBody] CreateInventoryDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var hospitalId = await CallerHospitalIdAsync();
            if (hospitalId == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Your account is not linked to a hospital."));
            }
            request.HospitalId = hospitalId.Value; // a hospital manages only its own stock

            try
            {
                var result = await _inventoryService.CreateInventoryAsync(request);
                return CreatedAtAction(nameof(GetInventoryById), new { id = result.InventoryId }, ApiResponse<InventoryResponseDto>.Ok(result, "Inventory created successfully."));
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.Contains("already exists"))
                {
                    return StatusCode(StatusCodes.Status409Conflict, ApiResponse<object>.Fail(ex.Message));
                }
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Retrieves all blood inventory records across hospitals.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllInventory()
        {
            var result = await _inventoryService.GetAllInventoryAsync();
            return Ok(ApiResponse<IEnumerable<InventoryResponseDto>>.Ok(result, "All inventories retrieved successfully."));
        }

        /// <summary>
        /// Retrieves low-stock blood inventory records (units available <= minimum threshold).
        /// </summary>
        [HttpGet("low-stock")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLowStockInventory()
        {
            var result = await _inventoryService.GetLowStockInventoryAsync();
            return Ok(ApiResponse<IEnumerable<InventoryResponseDto>>.Ok(result, "Low stock inventories retrieved successfully."));
        }

        /// <summary>
        /// Retrieves surplus blood inventory records (units available >= 80% maximum capacity).
        /// </summary>
        [HttpGet("surplus")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSurplusInventory()
        {
            var result = await _inventoryService.GetSurplusInventoryAsync();
            return Ok(ApiResponse<IEnumerable<InventoryResponseDto>>.Ok(result, "Surplus inventories retrieved successfully."));
        }

        /// <summary>
        /// Retrieves a specific blood inventory record by ID.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<InventoryResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetInventoryById(Guid id)
        {
            var result = await _inventoryService.GetInventoryByIdAsync(id);
            if (result == null)
            {
                return NotFound(ApiResponse<object>.Fail($"Inventory record with ID '{id}' was not found."));
            }

            return Ok(ApiResponse<InventoryResponseDto>.Ok(result, "Inventory retrieved successfully."));
        }

        /// <summary>
        /// Retrieves transaction audit history for a specific blood inventory record.
        /// </summary>
        [HttpGet("{id:guid}/transactions")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryTransactionResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetInventoryTransactions(Guid id)
        {
            var result = await _inventoryService.GetInventoryTransactionsAsync(id);
            return Ok(ApiResponse<IEnumerable<InventoryTransactionResponseDto>>.Ok(result, "Inventory transactions retrieved successfully."));
        }

        /// <summary>
        /// Updates thresholds of the hospital's own category. A lower unit count issues packets (earliest expiry first).
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<InventoryResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateInventory(Guid id, [FromBody] UpdateInventoryDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!await OwnsInventoryAsync(id))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("You can only manage your own hospital's inventory."));
            }

            try
            {
                var result = await _inventoryService.UpdateInventoryAsync(id, request, _currentUserService.UserId);
                return Ok(ApiResponse<InventoryResponseDto>.Ok(result, "Inventory updated successfully."));
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
        /// Deletes a blood inventory record.
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteInventory(Guid id)
        {
            if (!await OwnsInventoryAsync(id))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("You can only manage your own hospital's inventory."));
            }

            bool success;
            try
            {
                success = await _inventoryService.DeleteInventoryAsync(id);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
            if (!success)
            {
                return NotFound(ApiResponse<object>.Fail($"Inventory record with ID '{id}' was not found."));
            }

            return Ok(ApiResponse<string>.Ok("Inventory deleted successfully.", "Inventory record removed."));
        }

        /// <summary>
        /// Blood packets with expiry and source. Hospital staff see their own hospital's packets; with packetId a
        /// single packet is returned with its full audit history (collection, transfers, issue, expiry).
        /// </summary>
        [HttpGet("packets")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<BloodPacketResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPackets([FromQuery] Guid? hospitalId, [FromQuery] string? bloodGroup, [FromQuery] string? status, [FromQuery] Guid? packetId)
        {
            if (_currentUserService.Roles.Contains("HospitalStaff"))
            {
                hospitalId = await CallerHospitalIdAsync();
                if (hospitalId == null)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Your account is not linked to a hospital."));
                }
            }

            var result = await _inventoryService.GetPacketsAsync(hospitalId, bloodGroup, status, packetId);
            return Ok(ApiResponse<IEnumerable<BloodPacketResponseDto>>.Ok(result, "Blood packets retrieved successfully."));
        }

        /// <summary>
        /// Retrieves all blood inventory records for a specific hospital.
        /// </summary>
        [HttpGet("hospital/{hospitalId:guid}")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetHospitalInventory(Guid hospitalId)
        {
            var result = await _inventoryService.GetHospitalInventoryAsync(hospitalId);
            return Ok(ApiResponse<IEnumerable<InventoryResponseDto>>.Ok(result, "Hospital inventory retrieved successfully."));
        }
    }
}
