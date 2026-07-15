 
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;   

namespace OCR_BACKEND.Services
{
    
    public interface IEmailService
    {
        Task SendOtpEmailAsync(string toEmail, string otp);
        Task SendEmailAsync(string toEmail, string subject, string htmlBody, IEnumerable<string>? ccEmails = null);
    }
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        public EmailService(IConfiguration config) => _config = config;

        public async Task SendOtpEmailAsync(string toEmail, string otp)
        {
            await SendEmailAsync(
                toEmail,
                "Your Password Reset OTP",
                $@"
                    <div style='font-family:Arial,sans-serif;max-width:400px;margin:auto;'>
                        <h2 style='color:#333;'>Password Reset OTP</h2>
                        <p>Use the OTP below to reset your password. 
                           It expires in <strong>10 minutes</strong>.</p>
                        <div style='font-size:36px;font-weight:bold;letter-spacing:8px;
                                    text-align:center;background:#f4f4f4;padding:20px;
                                    border-radius:8px;color:#007bff;'>
                            {otp}
                        </div>
                        <p style='color:#999;font-size:12px;margin-top:16px;'>
                            If you didn't request this, ignore this email.
                        </p>
                    </div>");
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, IEnumerable<string>? ccEmails = null)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _config["Email:SenderName"],
                _config["Email:SenderEmail"]
            ));
            message.To.Add(new MailboxAddress("", toEmail));
            foreach (var ccEmail in ccEmails ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(ccEmail))
                    message.Cc.Add(new MailboxAddress("", ccEmail));
            }
            message.Subject = subject;
            message.Body = new TextPart("html")
            {
                Text = htmlBody
            };

            using var client = new SmtpClient();
            client.CheckCertificateRevocation = _config.GetValue("Email:CheckCertificateRevocation", true);
            await client.ConnectAsync(
                _config["Email:SmtpHost"],
                int.Parse(_config["Email:SmtpPort"]),
                GetSecureSocketOption()
            );
            await client.AuthenticateAsync(
                _config["Email:SmtpUser"],
                _config["Email:SmtpPass"]
            );
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private SecureSocketOptions GetSecureSocketOption()
        {
            var configuredOption = _config["Email:SmtpSecurity"];
            if (Enum.TryParse<SecureSocketOptions>(configuredOption, ignoreCase: true, out var option))
                return option;

            return int.TryParse(_config["Email:SmtpPort"], out var port) && port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
        }
    }
}
