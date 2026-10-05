namespace MaarifPlatform.Domain.Enums;

public enum UserRole
{
    Admin,
    Editor,
    Teacher,
    Reviewer
}

/// <summary>Rol bazında verilebilen, sayfa erişimini denetleyen yetki türleri — bkz.
/// <see cref="Entities.RolePermission"/>. Admin bu listeye HİÇ bakılmadan her zaman tüm
/// yetkilere sahiptir (kilitlenme riskini önlemek için PermissionAuthorizationHandler'da
/// sabit kodlanmıştır); burada yalnızca Admin DIŞINDAKİ rollere devredilebilecek yetkiler
/// tanımlanır. Books/Settings/Users gibi hassas admin sayfaları bu sisteme dahil DEĞİLDİR,
/// [Authorize(Roles="Admin")] olarak sabit kalır.</summary>
public enum Permission
{
    QuestionPoolAccess,
    QuestionGenerationAccess,
    ReferenceDocumentUpload,
    CurriculumApproval
}

/// <summary>Question DNA §17 durum makinesi: EXTRACTED → ... → PUBLISHED.</summary>
public enum QuestionStatus
{
    Extracted,
    Analyzed,
    Transformed,
    AiApproved,
    ManualReviewRequired,
    EditorApproved,
    Rejected,
    Published
}

public enum QuestionVersionStage
{
    Original,
    Analyzed,
    Transformed,
    Edited,
    Final
}

/// <summary>§41/§48 Soru Havuzu sınıflandırması (Question Intelligence Engine Faz 4) —
/// MaarifAlignmentScore'un §41'deki 5 bandını (0-29/30-49/50-69/70-84/85-100) §48'in 3 mantıksal
/// havuzuna deterministik olarak daraltır (bkz. QuestionPoolClassifier). BİLİNÇLİ TASARIM: bu
/// yalnızca bir ETİKETTİR — hiçbir soru bu sınıflandırmaya göre silinmez/gizlenmez, Traditional
/// bir soru da (temel işlem becerisi/kavram bilgisi/geleneksel sınav hazırlığı için) değerlidir.</summary>
public enum QuestionPoolClassification
{
    Traditional,
    Hybrid,
    MaarifAligned
}

/// <summary>§52 Orijinallik Kontrolü (Question Intelligence Engine Faz 0) —
/// <see cref="Entities.QuestionEmbedding"/> havuzunun hangi kaynaktan geldiğini ayırt eder.
/// Eskiden bu havuz yalnızca AI-ÜRETİLEN sorulardan oluşuyordu (yalnızca kendi aralarında
/// tekrar kontrolü); artık kitaptan ÇIKARILAN gerçek kaynak sorular da aynı havuza eklenir ki
/// yeni üretimler kaynak metne karşı da (yalnızca önceki üretimlere karşı değil) kontrol
/// edilebilsin.</summary>
public enum QuestionEmbeddingSourceKind
{
    Generated,
    Extracted
}

/// <summary>§55/§56 Geri Bildirim Öğrenme (Question Intelligence Engine Faz 7) — bir sorunun
/// yaşam döngüsünde GERÇEKTEN gerçekleşen öğretmen/sistem eylemleri. DÜRÜSTLÜK NOTU: bu enum'un
/// TAMAMI §55'in orijinal taksonomisiyle (Approved/Edited/Rejected/Regenerated/UsedInBook/
/// UsedInExam) birebir eşlenir, AMA bu platformda şu an yalnızca <see cref="Approved"/>,
/// <see cref="Rejected"/> ve <see cref="UsedInBook"/> GERÇEKTEN üretilir:
///   - Edited: platformda şu an bir "soru metnini düzenle" özelliği YOK (QuestionVersionStage.Edited
///     da aynı nedenle hiç kullanılmıyor) — bu değer hiçbir yerden tetiklenmiyor, yalnızca ileride
///     bir düzenleme özelliği eklenirse hazır bir yer tutucu.
///   - Regenerated: tekil bir soru için "yeniden üret" eylemi yok (yalnızca toplu üretim sırasındaki
///     OTOMATİK deneme-tekrarı var, bu bir öğretmen eylemi değil) — bu değer de şu an tetiklenmiyor.
///   - UsedInExam: platformda "sınav" kavramı hiç yok (yalnızca Book var) — bu değer de tetiklenmiyor.
/// Var olmayan bir özelliğin sahte verisini üretmektense bu alanları BOŞ bırakmak tercih edildi —
/// aynı projenin QuestionDna'daki "deklare edilmiş ama doldurulmamış alan" konvansiyonuyla tutarlı.</summary>
public enum QuestionLifecycleEventType
{
    Approved,
    Rejected,
    Edited,
    Regenerated,
    UsedInBook,
    UsedInExam
}

/// <summary>Rubrik puanına göre dönüşüm kararı, bkz. tasarım dokümanı §5/§E.</summary>
public enum TransformationLevel
{
    NoChange,
    LightEdit,
    ModerateTransformation,
    MajorTransformation,
    Rewrite,
    Reject,
    ManualReviewRequired
}

/// <summary>Editörün seçtiği dönüşüm modu, bkz. §6.</summary>
public enum TransformationMode
{
    Conservative,
    Transform,
    Redesign
}

public enum DifficultyLevel
{
    VeryEasy,
    Easy,
    Medium,
    Hard,
    VeryHard
}

public enum SourceType
{
    LegacyBook,
    MebReference,
    Generated
}

public enum ActorType
{
    Ai,
    Human
}

/// <summary>Model routing katmanı, bkz. §H.</summary>
public enum ModelTier
{
    Cheap,
    Mid,
    Strong
}

public enum PipelineStage
{
    Classification,
    Extraction,
    Vision,
    Analysis,
    Transformation,
    DistractorGeneration,
    Judge,
    Generation,
    CurriculumExtraction,
    CurriculumValidation,
    IndependentSolve,
    DnaAnalysis
}

/// <summary>Curriculum yapı elemanlarının (Theme/LearningOutcome/ContentFramework/
/// ProcessComponent/FieldSkill) insan onay durumu — yalnızca Approved kayıtlar cascading
/// dropdown'larda görünür ve üretimde kullanılabilir (LLM'in curriculum uydurmasını
/// engellemenin ikinci katmanı, birincisi extraction'ın gerçek doküman metnine bağlı olması).</summary>
public enum ApprovalStatus
{
    Draft,
    Approved,
    Rejected
}
