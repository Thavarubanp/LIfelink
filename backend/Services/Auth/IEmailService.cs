using System.Threading.Tasks;

namespace LifeLink.Services.Auth
{
    public interface IEmailService
    {
        Task SendPasswordResetEmailAsync(string email, string resetToken);
        Task SendPasswordResetOtpAsync(string email, string otp);
        Task SendEmailAsync(string toEmail, string subject, string body, bool isBodyHtml = false);
    }
}
