namespace MaarifPlatform.Domain.Enums;

public enum UserRole
{
    Admin,
    Editor,
    Teacher,
    Reviewer
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
    MebReference
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
    Generation
}
