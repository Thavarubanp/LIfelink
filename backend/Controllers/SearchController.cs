using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Search;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/search")]
    [Authorize]
    [AllowSuspendedAccess]
    public class SearchController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public SearchController(AppDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        [HttpGet]
        public async Task<IActionResult> GlobalSearch([FromQuery] string? q)
        {
            var response = new SearchResponseDto();

            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            {
                return Ok(response);
            }

            var term = q.Trim().ToLower();
            bool isCallerAdmin = _currentUserService.Roles.Contains("Admin");

            // 1. Search Hospitals
            var hospitals = await _context.Hospitals
                .Where(h => h.Name.ToLower().Contains(term) ||
                            (h.Email != null && h.Email.ToLower().Contains(term)) ||
                            (h.City != null && h.City.ToLower().Contains(term)) ||
                            (h.Address != null && h.Address.ToLower().Contains(term)))
                .OrderByDescending(h => h.IsVerified)
                .ThenBy(h => h.Name)
                .Take(5)
                .Select(h => new SearchItemDto
                {
                    ResultType = "Hospital",
                    Id = h.HospitalId,
                    DisplayName = h.Name,
                    SubText = !string.IsNullOrEmpty(h.City) ? $"{h.City} • {(h.IsVerified ? "Verified" : "Pending")}" : (h.Address ?? "Hospital Facility"),
                    ExtraInfo = h.ContactNumber,
                    AvatarInitial = "H",
                    Route = $"/profiles/hospital/{h.HospitalId}"
                })
                .ToListAsync();

            // 2. Search Doctors — Admins see all, Hospital Staff only their hospital's doctors, Doctors only themselves, Users none
            var doctorsQuery = _context.Doctors.Include(d => d.Hospital).AsQueryable();
            if (!isCallerAdmin)
            {
                var callerRoles = _currentUserService.Roles.ToList();
                Guid? staffHospitalId = null;
                if (callerRoles.Contains("HospitalStaff") && !string.IsNullOrWhiteSpace(_currentUserService.Email))
                {
                    var email = _currentUserService.Email.Trim().ToLower();
                    staffHospitalId = await _context.Hospitals
                        .Where(h => h.Email != null && h.Email.ToLower() == email)
                        .Select(h => (Guid?)h.HospitalId)
                        .FirstOrDefaultAsync();
                }
                var callerUserId = _currentUserService.UserId;
                bool isDoctor = callerRoles.Contains("Doctor");

                doctorsQuery = doctorsQuery.Where(d =>
                    (staffHospitalId != null && d.HospitalId == staffHospitalId) ||
                    (isDoctor && callerUserId != null && d.UserId == callerUserId));
            }

            var doctors = await doctorsQuery
                .Where(d => d.FirstName.ToLower().Contains(term) ||
                            d.LastName.ToLower().Contains(term) ||
                            (d.FirstName + " " + d.LastName).ToLower().Contains(term) ||
                            (d.Email != null && d.Email.ToLower().Contains(term)) ||
                            (d.Specialization != null && d.Specialization.ToLower().Contains(term)))
                .OrderBy(d => d.LastName)
                .Take(5)
                .Select(d => new SearchItemDto
                {
                    ResultType = "Doctor",
                    Id = d.DoctorId,
                    DisplayName = $"Dr. {d.FirstName} {d.LastName}".Trim(),
                    SubText = $"{d.Specialization} • {(d.Hospital != null ? d.Hospital.Name : "Hospital")}",
                    ExtraInfo = d.LicenseNumber,
                    AvatarInitial = "Dr",
                    Route = $"/profiles/doctor/{d.DoctorId}"
                })
                .ToListAsync();

            // 3. Search Users (Admin accounts strictly hidden from non-admin viewers).
            // Doctor and HospitalStaff login accounts are excluded because they are already
            // returned above as Doctor / Hospital results with their own profile pages.
            var usersQuery = _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Where(u => !u.UserRoles.Any(ur => ur.Role.Name == "Doctor" || ur.Role.Name == "HospitalStaff"))
                .AsQueryable();

            if (!isCallerAdmin)
            {
                usersQuery = usersQuery.Where(u => !u.UserRoles.Any(ur => ur.Role.Name == "Admin"));
            }

            var users = await usersQuery
                .Where(u => u.FirstName.ToLower().Contains(term) ||
                            u.LastName.ToLower().Contains(term) ||
                            (u.FirstName + " " + u.LastName).ToLower().Contains(term) ||
                            (u.Email != null && u.Email.ToLower().Contains(term)))
                .OrderBy(u => u.FirstName)
                .Take(5)
                .Select(u => new SearchItemDto
                {
                    ResultType = "User",
                    Id = u.UserId,
                    DisplayName = $"{u.FirstName} {u.LastName}".Trim(),
                    SubText = u.Email,
                    ExtraInfo = u.UserRoles.Select(ur => ur.Role.Name).FirstOrDefault() ?? "Donor / Patient",
                    AvatarInitial = !string.IsNullOrEmpty(u.FirstName) ? u.FirstName.Substring(0, 1).ToUpper() : "U",
                    Route = $"/profiles/user/{u.UserId}"
                })
                .ToListAsync();

            response.Hospitals = hospitals;
            response.Doctors = doctors;
            response.Users = users;

            return Ok(response);
        }
    }
}
