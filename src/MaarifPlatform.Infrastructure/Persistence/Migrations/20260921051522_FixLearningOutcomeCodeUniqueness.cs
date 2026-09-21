using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixLearningOutcomeCodeUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_learning_outcomes_Code_MaarifStandardVersionId",
                table: "learning_outcomes");

            migrationBuilder.CreateIndex(
                name: "IX_learning_outcomes_Code_Subject_Grade_MaarifStandardVersionId",
                table: "learning_outcomes",
                columns: new[] { "Code", "Subject", "Grade", "MaarifStandardVersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_learning_outcomes_Code_Subject_Grade_MaarifStandardVersionId",
                table: "learning_outcomes");

            migrationBuilder.CreateIndex(
                name: "IX_learning_outcomes_Code_MaarifStandardVersionId",
                table: "learning_outcomes",
                columns: new[] { "Code", "MaarifStandardVersionId" },
                unique: true);
        }
    }
}
