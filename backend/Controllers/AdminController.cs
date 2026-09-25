using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.Common;
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
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

        /// <summary>Approves a pending or rejected registration (409 if already approved or a newer hospital reply exists).</summary>
        [HttpPut("hospitals/{id:guid}/approve")]
        [ProducesResponseType(typeof(ApiResponse<AdminHospitalResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ApproveHospital(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveHospitalDto? dto)
        {
            try
            {
                var result = await _adminService.ApproveHospitalAsync(id, GetAdminId(), dto?.LastSeenEntryId);
                return Ok(ApiResponse<AdminHospitalResponseDto>.Ok(result, "Hospital approved successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (ConflictException ex)
            {
                return Conflict(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>Rejects a pending registration or a hospital reply awaiting review (409 while waiting for the hospital or once approved).</summary>
        [HttpPut("hospitals/{id:guid}/reject")]
        [ProducesResponseType(typeof(ApiResponse<AdminHospitalResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RejectHospital(Guid id, [FromBody] RejectHospitalDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _adminService.RejectHospitalAsync(id, GetAdminId(), dto, dto.LastSeenEntryId);
                return Ok(ApiResponse<AdminHospitalResponseDto>.Ok(result, "Hospital registration rejected."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (ConflictException ex)
            {
                return Conflict(ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>Admin comment in a rejected registration's conversation; the registration stays Rejected.</summary>
        [HttpPost("hospitals/{id:guid}/comments")]
        [ProducesResponseType(typeof(ApiResponse<AdminHospitalResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CommentOnHospitalRegistration(Guid id, [FromBody] HospitalRegistrationCommentDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _adminService.CommentOnHospitalRegistrationAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<AdminHospitalResponseDto>.Ok(result, "Comment sent to the hospital."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (ConflictException ex)
            {
                return Conflict(ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
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
                var result = await _adminService.SuspendUserAsync(id, dto, GetAdminId());
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
                var result = await _adminService.ReinstateUserAsync(id, GetAdminId());
                return Ok(ApiResponse<AdminUserResponseDto>.Ok(result, "User reinstated successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>Permanently blocks a donor/patient account (not Admin, HospitalStaff or Doctor accounts, not yourself).</summary>
        [HttpPut("users/{id:guid}/block")]
        [ProducesResponseType(typeof(ApiResponse<AdminUserResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> BlockUser(Guid id)
        {
            try
            {
                var result = await _adminService.BlockUserAsync(id, GetAdminId());
                return Ok(ApiResponse<AdminUserResponseDto>.Ok(result, "Account permanently blocked."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Transfers Admin ownership to an active donor/patient; the calling Admin becomes a normal User and is signed out.
        /// </summary>
        [HttpPut("users/{id:guid}/promote")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PromoteToAdmin(Guid id)
        {
            try
            {
                await _adminService.PromoteToAdminAsync(id, GetAdminId());
                return Ok(ApiResponse<object>.Ok(null!, "Admin ownership transferred. You are now a normal user and will be signed out."));
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

        /// <summary>
        /// Admin reply (the only admin complaint action). Replies alternate with the complaint creator;
        /// an optional attachment may be included. Admins cannot resolve, reject or delete complaints.
        /// </summary>
        [HttpPut("complaints/{id:guid}/review")]
        [ProducesResponseType(typeof(ApiResponse<ComplaintResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReplyToComplaint(Guid id, [FromBody] ReviewComplaintDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _complaintService.AdminReplyAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<ComplaintResponseDto>.Ok(result, "Reply sent."));
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

        [HttpPut("appeals/{id:guid}/reply")]
        [ProducesResponseType(typeof(ApiResponse<AppealResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReplyToAppeal(Guid id, [FromBody] ReviewComplaintDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var result = await _appealService.AdminReplyAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<AppealResponseDto>.Ok(result, "Reply sent."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>Permanently closes an appeal thread (read-only). The suspension itself is unchanged.</summary>
        [HttpPut("appeals/{id:guid}/close")]
        [ProducesResponseType(typeof(ApiResponse<AppealResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CloseAppeal(Guid id, [FromBody] ReviewAppealDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var result = await _appealService.CloseAppealAsync(id, GetAdminId(), dto);
                return Ok(ApiResponse<AppealResponseDto>.Ok(result, "Appeal thread closed."));
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
