using System.ComponentModel.DataAnnotations;

namespace MaarifPlatform.Api.Dtos;

public class GenerateQuestionApiRequest
{
    [Required] public int Grade { get; set; }
    [Required] public string Subject { get; set; } = string.Empty;
    [Required] public string Theme { get; set; } = string.Empty;
    [Required] public string LearningOutcomeCode { get; set; } = string.Empty;
    [Required] public string Difficulty { get; set; } = string.Empty;
    [Required] public string QuestionType { get; set; } = string.Empty;
    [Required] public string Context { get; set; } = string.Empty;
    [Required] public string ReasoningType { get; set; } = string.Empty;
}

public record GenerateQuestionResponse(Guid QuestionId, string Provider, string Model, decimal CostUsd);
