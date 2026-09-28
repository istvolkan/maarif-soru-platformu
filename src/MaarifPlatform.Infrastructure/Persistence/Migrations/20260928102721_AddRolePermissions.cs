using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Permission = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_Role_Permission",
                table: "role_permissions",
                columns: new[] { "Role", "Permission" },
                unique: true);

            // Bu [Authorize(Roles="Admin,Editor")] öznitelikleriyle Pool.razor/Generate.razor'da
            // ZATEN sahip olduğu erişimi korur — migration öncesi Editor rolündeki hiçbir
            // kullanıcı bu yayınla erişim kaybetmez. Admin için hiç satır gerekmez
            // (PermissionAuthorizationHandler Admin'i koşulsuz geçirir).
            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "Id", "Role", "Permission", "CreatedAt" },
                values: new object[,]
                {
                    { Guid.NewGuid(), "Editor", "QuestionPoolAccess", DateTimeOffset.UtcNow },
                    { Guid.NewGuid(), "Editor", "QuestionGenerationAccess", DateTimeOffset.UtcNow }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_permissions");
        }
    }
}
