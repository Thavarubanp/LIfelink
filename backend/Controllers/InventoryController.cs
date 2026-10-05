using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using LifeLink.Common;
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

        private bool IsHospitalStaffOnly => _currentUserService.Roles.Contains("HospitalStaff")
            && !_currentUserService.Roles.Contains("Admin")
            && !_currentUserService.Roles.Contains("InternalAgent");

        private IActionResult ForbiddenInventory() =>
            StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("You can only view your own hospital's inventory."));

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
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                if (ex.Message.Contains("already exists"))
                {
                    return StatusCode(StatusCodes.Status409Conflict, ApiResponse<object>.Fail(ex.Message));
                }
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// HospitalStaff receive only their authenticated hospital's inventory; Admin/InternalAgent receive all hospitals.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllInventory()
        {
            IEnumerable<InventoryResponseDto> result;
            if (IsHospitalStaffOnly)
            {
                var hospitalId = await CallerHospitalIdAsync();
                if (hospitalId == null) return ForbiddenInventory();
                result = await _inventoryService.GetHospitalInventoryAsync(hospitalId.Value);
            }
            else
            {
                result = await _inventoryService.GetAllInventoryAsync();
            }
            return Ok(ApiResponse<IEnumerable<InventoryResponseDto>>.Ok(result, "Inventories retrieved successfully."));
        }

        /// <summary>
        /// Starts the inventory analysis now (hospital staff only; there is no admin button). Same code and lock as the
        /// scheduled run: 409 "Analysis is already running" or "Analysis ran moments ago — try again in N s" (2-minute
        /// global cooldown). Returns the run: alerts sent per kind and repeated alerts skipped.
        /// </summary>
        [HttpPost("analysis/run")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<InventoryAnalysisRunDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RunAnalysis([FromServices] InventoryAnalysisService analysis)
        {
            var hospitalId = await CallerHospitalIdAsync();
            if (hospitalId == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Your account is not linked to a hospital."));
            }
            var run = await analysis.RunAsync(LifeLink.Entities.InventoryAnalysisTriggers.Manual, hospitalId, _currentUserService.UserId);
            if (run.Status == LifeLink.Entities.InventoryAnalysisStatuses.Failed)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse<object>.Fail("The analysis failed. Please try again later."));
            }
            return Ok(ApiResponse<InventoryAnalysisRunDto>.Ok(run, "Inventory analysis complete."));
        }

        /// <summary>
        /// Lock state, last run (with who started it) and the next scheduled run: the same answer for every hospital.
        /// Polled every 15 s in the background (does not extend the session).
        /// </summary>
        [HttpGet("analysis/status")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<InventoryAnalysisStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnalysisStatus([FromServices] InventoryAnalysisService analysis) =>
            Ok(ApiResponse<InventoryAnalysisStatusDto>.Ok(await analysis.GetStatusAsync(), "Inventory analysis status."));

        /// <summary>
        /// Retrieves low-stock records (the authenticated hospital only for HospitalStaff).
        /// </summary>
        [HttpGet("low-stock")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLowStockInventory()
        {
            IEnumerable<InventoryResponseDto> result;
            if (IsHospitalStaffOnly)
            {
                var hospitalId = await CallerHospitalIdAsync();
                if (hospitalId == null) return ForbiddenInventory();
                result = (await _inventoryService.GetHospitalInventoryAsync(hospitalId.Value)).Where(i => i.IsLowStock);
            }
            else
            {
                result = await _inventoryService.GetLowStockInventoryAsync();
            }
            return Ok(ApiResponse<IEnumerable<InventoryResponseDto>>.Ok(result, "Low stock inventories retrieved successfully."));
        }

        /// <summary>
        /// Retrieves surplus records (the authenticated hospital only for HospitalStaff).
        /// </summary>
        [HttpGet("surplus")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSurplusInventory()
        {
            IEnumerable<InventoryResponseDto> result;
            if (IsHospitalStaffOnly)
            {
                var hospitalId = await CallerHospitalIdAsync();
                if (hospitalId == null) return ForbiddenInventory();
                result = (await _inventoryService.GetHospitalInventoryAsync(hospitalId.Value)).Where(i => i.IsSurplus);
            }
            else
            {
                result = await _inventoryService.GetSurplusInventoryAsync();
            }
            return Ok(ApiResponse<IEnumerable<InventoryResponseDto>>.Ok(result, "Surplus inventories retrieved successfully."));
        }

        /// <summary>
        /// Retrieves a specific record; HospitalStaff may retrieve only their own hospital's record.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<InventoryResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetInventoryById(Guid id)
        {
            if (IsHospitalStaffOnly && !await OwnsInventoryAsync(id)) return ForbiddenInventory();
            var result = await _inventoryService.GetInventoryByIdAsync(id);
            if (result == null)
            {
                return NotFound(ApiResponse<object>.Fail($"Inventory record with ID '{id}' was not found."));
            }

            return Ok(ApiResponse<InventoryResponseDto>.Ok(result, "Inventory retrieved successfully."));
        }

        /// <summary>
        /// Retrieves transaction history; HospitalStaff may retrieve only their own hospital's record.
        /// </summary>
        [HttpGet("{id:guid}/transactions")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryTransactionResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetInventoryTransactions(Guid id)
        {
            if (IsHospitalStaffOnly && !await OwnsInventoryAsync(id)) return ForbiddenInventory();
            var result = await _inventoryService.GetInventoryTransactionsAsync(id);
            return Ok(ApiResponse<IEnumerable<InventoryTransactionResponseDto>>.Ok(result, "Inventory transactions retrieved successfully."));
        }

        /// <summary>
        /// Updates thresholds of the hospital's own category and issues the packets listed in IssuePacketIds
        /// (with AuditNotes as the reason). The unit count cannot be typed in.
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
            catch (ConflictException ex)
            {
                return StatusCode(StatusCodes.Status409Conflict, ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
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
            catch (InvalidOperationException ex) when (ex is not ConflictException)
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
            Guid? viewerHospitalId = null;
            if (_currentUserService.Roles.Contains("HospitalStaff"))
            {
                hospitalId = await CallerHospitalIdAsync();
                if (hospitalId == null)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Your account is not linked to a hospital."));
                }
                viewerHospitalId = hospitalId;
            }

            var result = await _inventoryService.GetPacketsAsync(hospitalId, bloodGroup, status, packetId, viewerHospitalId);
            return Ok(ApiResponse<IEnumerable<BloodPacketResponseDto>>.Ok(result, "Blood packets retrieved successfully."));
        }

        /// <summary>
        /// Hospital staff enter collected blood as packets (blood group, mandatory collected date that is not in the
        /// future, quantity 1-20). Each packet gets a unique tracking number; the hospital comes from the account.
        /// </summary>
        [HttpPost("packets")]
        [LifeLink.Common.Idempotent] // a double submit with the same Idempotency-Key creates nothing twice
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<List<BloodPacketResponseDto>>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreatePackets([FromBody] CreateBloodPacketsDto request)
        {
            var hospitalId = await CallerHospitalIdAsync();
            if (hospitalId == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Your account is not linked to a hospital."));
            }

            try
            {
                var result = await _inventoryService.CreatePacketsAsync(hospitalId.Value, request, _currentUserService.UserId);
                return StatusCode(StatusCodes.Status201Created,
                    ApiResponse<List<BloodPacketResponseDto>>.Ok(result, $"{result.Count} blood packet(s) added."));
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Edits a packet's blood group and collected date. Only the hospital that created the packet may edit it
        /// (403 otherwise), and only while it owns the packet and the packet is Available.
        /// </summary>
        [HttpPut("packets/{packetId:guid}")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<BloodPacketResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UpdatePacket(Guid packetId, [FromBody] UpdateBloodPacketDto request)
        {
            var hospitalId = await CallerHospitalIdAsync();
            if (hospitalId == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Your account is not linked to a hospital."));
            }

            try
            {
                var result = await _inventoryService.UpdatePacketAsync(packetId, hospitalId.Value, request, _currentUserService.UserId);
                return Ok(ApiResponse<BloodPacketResponseDto>.Ok(result, "Blood packet updated."));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ex.Message));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (ConflictException ex)
            {
                return StatusCode(StatusCodes.Status409Conflict, ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Retrieves a hospital snapshot; HospitalStaff may request only their authenticated hospital.
        /// </summary>
        [HttpGet("hospital/{hospitalId:guid}")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<InventoryResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetHospitalInventory(Guid hospitalId)
        {
            if (IsHospitalStaffOnly)
            {
                var ownHospitalId = await CallerHospitalIdAsync();
                if (ownHospitalId == null || ownHospitalId.Value != hospitalId) return ForbiddenInventory();
                hospitalId = ownHospitalId.Value;
            }
            var result = await _inventoryService.GetHospitalInventoryAsync(hospitalId);
            return Ok(ApiResponse<IEnumerable<InventoryResponseDto>>.Ok(result, "Hospital inventory retrieved successfully."));
        }
    }
}
