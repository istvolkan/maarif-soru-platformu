using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurriculumStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "learning_outcomes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<Guid>(
                name: "ThemeId",
                table: "learning_outcomes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "content_frameworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningOutcomeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_frameworks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_frameworks_learning_outcomes_LearningOutcomeId",
                        column: x => x.LearningOutcomeId,
                        principalTable: "learning_outcomes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_frameworks_reference_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "reference_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "field_skills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_skills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_field_skills_reference_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "reference_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "process_components",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningOutcomeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_components", x => x.Id);
                    table.ForeignKey(
                        name: "FK_process_components_learning_outcomes_LearningOutcomeId",
                        column: x => x.LearningOutcomeId,
                        principalTable: "learning_outcomes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_process_components_reference_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "reference_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "themes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Grade = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    MaarifStandardVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_themes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_themes_maarif_standard_versions_MaarifStandardVersionId",
                        column: x => x.MaarifStandardVersionId,
                        principalTable: "maarif_standard_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_themes_reference_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "reference_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_learning_outcomes_ThemeId_ApprovalStatus",
                table: "learning_outcomes",
                columns: new[] { "ThemeId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_content_frameworks_LearningOutcomeId_ApprovalStatus",
                table: "content_frameworks",
                columns: new[] { "LearningOutcomeId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_content_frameworks_SourceDocumentId",
                table: "content_frameworks",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_field_skills_SourceDocumentId",
                table: "field_skills",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_field_skills_Subject_Code",
                table: "field_skills",
                columns: new[] { "Subject", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_process_components_LearningOutcomeId_ApprovalStatus",
                table: "process_components",
                columns: new[] { "LearningOutcomeId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_process_components_SourceDocumentId",
                table: "process_components",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_themes_Grade_Subject_ApprovalStatus",
                table: "themes",
                columns: new[] { "Grade", "Subject", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_themes_MaarifStandardVersionId",
                table: "themes",
                column: "MaarifStandardVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_themes_SourceDocumentId",
                table: "themes",
                column: "SourceDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_learning_outcomes_themes_ThemeId",
                table: "learning_outcomes",
                column: "ThemeId",
                principalTable: "themes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_learning_outcomes_themes_ThemeId",
                table: "learning_outcomes");

            migrationBuilder.DropTable(
                name: "content_frameworks");

            migrationBuilder.DropTable(
                name: "field_skills");

            migrationBuilder.DropTable(
                name: "process_components");

            migrationBuilder.DropTable(
                name: "themes");

            migrationBuilder.DropIndex(
                name: "IX_learning_outcomes_ThemeId_ApprovalStatus",
                table: "learning_outcomes");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "learning_outcomes");

            migrationBuilder.DropColumn(
                name: "ThemeId",
                table: "learning_outcomes");
        }
    }
}
