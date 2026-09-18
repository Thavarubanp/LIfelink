using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Auth
{
    public class DevelopmentEmailService : IEmailService
    {
        private readonly ILogger<DevelopmentEmailService> _logger;

        public DevelopmentEmailService(ILogger<DevelopmentEmailService> logger)
        {
            _logger = logger;
        }

        public Task SendPasswordResetEmailAsync(string email, string resetToken)
        {
            // In development, log the password reset attempt safely.
            _logger.LogInformation("Development Email Service: Password reset token requested for recipient {Email}. Token: {Token}", email, resetToken);
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(string toEmail, string subject, string body)
        {
            // In development, log outgoing email safely.
            _logger.LogInformation("Development Email Service: Dispatched email to {Recipient}. Subject: {Subject}. Body: {Body}", toEmail, subject, body);
            return Task.CompletedTask;
        }
    }
}
