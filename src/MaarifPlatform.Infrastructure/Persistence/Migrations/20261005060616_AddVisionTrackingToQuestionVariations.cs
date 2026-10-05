using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisionTrackingToQuestionVariations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VisionCostUsd",
                table: "question_variation_batches",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisionModel",
                table: "question_variation_batches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisionProvider",
                table: "question_variation_batches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisionCostUsd",
                table: "question_variation_batches");

            migrationBuilder.DropColumn(
                name: "VisionModel",
                table: "question_variation_batches");

            migrationBuilder.DropColumn(
                name: "VisionProvider",
                table: "question_variation_batches");
        }
    }
}
