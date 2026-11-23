using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PaymentSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddViewAllAndReportPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Name", "Description" },
                values: new object[,]
                {
                    { "Payments.ViewAll", "View payments of every user, not only your own." },
                    { "Reports.View", "Read the daily payment report." }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionName", "Role" },
                values: new object[,]
                {
                    { "Payments.ViewAll", "Admin" },
                    { "Reports.View", "Admin" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionName", "Role" },
                keyValues: new object[] { "Payments.ViewAll", "Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionName", "Role" },
                keyValues: new object[] { "Reports.View", "Admin" });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Name",
                keyValue: "Payments.ViewAll");

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Name",
                keyValue: "Reports.View");
        }
    }
}
