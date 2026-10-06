using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.HospitalActivity;
using LifeLink.Services.HospitalActivity;
using LifeLink.Services.Common;
using LifeLink.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/hospital/activity-reports")]
    [Authorize(Roles = "HospitalStaff")]
    public class HospitalActivityReportsController : ControllerBase
    {
        private readonly IHospitalActivityService _activityService;
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public HospitalActivityReportsController(IHospitalActivityService activityService, AppDbContext context, ICurrentUserService currentUserService)
        {
            _activityService = activityService;
            _context = context;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Submits an activity report or supporting evidence requested by Admin during an investigation.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<ActivityReportResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SubmitActivityReport([FromBody] SubmitActivityReportDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var hospitalId = await CallerHospitalResolver.ResolveAsync(_context, _currentUserService);
                if (hospitalId == null)
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Unable to resolve the authenticated hospital."));
                var result = await _activityService.SubmitActivityReportAsync(request, hospitalId.Value);
                return StatusCode(StatusCodes.Status201Created, ApiResponse<ActivityReportResponseDto>.Ok(result, "Activity report submitted successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }
    }
}
