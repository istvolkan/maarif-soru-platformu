namespace MaarifPlatform.Application.Providers;

/// <summary>Her ILLMProvider çağrısıyla birlikte dönen kullanım verisi.
/// Cost Ledger (§9/§M) doğrudan bunu AiRun kaydına yazar.</summary>
public sealed record AiUsage(
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int LatencyMs);

/// <summary>RAG'den gelen ve prompt'a enjekte edilen, atıf zorunlu bağlam parçası (§G).</summary>
public sealed record GroundingReference(
    Guid ReferenceDocumentId,
    int? Page,
    string? SectionPath,
    string ChunkText);
