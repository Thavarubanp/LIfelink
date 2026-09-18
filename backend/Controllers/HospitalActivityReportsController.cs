using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.HospitalActivity;
using LifeLink.Services.HospitalActivity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/hospital/activity-reports")]
    [Authorize(Roles = "HospitalStaff")]
    [AllowSuspendedAccess]
    public class HospitalActivityReportsController : ControllerBase
    {
        private readonly IHospitalActivityService _activityService;

        public HospitalActivityReportsController(IHospitalActivityService activityService)
        {
            _activityService = activityService;
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
                var result = await _activityService.SubmitActivityReportAsync(request);
                return StatusCode(StatusCodes.Status201Created, ApiResponse<ActivityReportResponseDto>.Ok(result, "Activity report submitted successfully."));
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
    }
}
