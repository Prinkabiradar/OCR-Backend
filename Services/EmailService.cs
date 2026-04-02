 
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;   

namespace OCR_BACKEND.Services
{
    
    public interface IEmailService
    {
        Task SendOtpEmailAsync(string toEmail, string otp);
    }
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        public EmailService(IConfiguration config) => _config = config;

        public async Task SendOtpEmailAsync(string toEmail, string otp)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _config["Email:SenderName"],
                _config["Email:SenderEmail"]
            ));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = "Your Password Reset OTP";
            message.Body = new TextPart("html")
            {
                Text = $@"
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
                    </div>"
            };

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _config["Email:SmtpHost"],
                int.Parse(_config["Email:SmtpPort"]),
                SecureSocketOptions.StartTls    
            );
            await client.AuthenticateAsync(
                _config["Email:SmtpUser"],
                _config["Email:SmtpPass"]
            );
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}