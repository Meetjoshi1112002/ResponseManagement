using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using ReponseManagement.Data;
using ReponseManagement.Models.POCOs;
using ReponseManagement.Models.DTOs;
using ReponseManagement.Services;
using Microsoft.IdentityModel.Tokens;
using ReponseManagement.Models;

namespace ReponseManagement.Controllers
{
    [Route("api/submit/form")]
    [ApiController]
    public class SubmitController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMongoCollection<SurveySchema> _surveys;
        private readonly IMongoCollection<SurveyResponse> _responses;
        private readonly ILogger<SubmitController> _logger;
        private readonly JWTHelper _jwtHelper;

        public SubmitController(AppDbContext context,
                                ILogger<SubmitController> logger,
                                JWTHelper jwtHelper)
        {
            _context = context;
            _surveys = _context.GetCollection<SurveySchema>("Survey");
            _responses = _context.GetCollection<SurveyResponse>("SurveyResponses");
            _logger = logger;
            _jwtHelper = jwtHelper;
        }

        [HttpPost("{token}")] // This token is very important
        public async Task<IActionResult> SubmitForm(string token, [FromBody] ResponseSubmissionDTO dto)
        {
            try
            {
                // Only decrypt token if it's not null or empty
                if (string.IsNullOrEmpty(token))
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Token is required"
                    });
                }

                IDictionary<string, string> claims = _jwtHelper.DecryptToken(token);

                if (!claims.TryGetValue("surveyId", out string? surveyId) || string.IsNullOrEmpty(surveyId))
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Invalid link"
                    });
                }

                // Check if already submitted before database access
                if (CheckAlreadySubmitted(claims, surveyId, dto))
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "You have already submitted the survey"
                    });
                }

                // Use projection to get only necessary fields for initial validation
                //var projection = Builders<SurveySchema>.Projection
                //    .Include(s=>s.IsQuiz)
                //    .Include(s => s.Config.Status)
                //    .Include(s => s.Config.AccessControl)
                //    .Include(s => s.Config.ResponseLimit);

                // Find the survey with projection
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

                var validationResponse = ValidateSurveyStatus(survey.Config.Status);
                if (validationResponse != null)
                {
                    return validationResponse;
                }

                // Now get the full survey document since we need it for processing
                survey = await _surveys.Find(filter).FirstOrDefaultAsync();

                if(survey.IsQuiz){
                    if(ValidateSubmission(claims,surveyId)){

                        return await HandleQuizSubmission(survey,dto,claims);

                    }else{
                        return BadRequest(new ApiResponse{
                            Message = "your are now not elligible to give the quiz",
                            Success = false
                        });
                    }
                }

                return survey.Config.AccessControl.AccessType switch
                {
                    AccessType.Unrestricted => await HandleUnrestrictedSurvey(survey, dto),
                    AccessType.Restricted => await HandleRestrictedSurvey(survey, dto, claims),
                    _ => BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Unknown access type configuration"
                    })
                };
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
                return StatusCode(500, new ApiResponse
                {
                    Success = false,
                    Message = "An error occurred while processing your request"
                });
            }
        }

        private bool ValidateSubmission(IDictionary<string, string> claims, string surveyId)
        {
            // if token not present don't accept the response!
            if (!Request.Cookies.TryGetValue("QuizToken", out string? quizToken) || 
                string.IsNullOrEmpty(quizToken))
            {
                return false;
            }

            try
            {
                // get claims from the token
                var cookieClaims = _jwtHelper.DecryptToken(quizToken);
                if (!cookieClaims.TryGetValue("surveyId", out string? cookieSurveyId) || 
                    string.IsNullOrEmpty(cookieSurveyId) || 
                    cookieSurveyId != surveyId)
                {
                    return false;
                }

                bool isSameUser = claims.TryGetValue("userId", out string? userId) &&
                                !string.IsNullOrEmpty(userId) &&
                                cookieClaims.TryGetValue("userId", out string? cookieUserId) &&
                                cookieUserId == userId;
                
                if(isSameUser)
                {
                    // Check if quiz time is expired
                    if (cookieClaims.TryGetValue("startTime", out string? startTimeStr) &&
                        cookieClaims.TryGetValue("quizDuration", out string? durationStr) &&
                        DateTime.TryParse(startTimeStr, out DateTime startTime) &&
                        int.TryParse(durationStr, out int quizDuration))
                    {
                        // Calculate if quiz is still valid based on start time and duration
                        TimeSpan elapsedTime = DateTime.UtcNow - startTime;
                        return elapsedTime.TotalMinutes <= quizDuration;
                    }
                }
                
                // If user validation fails or we can't determine validity, reject submission
                return false;
            }
            catch
            {
                // If there's an error decrypting the token, assume it's not a valid submission
                return false;
            }
        }

        private bool CheckAlreadySubmitted(IDictionary<string, string> claims, string surveyId, ResponseSubmissionDTO dto)
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

                // Check if same user by email
                bool isSameEmail = !string.IsNullOrEmpty(dto.Email) &&
                                cookieClaims.TryGetValue("email", out string? cookieEmail) &&
                                cookieEmail == dto.Email;

                return isSameUser || isSameEmail;
            }
            catch
            {
                // If there's an error decrypting the token, assume it's not a valid submission
                return false;
            }
        }

        private IActionResult? ValidateSurveyStatus(SurveyStatus status)
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

        private async Task<IActionResult> HandleQuizSubmission(SurveySchema survey, ResponseSubmissionDTO dto, IDictionary<string, string> claims)
        {
            try
            {
                // Extract user identity from claims
                if (!claims.TryGetValue("userId", out string? userId) || string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Invalid quiz submission: missing user identification"
                    });
                }

                var attemptEntry = survey.Config.AttemptedUsers.FirstOrDefault(u => u.UserId == userId);
                if(attemptEntry == null){
                    return Unauthorized(new ApiResponse{
                        Message = "attmept not found",
                        Success = false
                    });
                }

                // Process answers
                var answers = ProcessAnswers(dto.Answers);
                
                // Calculate quiz score
                int totalScore = 0;
                int maxPossibleScore = 0;
                
                // Get all questions from the survey (whether in sections or not)
                List<Question> allQuestions = [];
                
                if (survey.Sections != null && survey.Sections.Count > 0)
                {
                    // Collect questions from all sections
                    foreach (var section in survey.Sections)
                    {
                        allQuestions.AddRange(section.Questions);
                    }
                }
                else if (survey.Questions != null)
                {
                    // Get questions directly from survey
                    allQuestions.AddRange(survey.Questions);
                }
                
                // Calculate scores for each answer
                foreach (var answer in answers)
                {
                    var question = allQuestions.FirstOrDefault(q => q.Id == answer.QuestionId);
                    
                    if (question == null || !question.Score.HasValue)
                        continue;
                        
                    maxPossibleScore += question.Score.Value;
                    
                    bool isCorrect = false;
                    
                    switch (question.Type)
                    {
                        case QuestionType.SingleSelectMCQ:
                            // Check if selected option is correct
                            var correctOption = question.Options?.FirstOrDefault(o => o.IsCorrect == true);
                            isCorrect = correctOption != null && correctOption.Id == answer.SelectedOptionId;
                            break;
                            
                        case QuestionType.CheckBoxes:
                            // For checkboxes, all correct options must be selected and no incorrect ones
                            if (question.Options != null && answer.SelectedOptionIds != null)
                            {
                                var correctOptionIds = question.Options.Where(o => o.IsCorrect == true).Select(o => o.Id).ToList();
                                isCorrect = correctOptionIds.Count == answer.SelectedOptionIds.Count &&
                                        correctOptionIds.All(id => answer.SelectedOptionIds.Contains(id));
                            }
                            break;
                            
                        case QuestionType.FillInTheBlank:
                            // Case-insensitive comparison for text answers
                            isCorrect = !string.IsNullOrEmpty(question.CorrectAnswer) && 
                                    !string.IsNullOrEmpty(answer.TextValue) &&
                                    answer.TextValue.Trim().Equals(question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
                            break;
                            
                        // Additional question types can be handled here if needed
                    }
                    
                    if (isCorrect)
                    {
                        totalScore += question.Score.Value;
                    }
                }
                
                // Create and save the response document
                var response = new SurveyResponse
                {
                    SurveyId = survey.Id,
                    UserId = userId,
                    Answers = answers,
                    SubmittedAt = DateTime.UtcNow
                };
                
                await _responses.InsertOneAsync(response);
                
                // Update user's attempt status in the survey
                
                
                // Update the existing attempt
                var arrayFilter = Builders<SurveySchema>.Filter.Eq(s => s.Id, survey.Id) &
                                Builders<SurveySchema>.Filter.ElemMatch(
                                    s => s.Config.AttemptedUsers,
                                    a => a.UserId == userId
                                );

                await _surveys.UpdateOneAsync(
                    arrayFilter,
                    Builders<SurveySchema>.Update.Set(
                        "Config.AttemptedUsers.$.SubmittedAt", // ✅ Use `$` instead of `-1`
                        DateTime.UtcNow
                    )
                );



                // Set rate limit cookie to prevent resubmission
                SetRateLimitCookie(survey.Id, userId: userId);
                
                // Remove the quiz timer cookie
                Response.Cookies.Delete("QuizToken");
                
                return Ok(new ApiResponse
                {
                    Success = true,
                    Message = "Quiz submitted successfully",
                    Data = new { 
                        responseId = response.Id,
                        score = totalScore,
                        totalPossibleScore = maxPossibleScore,
                        percentage = maxPossibleScore > 0 ? Math.Round((double)totalScore / maxPossibleScore * 100, 2) : 0
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing quiz submission");
                
                return StatusCode(500, new ApiResponse
                {
                    Success = false,
                    Message = "An error occurred while processing your quiz submission"
                });
            }
        }

        private async Task<IActionResult> HandleRestrictedSurvey(SurveySchema survey, ResponseSubmissionDTO dto, IDictionary<string, string> claims)
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

            // Check if user has already submitted (for single response limit)
            if (survey.Config.ResponseLimit.LimitType == ResponseLimitType.Single &&
                survey.Config.AttemptedUsers?.Any(u => u.UserId == userId && u.SubmittedAt.HasValue) == true)
            {
                // Set rate limit cookie to prevent further attempts
                SetRateLimitCookie(survey.Id, userId: userId);
                
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "You have already submitted this survey"
                });
            }

            try
            {
                // Process the answers
                var answers = ProcessAnswers(dto.Answers);

                // Create response document
                var response = new SurveyResponse
                {
                    SurveyId = survey.Id,
                    UserId = userId,
                    Answers = answers,
                    SubmittedAt = DateTime.UtcNow
                };

                // Save the response
                await _responses.InsertOneAsync(response);

                // For single response limit, update the attempted users
                if (survey.Config.ResponseLimit.LimitType == ResponseLimitType.Single)
                {
                    // Check if user already has an attempt entry
                    var attemptEntry = survey.Config.AttemptedUsers?.FirstOrDefault(u => u.UserId == userId);

                    if (attemptEntry == null)
                    {
                        // Create new attempt entry
                        var newAttemptEntry = new AttemptStatus
                        {
                            UserId = userId,
                            StartedAt = DateTime.UtcNow,
                            SubmittedAt = DateTime.UtcNow,
                            Expired = false
                        };

                        // Initialize the AttemptedUsers list if null
                        if (survey.Config.AttemptedUsers == null)
                        {
                            await _surveys.UpdateOneAsync(
                                s => s.Id == survey.Id,
                                Builders<SurveySchema>.Update.Set(s => s.Config.AttemptedUsers, new List<AttemptStatus>(){newAttemptEntry})
                            );
                        }else{
                            // Add the new attempt
                            await _surveys.UpdateOneAsync(
                                s => s.Id == survey.Id,
                                Builders<SurveySchema>.Update.Push(s => s.Config.AttemptedUsers, newAttemptEntry)
                            );
                        }

                    }
                    else
                    {
                        // Update the existing attempt
                        var arrayFilter = Builders<SurveySchema>.Filter.Eq(s => s.Id, survey.Id) &
                                        Builders<SurveySchema>.Filter.ElemMatch(
                                            s => s.Config.AttemptedUsers,
                                            a => a.UserId == userId
                                        );

                        await _surveys.UpdateOneAsync(
                            arrayFilter,
                            Builders<SurveySchema>.Update.Set(
                                s => s.Config.AttemptedUsers[-1].SubmittedAt,
                                DateTime.UtcNow
                            )
                        );
                    }

                    // Set rate limit cookie
                    SetRateLimitCookie(survey.Id, userId: userId);
                }

                return Ok(new ApiResponse
                {
                    Success = true,
                    Message = "Response submitted successfully",
                    Data = new { responseId = response.Id }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing restricted survey submission");
                
                return StatusCode(500, new ApiResponse
                {
                    Success = false,
                    Message = "An error occurred while processing your submission"
                });
            }
        }

        private async Task<IActionResult> HandleUnrestrictedSurvey(SurveySchema survey, ResponseSubmissionDTO dto)
        {
            // Validate DTO
            if (string.IsNullOrEmpty(dto.Email) || dto.Answers == null || !dto.Answers.Any())
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Email and answers are required"
                });
            }

            // Handle single response limit with email tracking
            if (survey.Config.ResponseLimit.LimitType == ResponseLimitType.Single &&
                survey.Config.SubmittedEmails?.Contains(dto.Email) == true)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "This email has already submitted a response"
                });
            }

            try
            {
                // Process the answers
                var answers = ProcessAnswers(dto.Answers);

                // Create response document
                var response = new SurveyResponse
                {
                    SurveyId = survey.Id,
                    Email = dto.Email,
                    Answers = answers,
                    SubmittedAt = DateTime.UtcNow
                };

                // Save the response
                await _responses.InsertOneAsync(response);

                // For single response limit, track the email
                if (survey.Config.ResponseLimit.LimitType == ResponseLimitType.Single)
                {   
                    // Initialize the SubmittedEmails collection if null
                    if (survey.Config.SubmittedEmails == null)
                    {
                        await _surveys.UpdateOneAsync(
                            s => s.Id == survey.Id,
                            Builders<SurveySchema>.Update.Set(s => s.Config.SubmittedEmails, new List<string>())
                        );
                    }

                    // Add email to submitted list
                    await _surveys.UpdateOneAsync(
                        s => s.Id == survey.Id,
                        Builders<SurveySchema>.Update.AddToSet(s => s.Config.SubmittedEmails, dto.Email)
                    );

                    // Set rate limit cookie
                    SetRateLimitCookie(survey.Id, email: dto.Email);
                }

                return Ok(new ApiResponse
                {
                    Success = true,
                    Message = "Response submitted successfully",
                    Data = new { responseId = response.Id }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing unrestricted survey submission");
                
                return StatusCode(500, new ApiResponse
                {
                    Success = false,
                    Message = "An error occurred while processing your submission"
                });
            }
        }

        private List<Answer> ProcessAnswers(List<AnswerDTO> answerDtos)
        {
            var answers = new List<Answer>();

            foreach (var answerDto in answerDtos)
            {
                var answer = new Answer
                {
                    QuestionId = answerDto.QuestionId
                };

                // Set the appropriate value based on question type
                switch (answerDto.QuestionType)
                {
                    case QuestionType.TextAnswer:
                    case QuestionType.FillInTheBlank:
                        answer.TextValue = answerDto.TextValue ?? "No response";
                        break;
                    case QuestionType.SingleSelectMCQ:
                        answer.SelectedOptionId = answerDto.SelectedOptionId ?? "No selection";
                        break;
                    case QuestionType.CheckBoxes:
                        answer.SelectedOptionIds = answerDto.SelectedOptionIds ?? new List<string>();
                        break;
                    case QuestionType.LinearScale:
                        answer.ScaleValue = answerDto.ScaleValue;
                        break;
                    case QuestionType.Date:
                        answer.DateValue = answerDto.DateValue;
                        break;
                    case QuestionType.Time:
                        answer.TimeValue = answerDto.TimeValue ?? "No time specified";
                        break;
                    default:
                        // Handle unrecognized question types
                        answer.TextValue = "Unsupported question type";
                        break;
                }

                answers.Add(answer);
            }

            return answers;
        }

        private void SetRateLimitCookie(string surveyId, string userId = null, string email = null)
        {
            var cookieClaims = new Dictionary<string, string>
            {
                { "surveyId", surveyId },
                { "issuedAt", DateTime.UtcNow.ToString("o") },
                { "expiryTime", "43200" } // 30 days in minutes
            };

            // Add either userId or email to claims based on which is provided
            if (!string.IsNullOrEmpty(userId))
            {
                cookieClaims.Add("userId", userId);
            }
            else if (!string.IsNullOrEmpty(email))
            {
                cookieClaims.Add("email", email);
            }

            string rateLimitToken = _jwtHelper.GenerateToken(cookieClaims);
            
            Response.Cookies.Append("rateLimit", rateLimitToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow.AddDays(30)
            });
        }
    }
}