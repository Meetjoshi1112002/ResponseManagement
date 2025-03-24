using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ReponseManagement.Services
{
    public class JWTHelper
    {
        private readonly string _secretKey;

        public JWTHelper(IConfiguration configuration)
        {
            _secretKey = configuration["JwtSettings:SecretKey"] ?? throw new ArgumentNullException("Secret key is missing in config.");
        }

        public string GenerateToken(Dictionary<string, string> claims)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);

            // Convert dictionary claims to Claim objects
            var claimsIdentity = claims.Select(c => new Claim(c.Key, c.Value)).ToList();

            // Handle expiration if provided in claims
            DateTime? expiryTime = null;
            if (claims.TryGetValue("expiryTime", out string? expiryMinutesStr) && 
                !string.IsNullOrEmpty(expiryMinutesStr) && 
                int.TryParse(expiryMinutesStr, out int expiryMinutes))
            {
                expiryTime = DateTime.UtcNow.AddMinutes(expiryMinutes);
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claimsIdentity),
                Expires = expiryTime,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public Dictionary<string, string> DecryptToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key)
            };

            try
            {
                var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
                var jwtToken = (JwtSecurityToken)validatedToken;

                return jwtToken.Claims.ToDictionary(c => c.Type, c => c.Value);
            }
            catch (Exception ex)
            {
                throw new SecurityTokenException("Invalid token: " + ex.Message);
            }
        }
    }
}
