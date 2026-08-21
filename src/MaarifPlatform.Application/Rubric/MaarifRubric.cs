namespace MaarifPlatform.Application.Rubric;

/// <summary>§E Maarif Uyum Rubriği — 15 kriter yerine (bazıları birleştirilerek) 14 anahtar
/// kriter, toplam ağırlık 100. "mathematical_accuracy" ve "learning_outcome_alignment"
/// critical gate'tir: bu ikisinden biri ihlal edilirse toplam puan ne olursa olsun
/// sonuç ManualReviewRequired'a döner (§8).</summary>
public static class MaarifRubric
{
    public sealed record CriterionDefinition(string Key, decimal Weight, bool IsCriticalGate);

    public static readonly IReadOnlyList<CriterionDefinition> Criteria =
    [
        new("mathematical_accuracy", 15, true),
        new("learning_outcome_alignment", 12, true),
        new("field_skill_alignment", 8, false),
        new("process_component", 7, false),
        new("reasoning", 8, false),
        new("problem_solving", 8, false),
        new("modeling", 6, false),
        new("context_quality", 8, false),
        new("representation_usage", 6, false),
        new("grade_level_fit", 6, false),
        new("language_clarity", 5, false),
        new("measurability", 5, false),
        new("distractor_quality", 4, false),
        new("cognitive_load_balance", 2, false)
    ];
}
