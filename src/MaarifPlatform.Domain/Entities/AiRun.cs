using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§9/§M Maliyet izleme — her AI çağrısının token/maliyet/gecikme kaydı.
/// Cost Dashboard doğrudan bu tablo üzerinden agregasyon yapar.</summary>
public class AiRun : Entity
{
    public Guid? QuestionId { get; set; }
    public Question? Question { get; set; }

    public PipelineStage Stage { get; set; }
    public ModelTier ModelTier { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? PromptVersion { get; set; }

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public int LatencyMs { get; set; }
}

/// <summary>§I Prompt Mimarisi — versiyonlu, denetlenebilir prompt şablonları.</summary>
public class PromptTemplate : Entity
{
    public string Name { get; set; } = string.Empty;
    public PipelineStage Stage { get; set; }
    public int Version { get; set; } = 1;
    public string Content { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
}

/// <summary>§N Güvenlik — değişmez (append-only) karar kaydı. Hiçbir alan sonradan güncellenmez.</summary>
public class AuditLogEntry : Entity
{
    public ActorType ActorType { get; set; }
    public string? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
