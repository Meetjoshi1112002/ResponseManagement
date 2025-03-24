using ReponseManagement.Models.DTOs;
using ReponseManagement.Models.POCOs;

namespace ReponseManagement.Services
{
    public class DTOConverter
    {
        /// <summary>
        /// Converts a SurveySchema to a SurveyDTO, stripping sensitive data like answers
        /// </summary>
        /// <param name="survey">The survey POCO to convert</param>
        /// <param name="includeScores">Whether to include score information (for quizzes)</param>
        /// <returns>A safely shareable SurveyDTO</returns>
        public SurveyDTO ConvertToSurveyDTO(SurveySchema survey, bool includeScores = false)
        {
            if (survey == null)
                throw new ArgumentNullException(nameof(survey));

            var dto = new SurveyDTO
            {
                Id = survey.Id,
                Name = survey.Name,
                Description = survey.Description,
                IsQuiz = survey.IsQuiz
            };

            // Handle sections if present
            if (survey.Sections?.Count > 0)
            {
                dto.Sections = survey.Sections
                    .OrderBy(s => s.SectionIndex)
                    .Select(s => ConvertToSectionDTO(s, includeScores && survey.IsQuiz))
                    .ToList();
            }

            // Handle direct questions if present
            if (survey.Questions?.Count > 0)
            {
                dto.Questions = survey.Questions
                    .OrderBy(q => q.QuestionIndex)
                    .Select(q => ConvertToQuestionDTO(q, includeScores && survey.IsQuiz))
                    .ToList();
            }

            return dto;
        }

        /// <summary>
        /// Specialized converter for quiz DTOs (includes score information)
        /// </summary>
        /// <param name="survey">The quiz POCO to convert</param>
        /// <returns>A quiz DTO with score information</returns>
        public SurveyDTO ConvertToQuizDTO(SurveySchema survey)
        {
            if (survey == null)
                throw new ArgumentNullException(nameof(survey));

            if (!survey.IsQuiz)
                throw new ArgumentException("The provided survey is not marked as a quiz", nameof(survey));

            return ConvertToSurveyDTO(survey, includeScores: true);
        }

        /// <summary>
        /// Converts a survey section to a section DTO
        /// </summary>
        private SectionDTO ConvertToSectionDTO(Section section, bool includeScores)
        {
            if (section == null)
                return null;

            return new SectionDTO
            {
                Id = section.Id,
                Title = section.Title,
                SectionIndex = section.SectionIndex,
                Questions = section.Questions?
                    .OrderBy(q => q.QuestionIndex)
                    .Select(q => ConvertToQuestionDTO(q, includeScores))
                    .ToList() ?? []
            };
        }

        /// <summary>
        /// Converts a question to a question DTO, stripping sensitive answer data
        /// </summary>
        private QuestionDTO ConvertToQuestionDTO(Question question, bool includeScores)
        {
            if (question == null)
                return null;

            var dto = new QuestionDTO
            {
                Id = question.Id,
                Text = question.Text,
                Type = question.Type,
                QuestionIndex = question.QuestionIndex,
                Score = includeScores ? question.Score : null
            };

            // Only include range properties for LinearScale questions
            if (question.Type == QuestionType.LinearScale)
            {
                dto.MinRange = question.MinRange;
                dto.MaxRange = question.MaxRange;
            }

            // Handle options for option-based questions
            if (question.Options?.Count > 0)
            {
                dto.Options = question.Options.Select(o => new OptionDTO
                {
                    Id = o.Id,
                    Text = o.Text
                    // Deliberately omit IsCorrect field
                }).ToList();
            }
            else
            {
                dto.Options = [];
            }

            // Handle DropDownItems if they exist
            // Note: DropDownItems isn't in the Question class definition you provided
            // Uncomment if it does exist in your actual model
            /*
            if (question.DropDownItems?.Count > 0)
            {
                dto.DropDownItems = new List<string>(question.DropDownItems);
            }
            else
            {
                dto.DropDownItems = new List<string>();
            }
            */

            return dto;
        }
    }
}