using System.Threading.Tasks;

namespace LifeLink.Services.Auth
{
    public interface IEmailService
    {
        Task SendPasswordResetEmailAsync(string email, string resetToken);
        Task SendEmailAsync(string toEmail, string subject, string body);
    }
}
