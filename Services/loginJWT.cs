using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ReponseManagement.Services
{
    public class JWTLoginHelper
    {
        private readonly IConfiguration _config;

        public JWTLoginHelper(IConfiguration config)
        {
            _config = config;
        }


        public ClaimsPrincipal? ValidateToken(string token)
        {
            var jwtSettings = _config.GetSection("JwtSettings2");
            var secretKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Secret"] ?? throw new ArgumentNullException("JWT Secret key is missing in config")));

            var tokenHandler = new JwtSecurityTokenHandler();
            
            try
            {
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = secretKey,
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSettings["Audience"],
                    ValidateLifetime = true, // Ensure token is not expired
                    ClockSkew = TimeSpan.Zero // Optional: Reduce default clock skew of 5 mins
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

                return principal; // Returns claims if the token is valid
            }
            catch
            {
                return null; // Return null if token is invalid or expired
            }
        }

    }
}