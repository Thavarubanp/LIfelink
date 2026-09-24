using System;
using System.Threading.Tasks;
using LifeLink.Common;
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
        [AllowSuspendedAccess]
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
        [AllowSuspendedAccess]
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
        /// Donor/patient deletes their own account: personal data and login removed, history kept (shown as "Deleted User").
        /// The same email can register again. Not available while suspended (blocked by the governance middleware).
        /// </summary>
        [HttpDelete("me")]
        [Authorize(Roles = "User")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> DeleteMyAccount()
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            await _authService.DeleteMyAccountAsync(userId.Value);
            return Ok(ApiResponse<object>.Ok(null!, "Your account has been deleted."));
        }

        /// <summary>
        /// Retrieves minimal user profile for AI donor screening (Least Privilege: ID, Name, Gender, DOB only).
        /// </summary>
        [HttpGet("user/{id:guid}")]
        [Authorize(Roles = "InternalAgent,Admin,HospitalStaff")]
        [ProducesResponseType(typeof(UserScreeningProfileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetUserById(Guid id)
        {
            var profile = await _authService.GetUserScreeningProfileAsync(id);
            if (profile == null)
            {
                return NotFound(new { message = $"User with ID '{id}' was not found." });
            }

            return Ok(profile);
        }

        /// <summary>
        /// Initiates password reset flow by sending a 6-digit OTP code to the requested email (Anti-enumeration enabled).
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
                "If an account exists for this email, a 6-digit verification OTP code has been sent.",
                "OTP verification code dispatched if account exists."
            ));
        }

        /// <summary>
        /// Verifies a 6-digit OTP code and returns a reset session token upon success.
        /// </summary>
        [HttpPost("verify-otp")]
        [ProducesResponseType(typeof(ApiResponse<VerifyOtpResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _authService.VerifyOtpAsync(request);
            return Ok(ApiResponse<VerifyOtpResponseDto>.Ok(result, "OTP verified successfully."));
        }

        /// <summary>
        /// Resends a 6-digit verification OTP code to the user's email.
        /// </summary>
        [HttpPost("resend-otp")]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _authService.ResendOtpAsync(request);
            return Ok(ApiResponse<string>.Ok(
                "If an account exists, a new 6-digit OTP has been sent.",
                "New OTP code dispatched if account exists."
            ));
        }

        /// <summary>
        /// Resets password after successful OTP verification.
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
