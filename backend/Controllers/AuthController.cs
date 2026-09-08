using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Auth;
using LifeLink.DTOs.Common;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ICurrentUserService _currentUserService;

        public AuthController(IAuthService authService, ICurrentUserService currentUserService)
        {
            _authService = authService;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Registers a new LifeLink user account.
        /// </summary>
        [HttpPost("register")]
        [ProducesResponseType(typeof(ApiResponse<RegisterResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _authService.RegisterAsync(request);
            return StatusCode(StatusCodes.Status201Created, ApiResponse<RegisterResponseDto>.Ok(result, "Account created successfully."));
        }

        /// <summary>
        /// Authenticates user and returns JWT access token.
        /// </summary>
        [HttpPost("login")]
        [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _authService.LoginAsync(request);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result, "Login successful."));
        }

        /// <summary>
        /// Logs out the user session.
        /// </summary>
        [HttpPost("logout")]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        public IActionResult Logout()
        {
            return Ok(ApiResponse<string>.Ok("Logout successful.", "Logged out successfully. Tokens can be cleared on client."));
        }

        /// <summary>
        /// Gets the current authenticated user's profile information.
        /// </summary>
        [HttpGet("me")]
        [Authorize]
        [ProducesResponseType(typeof(ApiResponse<CurrentUserDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetCurrentUser()
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            var result = await _authService.GetCurrentUserAsync(userId.Value);
            return Ok(ApiResponse<CurrentUserDto>.Ok(result, "Current user fetched successfully."));
        }

        /// <summary>
        /// Initiates password reset flow with anti-enumeration protection.
        /// </summary>
        [HttpPost("forgot-password")]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _authService.ForgotPasswordAsync(request);
            return Ok(ApiResponse<string>.Ok(
                "If an account exists for this email, a password reset link has been sent.",
                "Password reset link dispatched if email exists."
            ));
        }

        /// <summary>
        /// Resets password using valid token.
        /// </summary>
        [HttpPost("reset-password")]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _authService.ResetPasswordAsync(request);
            return Ok(ApiResponse<string>.Ok("Password has been reset successfully.", "Password reset completed successfully."));
        }

        /// <summary>
        /// Changes password for currently authenticated user.
        /// </summary>
        [HttpPost("change-password")]
        [Authorize]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = _currentUserService.UserId;
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            await _authService.ChangePasswordAsync(userId.Value, request);
            return Ok(ApiResponse<string>.Ok("Password updated successfully.", "Password updated successfully."));
        }
    }
}
