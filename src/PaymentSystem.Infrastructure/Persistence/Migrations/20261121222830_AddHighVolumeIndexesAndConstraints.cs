using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHighVolumeIndexesAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentStatusHistory_Payments_PaymentId",
                table: "PaymentStatusHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PaymentStatusHistory",
                table: "PaymentStatusHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Payments",
                table: "Payments");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PaymentStatusHistory",
                table: "PaymentStatusHistory",
                column: "Id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Payments",
                table: "Payments",
                column: "Id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentStatusHistory_Payments_PaymentId",
                table: "PaymentStatusHistory",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.CreateIndex(
                name: "CX_PaymentStatusHistory_ChangedAt_Id",
                table: "PaymentStatusHistory",
                columns: new[] { "ChangedAt", "Id" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "CX_Payments_CreatedAt_Id",
                table: "Payments",
                columns: new[] { "CreatedAt", "Id" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Processing_LockedUntil",
                table: "Payments",
                column: "LockedUntil",
                filter: "[Status] = N'Processing'");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_CreatedAt",
                table: "Payments",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId_CreatedAt",
                table: "Payments",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Amount_Positive",
                table: "Payments",
                sql: "[Amount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_AttemptCount_NonNegative",
                table: "Payments",
                sql: "[AttemptCount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Completed_HasTransaction",
                table: "Payments",
                sql: "[Status] <> N'Completed' OR [ProviderTransactionId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Currency_Format",
                table: "Payments",
                sql: "[Currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Status",
                table: "Payments",
                sql: "[Status] IN (N'Pending', N'Processing', N'Completed', N'Failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentStatusHistory_Payments_PaymentId",
                table: "PaymentStatusHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PaymentStatusHistory",
                table: "PaymentStatusHistory");

            migrationBuilder.DropIndex(
                name: "CX_PaymentStatusHistory_ChangedAt_Id",
                table: "PaymentStatusHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Payments",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "CX_Payments_CreatedAt_Id",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Processing_LockedUntil",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Status_CreatedAt",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_UserId_CreatedAt",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Amount_Positive",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_AttemptCount_NonNegative",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Completed_HasTransaction",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Currency_Format",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Status",
                table: "Payments");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PaymentStatusHistory",
                table: "PaymentStatusHistory",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Payments",
                table: "Payments",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentStatusHistory_Payments_PaymentId",
                table: "PaymentStatusHistory",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
