using OCR_BACKEND.Modals;
using System.Security.Claims;

namespace OCR_BACKEND.Services
{
    public interface IUserService
    {
        Task<User?> AuthenticateUserAsync(string username, string password);
        Task<User> GetUserByAccessToken(string accessToken);
    }

    public class UserService : IUserService
    {
        private readonly UserDBHelper _db;

        private readonly IConfiguration _config;

        public UserService(UserDBHelper db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public Task<User?> AuthenticateUserAsync(string username, string password)
        {
            return _db.AuthenticateUserAsync(username, password);
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
                    var companyLogoClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == "CompanyLogoURL"); // New
                    var locationIdClaim = tokenValidationResult.Claims.FirstOrDefault(claim => claim.Type == "LocationId");

                    if (idClaim != null && userNameClaim != null && roleClaim != null)
                    {
                        return new User
                        {
                            UserId = int.Parse(idClaim.Value),
                            UserName = userNameClaim.Value,
                            RoleId = int.Parse(roleIdClaim.Value),
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
    }

}
