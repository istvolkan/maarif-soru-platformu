using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisionSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresVisual",
                table: "question_dna",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VisualConfidence",
                table: "question_dna",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VisualDependencyScore",
                table: "question_dna",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualDescription",
                table: "question_dna",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualElementsJson",
                table: "question_dna",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualMeasurementsJson",
                table: "question_dna",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualRelationsJson",
                table: "question_dna",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisualRequiredForSolution",
                table: "question_dna",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualReusability",
                table: "question_dna",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualSymbolsJson",
                table: "question_dna",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualTextJson",
                table: "question_dna",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualType",
                table: "question_dna",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualWarningsJson",
                table: "question_dna",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "question_visual_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookPageId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageUri = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    BoundingBoxJson = table.Column<string>(type: "jsonb", nullable: true),
                    WidthPx = table.Column<int>(type: "integer", nullable: true),
                    HeightPx = table.Column<int>(type: "integer", nullable: true),
                    AssetHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_visual_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_question_visual_assets_book_pages_BookPageId",
                        column: x => x.BookPageId,
                        principalTable: "book_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_question_visual_assets_questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_question_dna_RequiresVisual",
                table: "question_dna",
                column: "RequiresVisual");

            migrationBuilder.CreateIndex(
                name: "IX_question_visual_assets_AssetHash",
                table: "question_visual_assets",
                column: "AssetHash");

            migrationBuilder.CreateIndex(
                name: "IX_question_visual_assets_BookPageId",
                table: "question_visual_assets",
                column: "BookPageId");

            migrationBuilder.CreateIndex(
                name: "IX_question_visual_assets_QuestionId",
                table: "question_visual_assets",
                column: "QuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "question_visual_assets");

            migrationBuilder.DropIndex(
                name: "IX_question_dna_RequiresVisual",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "RequiresVisual",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualConfidence",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualDependencyScore",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualDescription",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualElementsJson",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualMeasurementsJson",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualRelationsJson",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualRequiredForSolution",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualReusability",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualSymbolsJson",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualTextJson",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualType",
                table: "question_dna");

            migrationBuilder.DropColumn(
                name: "VisualWarningsJson",
                table: "question_dna");
        }
    }
}
