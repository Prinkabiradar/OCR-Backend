using Microsoft.IdentityModel.Tokens;
using OCR_BACKEND.Modals;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

public class JwtHelper
{
    private readonly IConfiguration _config;

    public JwtHelper(IConfiguration config)
    {
        _config = config;
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
}
