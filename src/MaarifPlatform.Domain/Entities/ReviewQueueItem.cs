namespace MaarifPlatform.Domain.Entities;

/// <summary>§hitl Editör kuyruğu — önceliklendirme, atama ve karar durumu.</summary>
public class ReviewQueueItem : Entity
{
    public Guid QuestionId { get; set; }
    public Question? Question { get; set; }

    public int Priority { get; set; }

    public Guid? AssignedToUserId { get; set; }
    public AppUser? AssignedToUser { get; set; }

    public string Status { get; set; } = "PENDING";
    public string? ReasonFlagsJson { get; set; }
}
