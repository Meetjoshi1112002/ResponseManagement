using ReponseManagement.Models.POCOs;

namespace ReponseManagement.Models.DTOs
{
    public class SurveyDTO
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsQuiz { get; set; }
        public List<SectionDTO> Sections { get; set; }
        public List<QuestionDTO> Questions { get; set; }
    }

    public class SectionDTO
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public int SectionIndex { get; set; }
        public List<QuestionDTO> Questions { get; set; }
    }

    public class QuestionDTO
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public QuestionType Type { get; set; }
        public int QuestionIndex { get; set; }
        public List<OptionDTO> Options { get; set; }
        public List<string> DropDownItems { get; set; }
        public int? MinRange { get; set; }
        public int? MaxRange { get; set; }
        public int? Score { get; set; } // Only show score for quizzes
        // Deliberately omit CorrectAnswer field
    }

    public class OptionDTO
    {
        public string Id { get; set; }
        public string Text { get; set; }
        // Deliberately omit IsCorrect field
    }
}
