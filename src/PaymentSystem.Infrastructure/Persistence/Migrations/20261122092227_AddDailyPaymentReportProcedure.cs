using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyPaymentReportProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.sp_GetDailyPaymentReport
                    @From date,
                    @To date,
                    @ProviderId uniqueidentifier = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @FromUtc datetimeoffset(7) = TODATETIMEOFFSET(CAST(@From AS datetime2(7)), 0);
                    DECLARE @ToUtcExclusive datetimeoffset(7) = TODATETIMEOFFSET(DATEADD(day, 1, CAST(@To AS datetime2(7))), 0);

                    SELECT CAST(SWITCHOFFSET(p.CreatedAt, 0) AS date) AS [Date],
                           pr.Id AS ProviderId,
                           pr.Name AS ProviderName,
                           p.Status,
                           p.Currency,
                           COUNT_BIG(*) AS PaymentCount,
                           SUM(p.Amount) AS TotalAmount
                    FROM dbo.Payments AS p
                    JOIN dbo.PaymentProviders AS pr ON pr.Id = p.ProviderId
                    WHERE p.CreatedAt >= @FromUtc
                      AND p.CreatedAt < @ToUtcExclusive
                      AND (@ProviderId IS NULL OR p.ProviderId = @ProviderId)
                    GROUP BY CAST(SWITCHOFFSET(p.CreatedAt, 0) AS date), pr.Id, pr.Name, p.Status, p.Currency
                    ORDER BY [Date], ProviderName, p.Status, p.Currency
                    OPTION (RECOMPILE);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.sp_GetDailyPaymentReport;");
        }
    }
}
