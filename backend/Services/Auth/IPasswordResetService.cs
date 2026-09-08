using System;
using System.Threading.Tasks;
using LifeLink.Entities;

namespace LifeLink.Services.Auth
{
    public interface IPasswordResetService
    {
        Task<string> CreatePasswordResetTokenAsync(User user);
        Task<PasswordResetToken?> ValidateTokenAsync(User user, string rawToken);
        Task MarkTokenAsUsedAsync(PasswordResetToken token);
        string HashToken(string rawToken);
    }
}
