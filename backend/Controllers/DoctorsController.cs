using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Doctors;
using LifeLink.Services.Common;
using LifeLink.Services.Doctors;
using LifeLink.Services.Hospitals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DoctorsController : ControllerBase
    {
        private readonly IDoctorService _doctorService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AppDbContext _context;
        private readonly IHospitalService _hospitalService;

        public DoctorsController(
            IDoctorService doctorService,
            ICurrentUserService currentUserService,
            AppDbContext context,
            IHospitalService hospitalService)
        {
            _doctorService = doctorService;
            _currentUserService = currentUserService;
            _context = context;
            _hospitalService = hospitalService;
        }

        /// <summary>
        /// Creates a new Doctor account. Only Hospital Staff may call this endpoint.
        /// The HospitalId in the request body is verified against the authenticated hospital's record.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<DoctorResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> CreateDoctor([FromBody] CreateDoctorDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Resolve the authenticated hospital from the current user's email
            var userEmail = _currentUserService.Email;
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(ApiResponse<object>.Fail("Unable to resolve authenticated user's email."));

            var hospital = await _context.Hospitals
                .FirstOrDefaultAsync(h => h.Email != null && h.Email.ToLower() == userEmail.Trim().ToLower());

            if (hospital == null)
                return Forbid(); // Authenticated user is not linked to any hospital

            // Ensure the HospitalId in the request matches the authenticated hospital
            if (dto.HospitalId != hospital.HospitalId)
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<object>.Fail("You can only create doctors for your own hospital."));

            try
            {
                var result = await _doctorService.CreateDoctorAsync(dto);
                return StatusCode(StatusCodes.Status201Created,
                    ApiResponse<DoctorResponseDto>.Ok(result, "Doctor account created successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Returns doctors for the authenticated hospital (or all doctors for Admins).
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "HospitalStaff,Admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetDoctors()
        {
            var userRoles = _currentUserService.Roles.ToList();

            // Admin sees all doctors; Hospital staff see only their own
            if (userRoles.Contains("Admin"))
            {
                var all = await _doctorService.GetDoctorsAsync();
                return Ok(ApiResponse<object>.Ok(all, "Doctors retrieved successfully."));
            }

            // Resolve hospital from authenticated user's email
            var userEmail = _currentUserService.Email;
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(ApiResponse<object>.Fail("Unable to resolve authenticated user's email."));

            var hospital = await _context.Hospitals
                .FirstOrDefaultAsync(h => h.Email != null && h.Email.ToLower() == userEmail.Trim().ToLower());

            if (hospital == null)
                return Forbid();

            var list = await _doctorService.GetDoctorsAsync(hospital.HospitalId);
            return Ok(ApiResponse<object>.Ok(list, "Doctors retrieved successfully."));
        }

        /// <summary>
        /// Returns a specific Doctor by ID. Hospital Staff can only fetch doctors from their own hospital.
        /// </summary>
        [HttpGet("{id:guid}")]
        [Authorize(Roles = "HospitalStaff,Admin,Doctor")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetDoctorById(Guid id)
        {
            var doctor = await _doctorService.GetDoctorByIdAsync(id);
            if (doctor == null)
                return NotFound(ApiResponse<object>.Fail($"Doctor with ID {id} not found."));

            var userRoles = _currentUserService.Roles.ToList();

            // Hospital staff: verify the doctor belongs to their hospital
            if (userRoles.Contains("HospitalStaff") && !userRoles.Contains("Admin"))
            {
                var userEmail = _currentUserService.Email;
                var hospital = await _context.Hospitals
                    .FirstOrDefaultAsync(h => h.Email != null && h.Email.ToLower() == userEmail!.Trim().ToLower());

                if (hospital == null || doctor.HospitalId != hospital.HospitalId)
                    return NotFound(ApiResponse<object>.Fail($"Doctor with ID {id} not found."));
            }
            // Doctors may only fetch their own record
            else if (userRoles.Contains("Doctor") && !userRoles.Contains("Admin") && doctor.UserId != _currentUserService.UserId)
            {
                return NotFound(ApiResponse<object>.Fail($"Doctor with ID {id} not found."));
            }

            return Ok(ApiResponse<DoctorResponseDto>.Ok(doctor, "Doctor retrieved successfully."));
        }

        /// <summary>
        /// Deletes a doctor created by the authenticated hospital, including the doctor's login account.
        /// Blood request history handled by the doctor is preserved.
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteDoctor(Guid id)
        {
            var hospitalId = await _hospitalService.GetHospitalIdByEmailAsync(_currentUserService.Email);
            if (hospitalId == null)
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Your account is not linked to a hospital."));

            try
            {
                await _doctorService.DeleteDoctorAsync(id, hospitalId.Value);
                return Ok(ApiResponse<object>.Ok(null!, "Doctor deleted successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }
    }
}
