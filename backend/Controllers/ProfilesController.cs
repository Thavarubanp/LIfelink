using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Profiles;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/profiles")]
    [AllowSuspendedAccess]
    public class ProfilesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public ProfilesController(AppDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Gets a hospital profile.
        /// Public / all users can view hospital profiles.
        /// Donors and patients cannot see blood inventory details.
        /// Hospital Staff, Doctors, and Admins can view blood inventory details.
        /// </summary>
        [HttpGet("hospital/{id:guid}")]
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
                CanViewInventory = canViewInventory
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
                CreatedAt = user.CreatedAt
            };

            return Ok(dto);
        }

        /// <summary>
        /// Gets a doctor profile with affiliated hospital details.
        /// </summary>
        [HttpGet("doctor/{id:guid}")]
        [Authorize]
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
                CreatedAt = doctor.CreatedAt
            };

            return Ok(dto);
        }
    }
}
