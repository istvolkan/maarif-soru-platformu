using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Grade = table.Column<int>(type: "integer", nullable: true),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Publisher = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TotalPages = table.Column<int>(type: "integer", nullable: true),
                    StorageUri = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_books", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "maarif_standard_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maarif_standard_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reference_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Grade = table.Column<int>(type: "integer", nullable: true),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PublicationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Authority = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    StorageUri = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DocumentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "book_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNo = table.Column<int>(type: "integer", nullable: false),
                    RawText = table.Column<string>(type: "text", nullable: true),
                    OcrUsed = table.Column<bool>(type: "boolean", nullable: false),
                    ImageUri = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_book_pages_books_BookId",
                        column: x => x.BookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_outcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Grade = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaarifStandardVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_outcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_outcomes_maarif_standard_versions_MaarifStandardVe~",
                        column: x => x.MaarifStandardVersionId,
                        principalTable: "maarif_standard_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learning_outcomes_reference_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "reference_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reference_chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: true),
                    SectionPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChunkText = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reference_chunks_reference_documents_ReferenceDocumentId",
                        column: x => x.ReferenceDocumentId,
                        principalTable: "reference_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookPageId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionNo = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MaarifStandardVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_questions_book_pages_BookPageId",
                        column: x => x.BookPageId,
                        principalTable: "book_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_questions_books_BookId",
                        column: x => x.BookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_questions_maarif_standard_versions_MaarifStandardVersionId",
                        column: x => x.MaarifStandardVersionId,
                        principalTable: "maarif_standard_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ModelTier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ai_runs_questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "question_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_question_versions_questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_queue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReasonFlagsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_queue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_queue_questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_review_queue_users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "alignment_scores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Criterion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsCriticalGate = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alignment_scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alignment_scores_question_versions_QuestionVersionId",
                        column: x => x.QuestionVersionId,
                        principalTable: "question_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "distractors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionLabel = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    MisconceptionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    IsHypothesis = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_distractors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_distractors_question_versions_QuestionVersionId",
                        column: x => x.QuestionVersionId,
                        principalTable: "question_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question_dna",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBook = table.Column<string>(type: "text", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    Grade = table.Column<int>(type: "integer", nullable: true),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    Theme = table.Column<string>(type: "text", nullable: true),
                    Topic = table.Column<string>(type: "text", nullable: true),
                    Subtopic = table.Column<string>(type: "text", nullable: true),
                    OriginalQuestion = table.Column<string>(type: "text", nullable: true),
                    OriginalOptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    OriginalAnswer = table.Column<string>(type: "text", nullable: true),
                    OriginalVisualReference = table.Column<string>(type: "text", nullable: true),
                    MathematicalCore = table.Column<string>(type: "text", nullable: true),
                    LearningOutcome = table.Column<string>(type: "text", nullable: true),
                    LearningOutcomeCode = table.Column<string>(type: "text", nullable: true),
                    FieldSkill = table.Column<string>(type: "text", nullable: true),
                    ConceptualSkill = table.Column<string>(type: "text", nullable: true),
                    ProcessComponent = table.Column<string>(type: "text", nullable: true),
                    QuestionType = table.Column<string>(type: "text", nullable: true),
                    ContextType = table.Column<string>(type: "text", nullable: true),
                    ContextQuality = table.Column<string>(type: "text", nullable: true),
                    RepresentationTypesJson = table.Column<string>(type: "jsonb", nullable: true),
                    CognitiveLevel = table.Column<string>(type: "text", nullable: true),
                    ReasoningTypesJson = table.Column<string>(type: "jsonb", nullable: true),
                    ExpectedSolutionSteps = table.Column<string>(type: "text", nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AiEstimatedStudentTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    MaarifAlignmentScore = table.Column<int>(type: "integer", nullable: true),
                    AlignmentIssuesJson = table.Column<string>(type: "jsonb", nullable: true),
                    TransformationLevel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NewQuestion = table.Column<string>(type: "text", nullable: true),
                    NewOptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CorrectAnswer = table.Column<string>(type: "text", nullable: true),
                    Solution = table.Column<string>(type: "text", nullable: true),
                    QualityScore = table.Column<int>(type: "integer", nullable: true),
                    QualityFlagsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EditorRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SourceReferencesJson = table.Column<string>(type: "jsonb", nullable: true),
                    DnaSchemaVersion = table.Column<string>(type: "text", nullable: false),
                    ExtensionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_dna", x => x.Id);
                    table.ForeignKey(
                        name: "FK_question_dna_question_versions_QuestionVersionId",
                        column: x => x.QuestionVersionId,
                        principalTable: "question_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_Provider",
                table: "ai_runs",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_QuestionId",
                table: "ai_runs",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_Stage",
                table: "ai_runs",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_alignment_scores_QuestionVersionId",
                table: "alignment_scores",
                column: "QuestionVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_EntityName_EntityId",
                table: "audit_log",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_book_pages_BookId_PageNo",
                table: "book_pages",
                columns: new[] { "BookId", "PageNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_distractors_QuestionVersionId",
                table: "distractors",
                column: "QuestionVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_outcomes_Code_MaarifStandardVersionId",
                table: "learning_outcomes",
                columns: new[] { "Code", "MaarifStandardVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_outcomes_MaarifStandardVersionId",
                table: "learning_outcomes",
                column: "MaarifStandardVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_outcomes_SourceDocumentId",
                table: "learning_outcomes",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_maarif_standard_versions_Code",
                table: "maarif_standard_versions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_templates_Name_Version",
                table: "prompt_templates",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_question_dna_LearningOutcomeCode",
                table: "question_dna",
                column: "LearningOutcomeCode");

            migrationBuilder.CreateIndex(
                name: "IX_question_dna_QuestionVersionId",
                table: "question_dna",
                column: "QuestionVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_question_versions_QuestionId_VersionNo",
                table: "question_versions",
                columns: new[] { "QuestionId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_questions_BookId",
                table: "questions",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_questions_BookPageId",
                table: "questions",
                column: "BookPageId");

            migrationBuilder.CreateIndex(
                name: "IX_questions_MaarifStandardVersionId",
                table: "questions",
                column: "MaarifStandardVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_questions_Status",
                table: "questions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_reference_chunks_ReferenceDocumentId",
                table: "reference_chunks",
                column: "ReferenceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_reference_documents_DocumentHash",
                table: "reference_documents",
                column: "DocumentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_review_queue_AssignedToUserId",
                table: "review_queue",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_review_queue_QuestionId",
                table: "review_queue",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_review_queue_Status_Priority",
                table: "review_queue",
                columns: new[] { "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_runs");

            migrationBuilder.DropTable(
                name: "alignment_scores");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "distractors");

            migrationBuilder.DropTable(
                name: "learning_outcomes");

            migrationBuilder.DropTable(
                name: "prompt_templates");

            migrationBuilder.DropTable(
                name: "question_dna");

            migrationBuilder.DropTable(
                name: "reference_chunks");

            migrationBuilder.DropTable(
                name: "review_queue");

            migrationBuilder.DropTable(
                name: "question_versions");

            migrationBuilder.DropTable(
                name: "reference_documents");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "questions");

            migrationBuilder.DropTable(
                name: "book_pages");

            migrationBuilder.DropTable(
                name: "maarif_standard_versions");

            migrationBuilder.DropTable(
                name: "books");
        }
    }
}
