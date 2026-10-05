namespace MaarifPlatform.Application.Generation;

/// <summary>§57 PatternScore — Question Intelligence Engine Faz 6/7. Tam formül: CurriculumAlignment
/// × QualityScore × MaarifAffinity × TopicCompatibility × DifficultyCompatibility × DiversityFactor
/// × InstitutionalPreference. MaarifAffinity (Faz 3), admin'in MaarifWeight ayarı (Faz 4) ve artık
/// InstitutionalPreference (Faz 7 — bkz. Score'un institutionalApprovalRate parametresi) GERÇEK
/// girdidir; diğer terimler BİLİNÇLİ OLARAK nötr sabit (1.0) bırakılmıştır:
///   - CurriculumAlignment/TopicCompatibility/DifficultyCompatibility: QuestionArchetype henüz
///     bunları ayrı saklamıyor (yalnızca Subject+GradeRange var, çağıran taraf zaten bunlarla
///     ön-filtreleme yapıyor — bkz. GenerationOrchestrationService).
///   - QualityScore: QuestionArchetype.QualityScore bilinçli olarak Faz 3'te doldurulmadı
///     (Transform/Judge aşamasından gelir, Analysis aşamasında henüz yok).
/// "Varmış gibi" sahte bir sinyal uydurmak yerine dürüstçe nötr bırakmak tercih edildi —
/// DiversityFactor ayrıca bu skora DEĞİL, blueprint'in round-robin ATAMASINA (GenerationBlueprintBuilder)
/// dahildir, çünkü çeşitlilik bireysel bir skor değil, N slot arasındaki bir DAĞITIM kararıdır.</summary>
public static class PatternScorer
{
    /// <summary><paramref name="maarifAffinity"/> 0-100 arası (QuestionArchetype.MaarifAffinity,
    /// null ise nötr 50 varsayılır). <paramref name="maarifWeightPercent"/> admin Ayarlar'daki
    /// Generation:MaarifWeight (0-100). <paramref name="institutionalApprovalRate"/> §56 Faz 7 —
    /// bkz. QuestionFeedbackService.GetArchetypeApprovalRatesAsync; null ise (yetersiz örnek VEYA
    /// bu archetype için hiç geri bildirim verisi yoksa) NÖTR (çarpan 1.0) kalır — "tek bir reddi
    /// genel kural sanma" ilkesi burada da geçerlidir. Dönüş 0-1 aralığına YAKIN (institutional
    /// çarpanla birlikte biraz dışına taşabilir) bir sıralama skoru — mutlak bir anlamı yok,
    /// yalnızca aday archetype'ları birbirine göre sıralamak için kullanılır.</summary>
    public static double Score(decimal? maarifAffinity, int maarifWeightPercent, decimal? institutionalApprovalRate = null)
    {
        var normalizedAffinity = (double)(maarifAffinity ?? 50m) / 100.0;
        var weight = Math.Clamp(maarifWeightPercent, 0, 100) / 100.0;
        // weight=100 → yalnızca affinity; weight=0 → affinity'den TAMAMEN bağımsız, nötr (0.5) sabit.
        var baseScore = normalizedAffinity * weight + 0.5 * (1 - weight);

        // %0 onay oranı → çarpan 0.5 (skoru yarıya indirir); %100 onay oranı → çarpan 1.5
        // (skoru %50 artırır); null (yetersiz örnek) → çarpan 1.0 (nötr, hiç etkilemez).
        var institutionalMultiplier = institutionalApprovalRate.HasValue ? 0.5 + (double)institutionalApprovalRate.Value : 1.0;

        return baseScore * institutionalMultiplier;
    }
}
