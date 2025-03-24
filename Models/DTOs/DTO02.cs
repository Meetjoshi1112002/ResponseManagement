using System.ComponentModel.DataAnnotations;
using ReponseManagement.Models.POCOs;

namespace ReponseManagement.Models.DTOs
{

    public class ResponseSubmissionDTO{

        public string? Email {get;set;} // for single response unresrticted survey

        public List<AnswerDTO> Answers {get;set;}
    }

    public class AnswerDTO{
        [Required]
        public string QuestionId { get; set; }
        public QuestionType QuestionType {get;set;}
        // Different response types
        public string? TextValue { get; set; } // For short answer type/fill in the blank
        public string? SelectedOptionId { get; set; } //for single select MCQ
        public List<string>? SelectedOptionIds { get; set; } // for check box type
        public int? ScaleValue { get; set; } // for linear scale
        public DateTime? DateValue { get; set; } // for date select
        public string? TimeValue { get; set; } // for time selection
    }
}
