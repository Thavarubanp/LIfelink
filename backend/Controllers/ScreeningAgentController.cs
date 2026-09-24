using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Acceptances;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/agent/screening")]
    [Authorize(Roles = "InternalAgent")]
    public class ScreeningAgentController : ControllerBase
    {
        private readonly IAcceptanceService _acceptanceService;
        private readonly ILogger<ScreeningAgentController> _logger;

        public ScreeningAgentController(
            IAcceptanceService acceptanceService,
            ILogger<ScreeningAgentController> logger)
        {
            _acceptanceService = acceptanceService;
            _logger = logger;
        }

        /// <summary>
        /// The Request Management agent submits a completed screening report. Each submission is stored as a new
        /// immutable version and routed to the assigned doctor; the agent never approves or rejects donors.
        /// </summary>
        [HttpPost("report-notify")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ReportNotify([FromBody] ScreeningReportNotificationDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.AcceptanceId))
            {
                return BadRequest(new { message = "Invalid screening report notification payload." });
            }

            try
            {
                var report = await _acceptanceService.SubmitScreeningReportAsync(dto);
                _logger.LogInformation("Screening report version {Version} stored for acceptance {AcceptanceId}", report.ReportVersion, dto.AcceptanceId);

                return Ok(new
                {
                    success = true,
                    message = "Screening report submitted to the doctor.",
                    reportId = dto.ReportId,
                    donorVerificationId = report.DonorVerificationId,
                    reportVersion = report.ReportVersion,
                    acceptanceId = dto.AcceptanceId
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}
