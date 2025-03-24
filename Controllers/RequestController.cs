using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using ReponseManagement.Data;
using ReponseManagement.Models;
using ReponseManagement.Models.POCOs;
using ReponseManagement.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace ReponseManagement.Controllers
{
    [Route("api/form")]
    [ApiController]
    public class RequestController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMongoCollection<SurveySchema> _surveys;
        private readonly ILogger<RequestController> _logger;
        private readonly DTOConverter _converter;
        private readonly JWTHelper _jwtHelper;

        public RequestController(AppDbContext context, 
                                ILogger<RequestController> logger,
                                DTOConverter converter,
                                JWTHelper jwtHelper)
        {
            _context = context;
            _surveys = _context.GetCollection<SurveySchema>("Survey");
            _logger = logger;
            _converter = converter;
            _jwtHelper = jwtHelper;
        }

        [HttpGet("Demo")]
        public string GetResult(){
            return "Hello";
        }

        [HttpGet("{token}")]
        public async Task<IActionResult> RequestForm(string token)
        {
            try
            {
                IDictionary<string, string> claims = DecodeToken(token);

                if (!claims.ContainsKey("surveyId"))
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Invalid link"
                    });
                }

                string surveyId = claims["surveyId"];

                // Check if already submitted before database access ( during get request it will only work for )
                if (CheckAlreadySubmitted(claims, surveyId))
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "You have already submitted the survey"
                    });
                }

                // Find the survey
                var filter = Builders<SurveySchema>.Filter.Eq(s => s.Id, surveyId);
                var survey = await _surveys.Find(filter).FirstOrDefaultAsync();

                if (survey == null)
                {
                    return NotFound(new ApiResponse
                    {
                        Success = false,
                        Message = "Survey not found"
                    });
                }

                var validationResponse = ValidateSurvey(survey.Config.Status);
                if (validationResponse != null)
                {
                    return validationResponse;
                }

                // Process based on survey type
                if (survey.IsQuiz)
                {
                    return await HandleQuizRequest(claims, survey, filter);
                }
                else if (survey.Config.AccessControl.AccessType == AccessType.Restricted)
                {
                    return await HandleRestrictedSurveyRequest(claims, survey, filter);
                }
                else
                {
                    return HandleUnrestrictedSurveyRequest(survey);
                }
            }
            catch (SecurityTokenExpiredException)
            {
                _logger.LogWarning("Token has expired");
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "Link has expired"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request");
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Invalid token or server error"
                });
            }
        }

        // During get requrest rate limit can be used only for restricted surveys/ quiz
        private bool CheckAlreadySubmitted(IDictionary<string, string> claims, string surveyId)
        {
            // Only decrypt and check the cookie if it exists
            if (!Request.Cookies.TryGetValue("rateLimit", out string? rateLimitToken) || 
                string.IsNullOrEmpty(rateLimitToken))
            {
                return false;
            }

            try
            {
                var cookieClaims = _jwtHelper.DecryptToken(rateLimitToken);

                if (!cookieClaims.TryGetValue("surveyId", out string? cookieSurveyId) || 
                    string.IsNullOrEmpty(cookieSurveyId) || 
                    cookieSurveyId != surveyId)
                {
                    return false;
                }

                // Check if same user by ID
                bool isSameUser = claims.TryGetValue("userId", out string? userId) &&
                                !string.IsNullOrEmpty(userId) &&
                                cookieClaims.TryGetValue("userId", out string? cookieUserId) &&
                                cookieUserId == userId;

                return isSameUser ;
            }
            catch
            {
                // If there's an error decrypting the token, assume it's not a valid submission
                return false;
            }
        }

        private IActionResult? ValidateSurvey(SurveyStatus status)
        {
            return status switch
            {
                SurveyStatus.Completed => BadRequest(new ApiResponse { Success = false, Message = "Survey is completed" }),
                SurveyStatus.Scheduled => BadRequest(new ApiResponse { Success = false, Message = "Survey not yet started" }),
                SurveyStatus.Draft => BadRequest(new ApiResponse { Success = false, Message = "Survey has yet not been built" }),
                SurveyStatus.Paused => BadRequest(new ApiResponse { Success = false, Message = "Survey is currently not accepting responses" }),
                _ => null
            };
        }


        private async Task<IActionResult> HandleQuizRequest(IDictionary<string, string> claims, SurveySchema survey, FilterDefinition<SurveySchema> filter)
        {
            // Verify user ID is present for quiz
            if (!claims.TryGetValue("userId", out string? userId) || string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "User ID required for quiz access"
                });
            }
            // Check if user is allowed to take the quiz
            if (!IsUserAllowed(userId, survey.Config.AccessControl.AllowedUserIds))
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "User not authorized for this quiz"
                });
            }

            // Find existing attempt for this user
            var attemptEntry = survey.Config.AttemptedUsers
                .FirstOrDefault(u => u.UserId == userId);

            // Default quiz duration (in minutes)
            int quizDuration = survey.Config.QuizDuration ?? 60;

            // New attempt
            if (attemptEntry == null)
            {
                attemptEntry = new AttemptStatus
                {
                    UserId = userId,
                    StartedAt = DateTime.UtcNow,
                    Expired = false,
                    SubmittedAt = null
                };
                survey = await AddFirstAttempt(filter,attemptEntry);
            }
            else{

                // Check existing attempt status
                if (attemptEntry.SubmittedAt.HasValue)
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Quiz already submitted"
                    });
                }

                if (attemptEntry.Expired)
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Quiz attempt has expired"
                    });
                }
                // Check if attempt has now expired based on start time and duration
                TimeSpan elapsedTime = DateTime.UtcNow - attemptEntry.StartedAt;
                if (elapsedTime.TotalMinutes > quizDuration)
                {
                    // Mark as expired
                    var updateExpired = Builders<SurveySchema>.Update
                        .Set(s => s.Config.AttemptedUsers[-1].Expired, true);

                    await _surveys.UpdateOneAsync(
                        filter & Builders<SurveySchema>.Filter.ElemMatch(
                            s => s.Config.AttemptedUsers,
                            a => a.UserId == userId),
                        updateExpired);

                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Quiz time has expired"
                    });
                }
            }

            // Calculate remaining time in minutes
            TimeSpan remainingTimeSpan = attemptEntry.StartedAt.AddMinutes(quizDuration) - DateTime.UtcNow;
            int remainingMinutes = (int)Math.Ceiling(remainingTimeSpan.TotalMinutes);
            
            // Build token claims
            Dictionary<string, string> tokenClaims = new Dictionary<string, string>
            {
                ["surveyId"] = survey.Id,
                ["userId"] = userId,
                ["startTime"] = attemptEntry.StartedAt.ToString("o"), // ISO 8601 format
                ["quizDuration"] = quizDuration.ToString(),
                ["expiryTime"] = remainingMinutes.ToString()
            };

            // Generate token
            string token = _jwtHelper.GenerateToken(tokenClaims);

            // Set quiz expiry cookie
            Response.Cookies.Append("QuizToken", token, new CookieOptions
            {
                Expires = DateTime.UtcNow.AddMinutes(remainingMinutes),
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            });

            // Return sanitized quiz content
            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Quiz accessed successfully",
                Data = new
                {
                    quiz = _converter.ConvertToQuizDTO(survey),
                    remainingMinutes,
                    token // Include token in response for client-side storage if needed
                }
            });
        }
        private async Task<SurveySchema> AddFirstAttempt(FilterDefinition<SurveySchema> filter,AttemptStatus firstAttempt)
        {
            var update = Builders<SurveySchema>.Update
                .Push(s => s.Config.AttemptedUsers, firstAttempt);

            var options = new FindOneAndUpdateOptions<SurveySchema>
            {
                ReturnDocument = ReturnDocument.After
            };

            var updatedSurvey = await _surveys.FindOneAndUpdateAsync(filter, update, options);

            return updatedSurvey;
        }

        private async Task<IActionResult> HandleRestrictedSurveyRequest(IDictionary<string, string> claims, SurveySchema survey, FilterDefinition<SurveySchema> filter)
        {

            string userId;
            
            // Determine user identity based on access control configuration
            if (survey.Config.AccessControl.RequireUniqueLink)
            {
                // For unique link, user identity comes from token claims
                if (!claims.TryGetValue("userId", out string? tokenUserId) || string.IsNullOrEmpty(tokenUserId))
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Invalid URL: missing user identification"
                    });
                }
                userId = tokenUserId;
            }
            else
            {
                // For standard restricted access, user identity from login context
                userId = HttpContext.Items["UserId"] as string;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new ApiResponse
                    {
                        Success = false,
                        Message = "Authentication required to submit this survey"
                    });
                }
            }

            // Check if user is allowed to take the survey
            if (!IsUserAllowed(userId, survey.Config.AccessControl.AllowedUserIds))
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "User not authorized for this survey"
                });
            }

            // Check if user has already submitted (for single response limit)
            if (survey.Config.ResponseLimit.LimitType == ResponseLimitType.Single)
            {
                var attemptEntry = survey.Config.AttemptedUsers
                    .FirstOrDefault(u => u.UserId == userId && u.SubmittedAt.HasValue);

                if (attemptEntry != null)
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "You have already submitted a response to this survey"
                    });
                }
            }

            // Create tracking entry if not exists
            var existingAttempt = survey.Config.AttemptedUsers
                .FirstOrDefault(u => u.UserId == userId && !u.SubmittedAt.HasValue);

            if (existingAttempt == null)
            {
                var attempt = new AttemptStatus
                {
                    UserId = userId,
                    StartedAt = DateTime.UtcNow,
                    Expired = false
                };

                var update = Builders<SurveySchema>.Update
                    .Push(s => s.Config.AttemptedUsers, attempt);

                await _surveys.UpdateOneAsync(filter, update);
            }

            // Return survey content
            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Survey accessed successfully",
                Data = new
                {
                    survey = _converter.ConvertToSurveyDTO(survey)
                }
            });
        }

        private IActionResult HandleUnrestrictedSurveyRequest(SurveySchema survey)
        {
            bool emailRequired = false;
            // If cookie tracking is used for single responses, check cookie
            if (survey.Config.ResponseLimit.LimitType == ResponseLimitType.Single)
            {
                emailRequired = true;
            }

            // Return survey content
            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Survey accessed successfully",
                Data = new
                {
                    survey = _converter.ConvertToSurveyDTO(survey),
                    emailRequired
                }
            });
        }

        private bool IsUserAllowed(string userId, List<UserDetails> allowedUsers)
        {
            return allowedUsers.Any(u => u.UserId == userId);
        }

        private IDictionary<string, string> DecodeToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes("ThisIsAStaticLongEnoughSecretKeyForJWTGeneration123!");

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero // For stricter validation
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
            return principal.Claims.ToDictionary(c => c.Type, c => c.Value);
        }
    }
    
}