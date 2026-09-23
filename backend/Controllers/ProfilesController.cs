using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Profiles;
using LifeLink.Services.Common;
using LifeLink.Services.Hospitals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    /// <summary>
    /// Profile viewing (allowed in Restricted Governance Mode) and own-profile editing.
    /// Edit endpoints carry no [AllowSuspendedAccess], so suspended accounts are blocked by
    /// RestrictedGovernanceModeMiddleware. Editing anyone else's profile returns 403.
    /// </summary>
    [ApiController]
    [Route("api/profiles")]
    public class ProfilesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUserService;
        private readonly IHospitalService _hospitalService;

        public ProfilesController(AppDbContext context, ICurrentUserService currentUserService, IHospitalService hospitalService)
        {
            _context = context;
            _currentUserService = currentUserService;
            _hospitalService = hospitalService;
        }

        /// <summary>
        /// Gets a hospital profile.
        /// Public / all users can view hospital profiles.
        /// Donors and patients cannot see blood inventory details.
        /// Hospital Staff, Doctors, and Admins can view blood inventory details.
        /// </summary>
        [HttpGet("hospital/{id:guid}")]
        [AllowSuspendedAccess]
        public async Task<IActionResult> GetHospitalProfile(Guid id)
        {
            var hospital = await _context.Hospitals
                .Include(h => h.Doctors)
                .FirstOrDefaultAsync(h => h.HospitalId == id);

            if (hospital == null)
            {
                return NotFound(ApiResponse<object>.Fail("Hospital profile not found."));
            }

            var callerRoles = _currentUserService.Roles.ToList();
            bool canViewInventory = callerRoles.Any(r =>
                r.Equals("HospitalStaff", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("Doctor", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("Admin", StringComparison.OrdinalIgnoreCase));

            var dto = new HospitalProfileDto
            {
                HospitalId = hospital.HospitalId,
                Name = hospital.Name,
                LicenseNumber = hospital.LicenseNumber,
                Address = hospital.Address,
                ContactNumber = hospital.ContactNumber,
                Email = hospital.Email,
                IsVerified = hospital.IsVerified,
                City = hospital.City,
                ContactPersonName = hospital.ContactPersonName,
                ContactPersonPhone = hospital.ContactPersonPhone,
                ContactPersonEmail = hospital.ContactPersonEmail,
                RegistrationNumber = hospital.RegistrationNumber,
                CreatedAt = hospital.CreatedAt,
                DoctorCount = hospital.Doctors.Count(d => d.IsActive),
                CanViewInventory = canViewInventory,
                CanEdit = await IsOwnHospitalAsync(id)
            };

            if (canViewInventory)
            {
                dto.Inventory = await _context.BloodInventories
                    .Where(bi => bi.HospitalId == id)
                    .OrderBy(bi => bi.BloodGroup)
                    .Select(bi => new HospitalInventoryItemDto
                    {
                        InventoryId = bi.InventoryId,
                        BloodGroup = bi.BloodGroup,
                        UnitsAvailable = bi.UnitsAvailable,
                        MinimumThreshold = bi.MinimumThreshold,
                        MaximumCapacity = bi.MaximumCapacity,
                        LastUpdated = bi.LastUpdated
                    })
                    .ToListAsync();
            }

            return Ok(dto);
        }

        /// <summary>
        /// Gets a user profile.
        /// Admin profiles can only be viewed by administrators.
        /// </summary>
        [HttpGet("user/{id:guid}")]
        [Authorize]
        [AllowSuspendedAccess]
        public async Task<IActionResult> GetUserProfile(Guid id)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
            {
                return NotFound(ApiResponse<object>.Fail("User profile not found."));
            }

            var userRoles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            bool isTargetAdmin = userRoles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase));
            bool isCallerAdmin = _currentUserService.Roles.Contains("Admin");

            if (isTargetAdmin && !isCallerAdmin)
            {
                return NotFound(ApiResponse<object>.Fail("User profile not found."));
            }

            var dto = new UserProfileDto
            {
                UserId = user.UserId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Gender = user.Gender,
                Address = user.Address,
                Roles = userRoles,
                AccountStatus = user.AccountStatus.ToString(),
                CreatedAt = user.CreatedAt,
                CanEdit = IsOwnUserProfile(id)
            };

            return Ok(dto);
        }

        /// <summary>
        /// Gets a doctor profile with affiliated hospital details.
        /// </summary>
        [HttpGet("doctor/{id:guid}")]
        [Authorize]
        [AllowSuspendedAccess]
        public async Task<IActionResult> GetDoctorProfile(Guid id)
        {
            var doctor = await _context.Doctors
                .Include(d => d.Hospital)
                .FirstOrDefaultAsync(d => d.DoctorId == id);

            if (doctor == null)
            {
                return NotFound(ApiResponse<object>.Fail("Doctor profile not found."));
            }

            var dto = new DoctorProfileDto
            {
                DoctorId = doctor.DoctorId,
                FirstName = doctor.FirstName,
                LastName = doctor.LastName,
                Email = doctor.Email,
                PhoneNumber = doctor.PhoneNumber,
                LicenseNumber = doctor.LicenseNumber, // SLMC Registration Number
                Specialization = doctor.Specialization,
                HospitalId = doctor.HospitalId,
                HospitalName = doctor.Hospital != null ? doctor.Hospital.Name : "Affiliated Hospital",
                HospitalAddress = doctor.Hospital != null ? (doctor.Hospital.Address ?? doctor.Hospital.City ?? "") : "",
                IsActive = doctor.IsActive,
                CreatedAt = doctor.CreatedAt,
                CanEdit = IsOwnDoctorProfile(doctor.UserId)
            };

            return Ok(dto);
        }

        /// <summary>
        /// Resolves the caller's own profile page: doctor profile for Doctors, hospital profile for
        /// Hospital Staff, otherwise the user profile (Users and Admins).
        /// </summary>
        [HttpGet("me")]
        [Authorize]
        [AllowSuspendedAccess]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
                return Unauthorized(ApiResponse<object>.Fail("Unable to resolve authenticated user."));

            var roles = _currentUserService.Roles.ToList();

            if (roles.Contains("Doctor"))
            {
                var doctorId = await _context.Doctors
                    .Where(d => d.UserId == userId)
                    .Select(d => (Guid?)d.DoctorId)
                    .FirstOrDefaultAsync();
                if (doctorId != null)
                    return Ok(new MyProfileDto { Type = "doctor", Id = doctorId.Value });
            }

            if (roles.Contains("HospitalStaff"))
            {
                var hospitalId = await _hospitalService.GetHospitalIdByEmailAsync(_currentUserService.Email);
                if (hospitalId != null)
                    return Ok(new MyProfileDto { Type = "hospital", Id = hospitalId.Value });
            }

            return Ok(new MyProfileDto { Type = "user", Id = userId.Value });
        }

        /// <summary>
        /// Users and Admins edit their own user profile. Email cannot be changed.
        /// </summary>
        [HttpPut("user/{id:guid}")]
        [Authorize(Roles = "User,Admin")]
        public async Task<IActionResult> UpdateUserProfile(Guid id, [FromBody] UpdateUserProfileDto dto)
        {
            if (!IsOwnUserProfile(id))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("You can only edit your own profile."));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == id);
            if (user == null)
                return NotFound(ApiResponse<object>.Fail("User profile not found."));

            user.FirstName = dto.FirstName.Trim();
            user.LastName = dto.LastName.Trim();
            user.PhoneNumber = dto.PhoneNumber.Trim();
            user.Gender = dto.Gender?.Trim() ?? string.Empty;
            user.Address = dto.Address?.Trim() ?? string.Empty;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return await GetUserProfile(id);
        }

        /// <summary>
        /// Doctors edit their own doctor profile (hospitals cannot edit doctor details).
        /// The SLMC number must stay unique; name and phone are kept in sync with the doctor's login account.
        /// </summary>
        [HttpPut("doctor/{id:guid}")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> UpdateDoctorProfile(Guid id, [FromBody] UpdateDoctorProfileDto dto)
        {
            var doctor = await _context.Doctors.Include(d => d.User).FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor == null)
                return NotFound(ApiResponse<object>.Fail("Doctor profile not found."));

            if (!IsOwnDoctorProfile(doctor.UserId))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("You can only edit your own profile."));

            var slmc = SlmcUniquenessHelper.Normalize(dto.LicenseNumber);
            if (slmc.Length == 0)
                return BadRequest(ApiResponse<object>.Fail("SLMC number is required."));
            if (await SlmcUniquenessHelper.IsSlmcTakenAsync(_context, slmc, doctor.DoctorId))
                return BadRequest(ApiResponse<object>.Fail(SlmcUniquenessHelper.DuplicateMessage));

            var now = DateTime.UtcNow;
            doctor.FirstName = dto.FirstName.Trim();
            doctor.LastName = dto.LastName.Trim();
            doctor.PhoneNumber = dto.PhoneNumber.Trim();
            doctor.Specialization = dto.Specialization?.Trim() ?? string.Empty;
            doctor.LicenseNumber = slmc;
            doctor.UpdatedAt = now;

            if (doctor.User != null)
            {
                doctor.User.FirstName = doctor.FirstName;
                doctor.User.LastName = doctor.LastName;
                doctor.User.PhoneNumber = doctor.PhoneNumber;
                doctor.User.UpdatedAt = now;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (SlmcUniquenessHelper.IsSlmcUniqueViolation(ex))
            {
                return BadRequest(ApiResponse<object>.Fail(SlmcUniquenessHelper.DuplicateMessage));
            }

            return await GetDoctorProfile(id);
        }

        /// <summary>
        /// Hospital staff edit their own hospital profile. Email, license number and registration number cannot be changed.
        /// </summary>
        [HttpPut("hospital/{id:guid}")]
        [Authorize(Roles = "HospitalStaff")]
        public async Task<IActionResult> UpdateHospitalProfile(Guid id, [FromBody] UpdateHospitalProfileDto dto)
        {
            if (!await IsOwnHospitalAsync(id))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("You can only edit your own hospital profile."));

            var hospital = await _context.Hospitals.FirstOrDefaultAsync(h => h.HospitalId == id);
            if (hospital == null)
                return NotFound(ApiResponse<object>.Fail("Hospital profile not found."));

            hospital.Name = dto.Name.Trim();
            hospital.Address = dto.Address.Trim();
            hospital.City = dto.City?.Trim();
            hospital.ContactNumber = dto.ContactNumber.Trim();
            hospital.ContactPersonName = dto.ContactPersonName?.Trim();
            hospital.ContactPersonPhone = string.IsNullOrWhiteSpace(dto.ContactPersonPhone) ? null : dto.ContactPersonPhone.Trim();
            hospital.ContactPersonEmail = string.IsNullOrWhiteSpace(dto.ContactPersonEmail) ? null : dto.ContactPersonEmail.Trim();
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return await GetHospitalProfile(id);
        }

        // Users and Admins own their user profile; Doctors and Hospital Staff edit their doctor/hospital profile instead.
        private bool IsOwnUserProfile(Guid userId)
        {
            var roles = _currentUserService.Roles.ToList();
            return _currentUserService.UserId == userId && (roles.Contains("User") || roles.Contains("Admin"));
        }

        private bool IsOwnDoctorProfile(Guid? doctorUserId) =>
            doctorUserId != null &&
            _currentUserService.UserId == doctorUserId &&
            _currentUserService.Roles.Contains("Doctor");

        private async Task<bool> IsOwnHospitalAsync(Guid hospitalId)
        {
            if (!_currentUserService.Roles.Contains("HospitalStaff"))
                return false;
            var ownHospitalId = await _hospitalService.GetHospitalIdByEmailAsync(_currentUserService.Email);
            return ownHospitalId == hospitalId;
        }
    }
}
