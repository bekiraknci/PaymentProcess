using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncludeCurrencyInReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX [IX_Payments_ProviderId_CreatedAt] ON [Payments] ([ProviderId], [CreatedAt]) INCLUDE ([Status], [Amount], [Currency]) WITH (DROP_EXISTING = ON);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX [IX_Payments_ProviderId_CreatedAt] ON [Payments] ([ProviderId], [CreatedAt]) INCLUDE ([Status], [Amount]) WITH (DROP_EXISTING = ON);");
        }
    }
}
