using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Inventory;
using LifeLink.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "HospitalStaff,Admin,InternalAgent")]
    public class InventoryController : ControllerBase
    {
        private readonly IBloodInventoryService _inventoryService;

        public InventoryController(IBloodInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// Creates a new blood inventory record for a hospital.
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
        /// Updates a blood inventory record.
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

            try
            {
                var result = await _inventoryService.UpdateInventoryAsync(id, request);
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
            var success = await _inventoryService.DeleteInventoryAsync(id);
            if (!success)
            {
                return NotFound(ApiResponse<object>.Fail($"Inventory record with ID '{id}' was not found."));
            }

            return Ok(ApiResponse<string>.Ok("Inventory deleted successfully.", "Inventory record removed."));
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
