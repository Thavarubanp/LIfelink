using System;
using System.Threading.Tasks;
using LifeLink.Entities;

namespace LifeLink.Services.Auth
{
    public interface IPasswordResetService
    {
        Task<string> CreatePasswordResetTokenAsync(User user);
        Task<string> CreatePasswordResetOtpAsync(User user);
        Task<PasswordResetToken?> VerifyOtpAsync(User user, string otp);
        Task<PasswordResetToken?> ValidateVerifiedResetTokenAsync(User user, string resetSessionToken);
        Task<PasswordResetToken?> ValidateTokenAsync(User user, string rawToken);
        Task MarkTokenAsUsedAsync(PasswordResetToken token);
        Task<bool> IsResendOnCooldownAsync(User user);
        string HashToken(string rawToken);
    }
}
