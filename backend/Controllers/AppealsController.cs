using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.DTOs.Appeals;
using LifeLink.DTOs.Common;
using LifeLink.Services.Appeals;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowSuspendedAccess]
    public class AppealsController : ControllerBase
    {
        private readonly IAppealService _appealService;
        private readonly ICurrentUserService _currentUserService;

        public AppealsController(IAppealService appealService, ICurrentUserService currentUserService)
        {
            _appealService = appealService;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Submits an appeal or explanation regarding an account or hospital suspension.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<AppealResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SubmitAppeal([FromBody] CreateAppealDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var currentUserId = _currentUserService.UserId;

            try
            {
                var result = await _appealService.SubmitAppealAsync(request, currentUserId);
                return StatusCode(StatusCodes.Status201Created, ApiResponse<AppealResponseDto>.Ok(result, "Appeal submitted successfully."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
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

        /// <summary>
        /// Retrieves the current user's submitted appeals.
        /// </summary>
        [HttpGet("my")]
        [Authorize]
        [ProducesResponseType(typeof(ApiResponse<List<AppealResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyAppeals()
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            var list = await _appealService.GetMyAppealsAsync(userId.Value);
            return Ok(ApiResponse<List<AppealResponseDto>>.Ok(list, "User appeals retrieved successfully."));
        }
    }
}
