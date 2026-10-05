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
    IndependentSolve
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
