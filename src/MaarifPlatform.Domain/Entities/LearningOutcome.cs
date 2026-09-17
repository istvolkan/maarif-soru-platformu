using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

/// <summary>MEB kazanım kaydı. RAG kaynağına atıfla gelir — §elestiri madde 1'in
/// mitigasyonu: skorlama bu tabloya bağlanmadan sentetik kalmaz.</summary>
public class LearningOutcome : Entity
{
    public string Code { get; set; } = string.Empty;
    public int Grade { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid? ThemeId { get; set; }
    public Theme? Theme { get; set; }

    public Guid? SourceDocumentId { get; set; }
    public ReferenceDocument? SourceDocument { get; set; }

    public Guid MaarifStandardVersionId { get; set; }
    public MaarifStandardVersion? MaarifStandardVersion { get; set; }

    /// <summary>Yalnızca Approved kayıtlar cascading dropdown'larda görünür ve üretimde
    /// kullanılabilir (bkz. CurriculumExtractionService, Admin/CurriculumReview.razor).</summary>
    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Draft;

    public ICollection<ContentFramework> ContentFrameworks { get; set; } = new List<ContentFramework>();
    public ICollection<ProcessComponent> ProcessComponents { get; set; } = new List<ProcessComponent>();
}
