using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ReponseManagement.Models.POCOs
{
    public class SurveySchema
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("name")]
        public string Name { get; set; }  // Survey/Quiz Template Name

        [BsonElement("description")]
        public string? Description { get; set; }  // Optional description

        [BsonElement("isQuiz")]
        public bool IsQuiz { get; set; }  // True if it's a Quiz, False if it's a Survey

        [BsonElement("sections")]
        public List<Section>? Sections { get; set; }  // Sections (for multi-page surveys/quizzes)

        [BsonElement("questions")]
        public List<Question>? Questions { get; set; }  // Only for single-page surveys/quizzes

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;  // Default timestamp

        [BsonElement("updatedAt")]
        public DateTime? UpdatedAt { get; set; }

        [BsonElement("configurations")]
        public SurveyConfiguration Config { get; set; } = new SurveyConfiguration();
    }

    public class Section
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }


        [BsonElement("sectionIndex")]
        public int SectionIndex { get; set; }  // Section order

        [BsonElement("title")]
        public string Title { get; set; }  // Section Title

        [BsonElement("questions")]
        public List<Question> Questions { get; set; }  // Embedded list of questions
    }

    public class Question
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }


        [BsonElement("questionIndex")]
        public int QuestionIndex { get; set; }  // Determines order of the question

        [BsonElement("text")]
        public string Text { get; set; }  // Question text

        [BsonElement("type")]
        public QuestionType Type { get; set; }  // Enum for question type

        [BsonElement("options")]
        [BsonIgnoreIfNull]
        public List<Option>? Options { get; set; }  // For MCQ & Checkboxes

        [BsonElement("minRange")]
        [BsonIgnoreIfNull]
        public int? MinRange { get; set; }  // For Linear Scale

        [BsonElement("maxRange")]
        [BsonIgnoreIfNull]
        public int? MaxRange { get; set; }  // For Linear Scale

        [BsonElement("correctAnswer")]
        [BsonIgnoreIfNull]  // This ensures it's only stored in Quiz Questions
        public string? CorrectAnswer { get; set; }  // For Fill-in-the-Blank & MCQ Quiz

        [BsonElement("score")]
        [BsonIgnoreIfNull]  // This ensures it's only stored in Quiz Questions
        public int? Score { get; set; }  // Score value for quiz-type questions
    }

    public enum QuestionType
    {
        SingleSelectMCQ,  // Single choice MCQ
        CheckBoxes,        // Multiple choice MCQ
        TextAnswer,        // Open-ended text response
        LinearScale,       // Range-based selection (e.g., 1-5)
        FillInTheBlank,    // Answer-based quiz question
        Date,              // Date selection
        Time               // Time selection
    }

    public class Option
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }


        [BsonElement("text")]
        public string Text { get; set; }  // Option text

        [BsonElement("isCorrect")]
        [BsonIgnoreIfNull]  // This ensures it's only stored in Quiz Questions
        public bool? IsCorrect { get; set; }  // True if correct in MCQ Quiz
    }
}
