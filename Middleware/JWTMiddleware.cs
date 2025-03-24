using System.Security.Claims;
using ReponseManagement.Services;

namespace YourMicroservice.Middleware
{
    public class JwtUserMiddleware
    {
        private readonly RequestDelegate _next;

        private readonly JWTLoginHelper _jwtHelper;
        
        public JwtUserMiddleware(RequestDelegate next, JWTLoginHelper jwthelper)
        {
            _next = next;
            _jwtHelper = jwthelper;
        }
        
        public async Task InvokeAsync(HttpContext context)
        {
            // Try to get token from cookie
            context.Request.Cookies.TryGetValue("userToken", out string? token);
            
            // If token exists, try to validate it
            if (!string.IsNullOrEmpty(token))
            {
                var principal = _jwtHelper.ValidateToken(token);
                
                if (principal != null)
                {
                    // Extract userId from claims
                    var userIdClaim = principal.FindFirstValue("userId");
                    
                    if (!string.IsNullOrEmpty(userIdClaim))
                    {
                        // Add userId to HttpContext.Items for later retrieval in controllers
                        context.Items["UserId"] = userIdClaim;
                    }
                }
            }
            
            // Continue with the pipeline regardless of token presence/validity
            await _next(context);
        }
    }
    
    // Extension method for registering the middleware
    public static class JwtUserMiddlewareExtensions
    {
        public static IApplicationBuilder UseJwtUser(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JwtUserMiddleware>();
        }
    }
}