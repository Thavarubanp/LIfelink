using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.DTOs.Assistant;
using LifeLink.Services.Assistant;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LifeLink.Controllers
{
    /// <summary>
    /// Universal LifeLink assistant (Supervisor agent). Suspended accounts are stopped by the governance middleware
    /// like every other endpoint without [AllowSuspendedAccess].
    /// </summary>
    [ApiController]
    [Route("api/assistant")]
    [Authorize(Roles = "User,HospitalStaff,Doctor,Admin")]
    public class AssistantController : ControllerBase
    {
        private readonly IAssistantService _assistantService;
        private readonly ICurrentUserService _currentUserService;

        public AssistantController(IAssistantService assistantService, ICurrentUserService currentUserService)
        {
            _assistantService = assistantService;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Sends a message to the assistant. With acceptanceId it is a turn of the donor's own screening interview
        /// (an empty message resumes the interview).
        /// </summary>
        [HttpPost("chat")]
        [EnableRateLimiting("assistant")]
        [ProducesResponseType(typeof(AssistantChatResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Chat([FromBody] AssistantChatRequestDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = _currentUserService.UserId;
            if (!userId.HasValue)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            if (string.IsNullOrWhiteSpace(dto.Message) && !dto.AcceptanceId.HasValue)
            {
                return BadRequest(new { message = "Please type a message." });
            }

            try
            {
                var result = await _assistantService.ChatAsync(userId.Value, _currentUserService.Roles.ToList(), _currentUserService.Email, dto);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (AssistantUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
            }
        }
    }
}
