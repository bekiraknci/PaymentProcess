using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PaymentSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PermissionName = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.Role, x.PermissionName });
                    table.CheckConstraint("CK_RolePermissions_Role", "[Role] IN (N'Admin', N'User')");
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionName",
                        column: x => x.PermissionName,
                        principalTable: "Permissions",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Name", "Description" },
                values: new object[,]
                {
                    { "Payments.Create", "Submit payment requests." },
                    { "Payments.Process", "Manually retry failed payments." },
                    { "Payments.View", "View payments and their status history." },
                    { "Providers.Manage", "Create, update, activate and deactivate payment providers." }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionName", "Role" },
                values: new object[,]
                {
                    { "Payments.Create", "Admin" },
                    { "Payments.Process", "Admin" },
                    { "Payments.View", "Admin" },
                    { "Providers.Manage", "Admin" },
                    { "Payments.Create", "User" },
                    { "Payments.View", "User" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionName",
                table: "RolePermissions",
                column: "PermissionName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "Permissions");
        }
    }
}
