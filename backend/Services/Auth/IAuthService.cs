using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Auth;

namespace LifeLink.Services.Auth
{
    public interface IAuthService
    {
        Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request);
        Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
        Task<CurrentUserDto> GetCurrentUserAsync(Guid userId);
        Task ForgotPasswordAsync(ForgotPasswordRequestDto request);
        Task ResetPasswordAsync(ResetPasswordRequestDto request);
        Task ChangePasswordAsync(Guid userId, ChangePasswordRequestDto request);
        Task<UserScreeningProfileDto?> GetUserScreeningProfileAsync(Guid userId);
    }
}
