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
    [Authorize(Roles = "InternalAgent,Admin")]
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
        /// Callback endpoint for Agent 1 to notify the backend of completed screening report metadata.
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

            _logger.LogInformation(
                "Received AI screening report callback: ReportId={ReportId}, AcceptanceId={AcceptanceId}, RiskLevel={RiskLevel}, Recommendation={Recommendation}",
                dto.ReportId,
                dto.AcceptanceId,
                dto.RiskLevel,
                dto.Recommendation
            );

            if (Guid.TryParse(dto.AcceptanceId, out var acceptanceGuid))
            {
                try
                {
                    // Ensure acceptance status moves to ScreeningCompleted
                    await _acceptanceService.UpdateScreeningStatusAsync(acceptanceGuid, AcceptanceStatus.ScreeningCompleted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not auto-transition acceptance {AcceptanceId} status: {Message}", dto.AcceptanceId, ex.Message);
                }
            }

            return Ok(new
            {
                success = true,
                message = "Screening report notification recorded successfully.",
                reportId = dto.ReportId,
                acceptanceId = dto.AcceptanceId
            });
        }
    }
}
