using System;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace LifeLink.Services.Auth
{
    public class MailKitEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MailKitEmailService> _logger;

        public MailKitEmailService(IConfiguration configuration, ILogger<MailKitEmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendPasswordResetOtpAsync(string email, string otp)
        {
            _logger.LogInformation("MailKit: Initiating OTP Email Delivery. Recipient={Email}", email);

            string subject = "LifeLink - Password Reset Verification Code";
            string body = $@"
                <div style=""font-family: Arial, sans-serif; background-color: #0f172a; color: #f8fafc; padding: 30px; border-radius: 12px; max-width: 600px; margin: 0 auto; border: 1px solid #1e293b;"">
                    <div style=""text-align: center; margin-bottom: 24px;"">
                        <h1 style=""color: #ef4444; margin: 0; font-size: 28px;"">💉 LifeLink</h1>
                        <p style=""color: #94a3b8; font-size: 14px; margin-top: 4px;"">Emergency Blood Coordination Platform</p>
                    </div>
                    <div style=""background-color: #1e293b; padding: 24px; border-radius: 8px; border-left: 4px solid #ef4444;"">
                        <h2 style=""color: #ffffff; font-size: 18px; margin-top: 0;"">Password Reset Verification</h2>
                        <p style=""color: #cbd5e1; font-size: 14px; line-height: 1.6;"">
                            We received a request to reset your password for your LifeLink account (<strong>{email}</strong>).
                        </p>
                        <p style=""color: #cbd5e1; font-size: 14px;"">Please use the following 6-digit verification code (OTP) to proceed with your password reset:</p>
                        <div style=""text-align: center; margin: 24px 0;"">
                            <span style=""display: inline-block; font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #38bdf8; background-color: #0f172a; padding: 12px 28px; border-radius: 8px; border: 1px solid #334155;"">{otp}</span>
                        </div>
                        <p style=""color: #94a3b8; font-size: 12px; margin-bottom: 0;"">
                            ⚠️ This code is valid for <strong>10 minutes</strong>. Do not share this code with anyone. If you did not request a password reset, please ignore this email.
                        </p>
                    </div>
                </div>";

            await SendEmailAsync(email, subject, body, isBodyHtml: true);
        }

        public async Task SendPasswordResetEmailAsync(string email, string resetToken)
        {
            await SendPasswordResetOtpAsync(email, resetToken);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body, bool isBodyHtml = true)
        {
            _logger.LogInformation("[MailKit] Preparing email dispatch. Target={Recipient}, Subject={Subject}", toEmail, subject);

            var smtpHost = _configuration["Smtp:Host"] ?? "smtp.gmail.com";
            var smtpPortStr = _configuration["Smtp:Port"];
            int smtpPort = int.TryParse(smtpPortStr, out int p) ? p : 587;
            var smtpUsername = _configuration["Smtp:Username"] ?? _configuration["Smtp:User"];
            var smtpPassword = _configuration["Smtp:Password"] ?? _configuration["Smtp:Pass"];
            if (smtpPassword != null)
            {
                smtpPassword = smtpPassword.Trim();
            }
            var fromEmail = _configuration["Smtp:FromEmail"] ?? smtpUsername ?? "noreply@lifelink.org";
            var fromName = _configuration["Smtp:FromName"] ?? "LifeLink Platform";

            bool isConfigured = !string.IsNullOrWhiteSpace(smtpUsername) &&
                                !string.IsNullOrWhiteSpace(smtpPassword) &&
                                !smtpUsername.Contains("YOUR_GMAIL_ADDRESS");

            if (isConfigured)
            {
                try
                {
                    var cleanFromEmail = (fromEmail ?? string.Empty).Trim();
                    var cleanToEmail = (toEmail ?? string.Empty).Trim();
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress(fromName, cleanFromEmail));
                    message.To.Add(new MailboxAddress(cleanToEmail, cleanToEmail));
                    message.Subject = subject;

                    var bodyBuilder = new BodyBuilder();
                    if (isBodyHtml)
                    {
                        bodyBuilder.HtmlBody = body;
                    }
                    else
                    {
                        bodyBuilder.TextBody = body;
                    }
                    message.Body = bodyBuilder.ToMessageBody();

                    _logger.LogInformation("[MailKit] Connecting to SMTP Server {Host}:{Port}...", smtpHost, smtpPort);
                    using (var client = new SmtpClient())
                    {
                        // Secure TLS connection
                        await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
                        _logger.LogInformation("[MailKit] Connected to {Host}:{Port}. Authenticating user {User}...", smtpHost, smtpPort, smtpUsername);
                        
                        await client.AuthenticateAsync(smtpUsername, smtpPassword);
                        _logger.LogInformation("[MailKit] Authentication successful. Delivering message to {Recipient}...", toEmail);
                        
                        await client.SendAsync(message);
                        await client.DisconnectAsync(true);
                    }

                    _logger.LogInformation("[MailKit] Real OTP Email successfully delivered via Gmail SMTP to {Recipient}", toEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[MailKit Error] Failed to deliver email to {Recipient} via Gmail SMTP at {Host}:{Port}. Error: {Message}", toEmail, smtpHost, smtpPort, ex.Message);
                    throw; // Do not swallow exceptions so failure is detected cleanly
                }
            }
            else
            {
                _logger.LogWarning(
                    "[MailKit Configuration Warning] Gmail SMTP credentials are not configured in appsettings.Development.json or appsettings.json. " +
                    "To enable real email sending to Gmail, set Smtp:Username to your Gmail address and Smtp:Password to your 16-character App Password. " +
                    "Recipient={Recipient}, Subject={Subject}", toEmail, subject);
            }
        }
    }
}
