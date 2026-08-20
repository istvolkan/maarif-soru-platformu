using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§17 Human-in-the-loop durum makinesindeki soru kimliği. Değişken içerik
/// (orijinal/analiz/dönüşüm/düzenleme) <see cref="QuestionVersion"/> zincirinde tutulur.</summary>
public class Question : Entity
{
    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    public Guid? BookPageId { get; set; }
    public BookPage? BookPage { get; set; }

    public int? QuestionNo { get; set; }
    public QuestionStatus Status { get; set; } = QuestionStatus.Extracted;

    public Guid? MaarifStandardVersionId { get; set; }
    public MaarifStandardVersion? MaarifStandardVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<QuestionVersion> Versions { get; set; } = new List<QuestionVersion>();
}

/// <summary>Event-sourced versiyon geçmişi: original → analyzed → transformed → edited → final.
/// §17'deki GOLD DATASET doğrudan bu zincirden türetilir.</summary>
public class QuestionVersion : Entity
{
    public Guid QuestionId { get; set; }
    public Question? Question { get; set; }

    public int VersionNo { get; set; }
    public QuestionVersionStage Stage { get; set; }

    /// <summary>Bu versiyona ait ham AI/insan çıktısı (jsonb).</summary>
    public string PayloadJson { get; set; } = "{}";

    public string? CreatedBy { get; set; }

    public QuestionDna? Dna { get; set; }
    public ICollection<AlignmentScore> AlignmentScores { get; set; } = new List<AlignmentScore>();
    public ICollection<Distractor> Distractors { get; set; } = new List<Distractor>();
}
