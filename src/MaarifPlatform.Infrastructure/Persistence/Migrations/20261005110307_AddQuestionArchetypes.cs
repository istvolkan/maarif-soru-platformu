using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionArchetypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArchetypeId",
                table: "question_dna",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "question_archetypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GradeRangeMin = table.Column<int>(type: "integer", nullable: false),
                    GradeRangeMax = table.Column<int>(type: "integer", nullable: false),
                    VisualType = table.Column<string>(type: "text", nullable: true),
                    SourceSupportCount = table.Column<int>(type: "integer", nullable: false),
                    DistinctBookSupportCount = table.Column<int>(type: "integer", nullable: false),
                    MaarifAffinity = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    QualityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    CentroidEmbedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_archetypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "question_archetype_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchetypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_archetype_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_question_archetype_members_question_archetypes_ArchetypeId",
                        column: x => x.ArchetypeId,
                        principalTable: "question_archetypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_question_archetype_members_question_versions_QuestionVersio~",
                        column: x => x.QuestionVersionId,
                        principalTable: "question_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_question_dna_ArchetypeId",
                table: "question_dna",
                column: "ArchetypeId");

            migrationBuilder.CreateIndex(
                name: "IX_question_archetype_members_ArchetypeId_BookId",
                table: "question_archetype_members",
                columns: new[] { "ArchetypeId", "BookId" });

            migrationBuilder.CreateIndex(
                name: "IX_question_archetype_members_ArchetypeId_QuestionVersionId",
                table: "question_archetype_members",
                columns: new[] { "ArchetypeId", "QuestionVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_question_archetype_members_QuestionVersionId",
                table: "question_archetype_members",
                column: "QuestionVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_question_archetypes_Subject",
                table: "question_archetypes",
                column: "Subject");

            migrationBuilder.AddForeignKey(
                name: "FK_question_dna_question_archetypes_ArchetypeId",
                table: "question_dna",
                column: "ArchetypeId",
                principalTable: "question_archetypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_question_dna_question_archetypes_ArchetypeId",
                table: "question_dna");

            migrationBuilder.DropTable(
                name: "question_archetype_members");

            migrationBuilder.DropTable(
                name: "question_archetypes");

            migrationBuilder.DropIndex(
                name: "IX_question_dna_ArchetypeId",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "ArchetypeId",
                table: "question_dna");
        }
    }
}
