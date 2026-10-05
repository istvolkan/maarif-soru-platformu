using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAuthorName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorName",
                table: "books",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_books_AuthorName",
                table: "books",
                column: "AuthorName");

            migrationBuilder.CreateIndex(
                name: "IX_books_Publisher",
                table: "books",
                column: "Publisher");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_books_AuthorName",
                table: "books");

            migrationBuilder.DropIndex(
                name: "IX_books_Publisher",
                table: "books");

            migrationBuilder.DropColumn(
                name: "AuthorName",
                table: "books");
        }
    }
}
