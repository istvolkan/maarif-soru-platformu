namespace MaarifPlatform.Application.Rubric;

/// <summary>§E/§42 Maarif Uyum Rubriği — toplam ağırlık her zaman 100. "mathematical_accuracy"
/// ve "learning_outcome_alignment" critical gate'tir: bu ikisinden biri ihlal edilirse toplam
/// puan ne olursa olsun sonuç ManualReviewRequired'a döner (§8).
///
/// 2026-10 (Question Intelligence Engine Faz 1) güncellemesi: kriterler §42'nin 15 Maarif Uyum
/// Boyutu'yla (curriculum_alignment...age_appropriateness) BİREBİR eşlenecek şekilde yeniden
/// düzenlendi — process_component→process_component_alignment, field_skill_alignment→
/// domain_skill_alignment, reasoning→reasoning_depth, context_quality→real_world_context,
/// grade_level_fit→age_appropriateness yeniden adlandırıldı; conceptual_skill_alignment/
/// curriculum_alignment/interpretation_requirement/justification_requirement/data_literacy/
/// visual_literacy/multi_step_reasoning YENİ eklendi. §42 listesinde olmayan ama var olan
/// kaliteli sinyaller (modeling/language_clarity/measurability/distractor_quality/
/// cognitive_load_balance) SİLİNMEDİ, küçük tamamlayıcı ağırlıklarla korundu — §42 "bu
/// boyutları puanla" diyor, "başka hiçbir şeyi puanlama" demiyor.
///
/// DİKKAT: Bu değişiklik MaarifAlignmentScore'un ağırlıklarını değiştirir — bu commit'ten
/// önce/sonra üretilen puanlar birebir kıyaslanamaz (bilinçli, onaylı karar).</summary>
public static class MaarifRubric
{
    public sealed record CriterionDefinition(string Key, decimal Weight, bool IsCriticalGate);

    public static readonly IReadOnlyList<CriterionDefinition> Criteria =
    [
        // Critical gates — ihlalde skor ne olursa olsun ManualReviewRequired.
        new("mathematical_accuracy", 15, true),
        new("learning_outcome_alignment", 12, true),

        // §42'nin 15 Maarif Uyum Boyutu (learning_outcome_alignment yukarıda, critical gate
        // olarak zaten sayıldı) — ağırlıkların %63'ü bu 14 kritere ayrılır.
        new("curriculum_alignment", 6, false),
        new("process_component_alignment", 6, false),
        new("domain_skill_alignment", 5, false),
        new("conceptual_skill_alignment", 4, false),
        new("reasoning_depth", 6, false),
        new("problem_solving", 6, false),
        new("representation_usage", 5, false),
        new("real_world_context", 5, false),
        new("interpretation_requirement", 3, false),
        new("justification_requirement", 3, false),
        new("data_literacy", 3, false),
        new("visual_literacy", 3, false),
        new("multi_step_reasoning", 4, false),
        new("age_appropriateness", 4, false),

        // §42 dışı, önceden var olan tamamlayıcı kalite sinyalleri — küçük ama sıfır olmayan
        // ağırlıkla korunur (bkz. sınıf doc'u).
        new("modeling", 3, false),
        new("language_clarity", 2, false),
        new("measurability", 2, false),
        new("distractor_quality", 2, false),
        new("cognitive_load_balance", 1, false)
    ];
}
