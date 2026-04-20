using OCR_BACKEND.Modals;
using System.Security.Claims;

namespace OCR_BACKEND.Services
{
    public interface IUserService
    {
        Task<User?> AuthenticateUserAsync(string username, string password);
        Task<User> GetUserByAccessToken(string accessToken);
        Task<(bool Success, string Message)> SendOtpAsync(string emailOrMobile);
        Task<(bool Success, int UserId)> VerifyOtpAsync(string emailOrMobile, string otp);
        Task<bool> ResetPasswordAsync(int userId, string newPassword);
    }

    public class UserService : IUserService
    {
        private readonly UserDBHelper _db;

        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;

        public UserService(UserDBHelper db, IConfiguration config, IEmailService emailService)
        {
            _db = db;
            _config = config;
            _emailService = emailService;
        }

        public Task<User?> AuthenticateUserAsync(string username, string password)
        {
            var hashedPassword = PasswordHelper.HashPassword(password);

            return _db.AuthenticateUserAsync(username, hashedPassword);
        }
        public async Task<User> GetUserByAccessToken(string accessToken)
        {
            try
            {
                var jwtService = new JwtHelper(_config);
                    var tokenValidationResult = jwtService.ValidateToken(accessToken);

                    if (tokenValidationResult.IsValid)
                {
                    var idClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier);
                    var userNameClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Name);
                    var roleClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Role);
                    var roleIdClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == "RoleId");
                    var emailClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == "Email");
                    var roleNameClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == "RoleName");
                    var companyLogoClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == "CompanyLogoURL"); // New
                    var locationIdClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == "LocationId");

                    if (idClaim != null && userNameClaim != null && roleClaim != null)
                    {
                        return new User
                        {
                            UserId = int.Parse(idClaim.Value),
                            UserName = userNameClaim.Value,
                            RoleId = int.Parse(roleClaim.Value),
                            RoleName = roleNameClaim?.Value,
                            //email = emailClaim?.Value,
                            //CompanyLogoURL = companyLogoClaim?.Value,
                            //LocationId = int.Parse(locationIdClaim.Value) 
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token validation failed: {ex.Message}");
            }

            return null;
        }
        public async Task<(bool Success, string Message)> SendOtpAsync(string emailOrMobile)
        {
            var user = await _db.GetUserByEmailOrMobileAsync(emailOrMobile);
            if (user == null)
                return (false, "No account found with this email or mobile.");

            // Generate 6-digit OTP
            var otp = new Random().Next(100000, 999999).ToString();
            var expiresAt = DateTime.UtcNow.AddMinutes(10);

            await _db.SaveOtpAsync(user.UserId, otp, expiresAt);

            if (!string.IsNullOrEmpty(user.Email))
                await _emailService.SendOtpEmailAsync(user.Email, otp);

            return (true, "OTP sent to your registered email.");
        }

        public async Task<(bool Success, int UserId)> VerifyOtpAsync(string emailOrMobile, string otp)
        {
            var user = await _db.GetUserByEmailOrMobileAsync(emailOrMobile);
            if (user == null)
                return (false, 0);

            var isValid = await _db.VerifyOtpAsync(user.UserId, otp);
            return isValid ? (true, user.UserId) : (false, 0);
        }

        public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
        {
            var hashed = PasswordHelper.HashPassword(newPassword);
            await _db.ResetPasswordByUserIdAsync(userId, hashed);
            return true;
        }
    }

}
