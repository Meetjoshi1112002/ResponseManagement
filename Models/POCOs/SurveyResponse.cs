using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ReponseManagement.Models.POCOs
{
    public class SurveyResponse
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("surveyId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonRequired]
        public string SurveyId { get; set; }  // Reference to the survey/quiz

        [BsonElement("userId")]
        [BsonIgnoreIfNull]
        public string UserId { get; set; }  // For authenticated/restricted responses

        [BsonElement("email")]
        [BsonIgnoreIfNull]
        public string Email { get; set; }  // For email-tracked responses for unregistered users

        [BsonElement("answers")]
        public List<Answer> Answers { get; set; }  // List of all answers

        [BsonElement("SubmittedTime")]
        public DateTime SubmittedAt {get;set;} // track time of submission  

        public SurveyResponse(){
            Answers = []; // inialize empty
        }
    }

    public class Answer
    {
        [BsonElement("questionId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string QuestionId { get; set; }  // Reference to the question

        // Different response value types based on question type
        [BsonElement("textValue")]
        [BsonIgnoreIfNull]
        public string TextValue { get; set; }  // For TextAnswer, FillInTheBlank

        [BsonElement("selectedOptionIds")]
        [BsonIgnoreIfNull]
        public List<string> SelectedOptionIds { get; set; }  // For MCQ, CheckBoxes

        [BsonElement("selectionOptionId")]
        [BsonIgnoreIfNull]
        public string SelectedOptionId { get; set; }  // for single select MCQ

        [BsonElement("scaleValue")]
        [BsonIgnoreIfNull]
        public int? ScaleValue { get; set; }  // For LinearScale

        [BsonElement("dateValue")]
        [BsonIgnoreIfNull]
        public DateTime? DateValue { get; set; }  // For Date

        [BsonElement("timeValue")]
        [BsonIgnoreIfNull]
        public string TimeValue { get; set; }  // For Time
    }
}