using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.DTOs.Hospitals;
using LifeLink.Services.Common;
using LifeLink.Services.Hospitals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    /// <summary>
    /// Hospital registration and directory. Only registration is open to signed-out visitors; the directory returns
    /// summary fields, and a hospital's full record (documents, registration conversation) is limited to admins and
    /// that hospital's own staff.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class HospitalsController : ControllerBase
    {
        private readonly IHospitalService _hospitalService;
        private readonly ICurrentUserService _currentUserService;

        public HospitalsController(IHospitalService hospitalService, ICurrentUserService currentUserService)
        {
            _hospitalService = hospitalService;
            _currentUserService = currentUserService;
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> CreateHospital([FromBody] CreateHospitalDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var result = await _hospitalService.CreateHospitalAsync(dto);
            return CreatedAtAction(nameof(GetHospitalById), new { id = result.HospitalId }, result);
        }

        /// <summary>Hospital directory for signed-in users: summary fields only.</summary>
        [HttpGet]
        public async Task<IActionResult> GetHospitals([FromQuery] bool? isVerified)
        {
            var list = await _hospitalService.GetHospitalsAsync(isVerified);
            return Ok(list);
        }

        /// <summary>The signed-in hospital staff member's own hospital, with its registration conversation.</summary>
        [HttpGet("me")]
        [Authorize(Roles = "HospitalStaff")]
        public async Task<IActionResult> GetMyHospital()
        {
            var hospitalId = await _hospitalService.GetHospitalIdByEmailAsync(_currentUserService.Email);
            var hospital = hospitalId.HasValue ? await _hospitalService.GetHospitalByIdAsync(hospitalId.Value) : null;
            if (hospital == null) return NotFound(new { message = "No hospital is registered for this account." });
            return Ok(hospital);
        }

        /// <summary>A hospital's full record: admins, or that hospital's own staff.</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetHospitalById(Guid id)
        {
            var isAdmin = _currentUserService.Roles.Contains("Admin");
            if (!isAdmin && !await IsOwnHospitalAsync(id))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only view your own hospital's registration." });
            }

            var hospital = await _hospitalService.GetHospitalByIdAsync(id, includeAdminIdentity: isAdmin);
            if (hospital == null) return NotFound(new { message = $"Hospital with ID {id} not found." });
            return Ok(hospital);
        }

        /// <summary>Legacy verification switch; admins only (the approval queue uses /api/Admin/hospitals).</summary>
        [HttpPut("{id:guid}/verify")]
        [HttpPut("{id:guid}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> VerifyHospital(Guid id, [FromBody] VerifyHospitalDto? dto)
        {
            var isVerified = dto?.IsVerified ?? true;
            var updated = await _hospitalService.VerifyHospitalAsync(id, isVerified);
            if (updated == null) return NotFound(new { message = $"Hospital with ID {id} not found." });
            return Ok(updated);
        }

        /// <summary>
        /// The hospital's reply in its rejected registration's conversation, optionally correcting details and
        /// documents. Only that hospital's staff, and only while the registration is Rejected.
        /// </summary>
        [HttpPost("{id:guid}/replies")]
        [Authorize(Roles = "HospitalStaff")]
        public async Task<IActionResult> ReplyToRegistration(Guid id, [FromBody] HospitalRegistrationReplyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!await IsOwnHospitalAsync(id))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only reply to your own hospital's registration." });
            }

            try
            {
                var result = await _hospitalService.ReplyToRegistrationAsync(id, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ConflictException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task<bool> IsOwnHospitalAsync(Guid hospitalId)
        {
            if (!_currentUserService.Roles.Contains("HospitalStaff")) return false;
            var ownHospitalId = await _hospitalService.GetHospitalIdByEmailAsync(_currentUserService.Email);
            return ownHospitalId == hospitalId;
        }
    }
}
