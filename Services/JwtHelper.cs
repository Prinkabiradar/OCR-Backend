using Microsoft.IdentityModel.Tokens;
using OCR_BACKEND.Modals;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

public class JwtHelper
{
    private readonly IConfiguration _config;
    private readonly string _secret;

    public JwtHelper(IConfiguration config)
    {
        _config = config;
        _secret = config.GetSection("JwtConfig").GetSection("Key").Value;
    }

    public string GenerateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(ClaimTypes.Role, user.RoleId.ToString()),
            new Claim("Email", user.Email)
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"])
        );

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddMinutes(
                Convert.ToInt32(_config["Jwt:ExpiryMinutes"])
            ),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public TokenValidationResult ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secret);

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            SecurityToken validatedToken;
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out validatedToken);

            if (validatedToken != null && principal != null)
            {
                var user = new User
                {
                    UserName = principal.FindFirst(ClaimTypes.Name)?.Value,
                    RoleId = int.Parse(principal.FindFirst(ClaimTypes.Role)?.Value ?? "0"),
                    UserId = int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0")
                };

                return new TokenValidationResult
                {
                    IsValid = true,
                    Claims = principal.Claims,
                    User = user
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Token validation failed: {ex.Message}");
        }

        return new TokenValidationResult { IsValid = false };
    }

    public class TokenValidationResult
    {
        public bool IsValid { get; set; }
        public IEnumerable<Claim> Claims { get; set; }
        public User User { get; set; }
    }
}
