using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionEmbeddingSourceKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mevcut tüm satırlar bu sütun eklenmeden önce yalnızca AI-üretilen sorulardan
            // oluşuyordu (bkz. QuestionEmbedding'deki doc) — varsayılan "" DEĞİL "Generated"
            // olmalı, aksi halde HasConversion<string>() geriye okurken tanımsız bir enum adıyla
            // karşılaşıp patlar.
            migrationBuilder.AddColumn<string>(
                name: "SourceKind",
                table: "question_embeddings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Generated");

            migrationBuilder.CreateIndex(
                name: "IX_question_embeddings_SourceKind",
                table: "question_embeddings",
                column: "SourceKind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_question_embeddings_SourceKind",
                table: "question_embeddings");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "question_embeddings");
        }
    }
}
