using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.Appeals;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Complaints;
using LifeLink.DTOs.HospitalActivity;
using LifeLink.Services.Admin;
using LifeLink.Services.Appeals;
using LifeLink.Services.Common;
using LifeLink.Services.Complaints;
using LifeLink.Services.HospitalActivity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IAdminService _adminService;
        private readonly IComplaintService _complaintService;
        private readonly IHospitalActivityService _activityService;
        private readonly IAppealService _appealService;
        private readonly ICurrentUserService _currentUserService;

        public AdminController(
            IAdminService adminService,
            IComplaintService complaintService,
            IHospitalActivityService activityService,
            IAppealService appealService,
            ICurrentUserService currentUserService)
        {
            _adminService = adminService;
            _complaintService = complaintService;
            _activityService = activityService;
            _appealService = appealService;
            _currentUserService = currentUserService;
        }

        private Guid GetAdminId()
        {
            return _currentUserService.UserId ?? Guid.Empty;
        }

        #region Dashboard

        [HttpGet("dashboard")]
        [ProducesResponseType(typeof(ApiResponse<AdminDashboardStatsDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDashboard()
        {
            var stats = await _adminService.GetDashboardStatsAsync();
            return Ok(ApiResponse<AdminDashboardStatsDto>.Ok(stats, "Admin dashboard statistics retrieved successfully."));
        }

        #endregion

        #region Hospital Approvals

        [HttpGet("hospitals/pending")]
        [ProducesResponseType(typeof(ApiResponse<List<AdminHospitalResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPendingHospitals()
        {
            var list = await _adminService.GetPendingHospitalsAsync();
            return Ok(ApiResponse<List<AdminHospitalResponseDto>>.Ok(list, "Pending hospitals retrieved successfully."));
        }

        [HttpPut("hospitals/{id:guid}/approve")]
        [ProducesResponseType(typeof(ApiResponse<AdminHospitalResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ApproveHospital(Guid id)
        {
            try
            {
                var result = await _adminService.ApproveHospitalAsync(id, GetAdminId());
                return Ok(ApiResponse<AdminHospitalResponseDto>.Ok(result, "Hospital approved successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPut("hospitals/{id:guid}/reject")]
        [ProducesResponseType(typeof(ApiResponse<AdminHospitalResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RejectHospital(Guid id, [FromBody] RejectHospitalDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _adminService.RejectHospitalAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<AdminHospitalResponseDto>.Ok(result, "Hospital registration rejected."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        #endregion

        #region Suspensions (User & Hospital)

        [HttpPut("users/{id:guid}/suspend")]
        [ProducesResponseType(typeof(ApiResponse<AdminUserResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SuspendUser(Guid id, [FromBody] SuspendUserDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _adminService.SuspendUserAsync(id, dto);
                return Ok(ApiResponse<AdminUserResponseDto>.Ok(result, "User suspended successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPut("users/{id:guid}/reinstate")]
        [ProducesResponseType(typeof(ApiResponse<AdminUserResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReinstateUser(Guid id)
        {
            try
            {
                var result = await _adminService.ReinstateUserAsync(id);
                return Ok(ApiResponse<AdminUserResponseDto>.Ok(result, "User reinstated successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPut("hospitals/{id:guid}/suspend")]
        [ProducesResponseType(typeof(ApiResponse<AdminHospitalResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SuspendHospital(Guid id, [FromBody] SuspendHospitalDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _adminService.SuspendHospitalAsync(id, dto);
                return Ok(ApiResponse<AdminHospitalResponseDto>.Ok(result, "Hospital suspended successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPut("hospitals/{id:guid}/reinstate")]
        [ProducesResponseType(typeof(ApiResponse<AdminHospitalResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReinstateHospital(Guid id)
        {
            try
            {
                var result = await _adminService.ReinstateHospitalAsync(id);
                return Ok(ApiResponse<AdminHospitalResponseDto>.Ok(result, "Hospital reinstated successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpGet("users")]
        [ProducesResponseType(typeof(ApiResponse<List<AdminUserResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetUsers()
        {
            var list = await _adminService.GetUsersAsync();
            return Ok(ApiResponse<List<AdminUserResponseDto>>.Ok(list, "Registered users retrieved successfully."));
        }

        [HttpGet("hospitals")]
        [ProducesResponseType(typeof(ApiResponse<List<AdminHospitalResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllHospitals()
        {
            var list = await _adminService.GetAllHospitalsAsync();
            return Ok(ApiResponse<List<AdminHospitalResponseDto>>.Ok(list, "Registered hospitals retrieved successfully."));
        }

        #endregion

        #region Complaints

        [HttpGet("complaints")]
        [ProducesResponseType(typeof(ApiResponse<List<ComplaintResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetComplaints([FromQuery] string? status)
        {
            var list = await _complaintService.GetComplaintsAsync(status);
            return Ok(ApiResponse<List<ComplaintResponseDto>>.Ok(list, "Complaints retrieved successfully."));
        }

        [HttpGet("complaints/{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<ComplaintResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetComplaintById(Guid id)
        {
            var complaint = await _complaintService.GetComplaintByIdAsync(id);
            if (complaint == null)
            {
                return NotFound(ApiResponse<object>.Fail($"Complaint with ID {id} was not found."));
            }
            return Ok(ApiResponse<ComplaintResponseDto>.Ok(complaint, "Complaint retrieved successfully."));
        }

        [HttpPut("complaints/{id:guid}/review")]
        [ProducesResponseType(typeof(ApiResponse<ComplaintResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReviewComplaint(Guid id, [FromBody] ReviewComplaintDto? dto)
        {
            try
            {
                var result = await _complaintService.ReviewComplaintAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<ComplaintResponseDto>.Ok(result, "Complaint marked under review."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPut("complaints/{id:guid}/request-activity")]
        [ProducesResponseType(typeof(ApiResponse<ComplaintResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RequestActivity(Guid id, [FromBody] RequestActivityReportDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _complaintService.RequestActivityReportAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<ComplaintResponseDto>.Ok(result, "Activity report requested from hospital successfully."));
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

        [HttpPut("complaints/{id:guid}/resolve")]
        [ProducesResponseType(typeof(ApiResponse<ComplaintResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ResolveComplaint(Guid id, [FromBody] ResolveComplaintDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _complaintService.ResolveComplaintAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<ComplaintResponseDto>.Ok(result, $"Complaint marked as {dto.Status}."));
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

        #endregion

        #region Hospital Activity Reports

        [HttpGet("activity-reports")]
        [ProducesResponseType(typeof(ApiResponse<List<ActivityReportResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetActivityReports([FromQuery] Guid? complaintId, [FromQuery] Guid? hospitalId)
        {
            var list = await _activityService.GetActivityReportsAsync(complaintId, hospitalId);
            return Ok(ApiResponse<List<ActivityReportResponseDto>>.Ok(list, "Activity reports retrieved successfully."));
        }

        [HttpGet("activity-reports/{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<ActivityReportResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetActivityReportById(Guid id)
        {
            var report = await _activityService.GetActivityReportByIdAsync(id);
            if (report == null)
            {
                return NotFound(ApiResponse<object>.Fail($"Activity report with ID {id} was not found."));
            }
            return Ok(ApiResponse<ActivityReportResponseDto>.Ok(report, "Activity report retrieved successfully."));
        }

        #endregion

        #region Appeals

        [HttpGet("appeals")]
        [ProducesResponseType(typeof(ApiResponse<List<AppealResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAppeals([FromQuery] string? status)
        {
            var list = await _appealService.GetAppealsAsync(status);
            return Ok(ApiResponse<List<AppealResponseDto>>.Ok(list, "Appeals retrieved successfully."));
        }

        [HttpPut("appeals/{id:guid}/approve")]
        [ProducesResponseType(typeof(ApiResponse<AppealResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ApproveAppeal(Guid id, [FromBody] ReviewAppealDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _appealService.ApproveAppealAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<AppealResponseDto>.Ok(result, "Appeal approved and entity reinstated successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPut("appeals/{id:guid}/reject")]
        [ProducesResponseType(typeof(ApiResponse<AppealResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RejectAppeal(Guid id, [FromBody] ReviewAppealDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _appealService.RejectAppealAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<AppealResponseDto>.Ok(result, "Appeal rejected."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPut("appeals/{id:guid}/permanently-block")]
        [ProducesResponseType(typeof(ApiResponse<AppealResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PermanentlyBlockAppeal(Guid id, [FromBody] ReviewAppealDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _appealService.PermanentlyBlockAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<AppealResponseDto>.Ok(result, "Account permanently blocked."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        #endregion
    }
}
