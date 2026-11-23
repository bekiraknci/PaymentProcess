using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Api.IntegrationTests.Infrastructure;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Authorization;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Api.IntegrationTests.Database;

[Collection(ApiCollection.Name)]
public sealed class DatabaseTests(PaymentApiFactory factory) : ApiTestBase(factory)
{
    private async Task<List<T>> QueryAsync<T>(string sql, Func<SqlDataReader, T> map)
    {
        await using var connection = await Factory.OpenConnectionAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await Factory.OpenConnectionAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task All_migrations_are_applied_and_the_model_has_no_pending_changes()
    {
        var (pending, hasModelChanges) = await Factory.WithDbAsync(async db =>
            ((await db.Database.GetPendingMigrationsAsync()).ToList(), db.Database.HasPendingModelChanges()));

        pending.Should().BeEmpty();
        hasModelChanges.Should().BeFalse("the model snapshot must match the entity configuration");
    }

    [Fact]
    public async Task Seeded_permissions_match_the_policies_the_api_registers()
    {
        var names = await QueryAsync("SELECT Name FROM dbo.Permissions", r => r.GetString(0));

        names.Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task Seeded_role_permissions_grant_admin_everything_and_user_create_and_view()
    {
        var grants = await QueryAsync("SELECT Role, PermissionName FROM dbo.RolePermissions", r => (Role: r.GetString(0), Permission: r.GetString(1)));

        grants.Where(g => g.Role == "Admin").Select(g => g.Permission).Should().BeEquivalentTo(Permissions.All);
        grants.Where(g => g.Role == "User").Select(g => g.Permission).Should().BeEquivalentTo(Permissions.PaymentsCreate, Permissions.PaymentsView);
        grants.Should().OnlyContain(g => g.Role == "Admin" || g.Role == "User");
    }

    [Theory]
    [InlineData("INSERT INTO dbo.RolePermissions (Role, PermissionName) VALUES (N'User', 'Payments.Refund')", 547)]
    [InlineData("INSERT INTO dbo.RolePermissions (Role, PermissionName) VALUES (N'Auditor', 'Payments.View')", 547)]
    [InlineData("INSERT INTO dbo.RolePermissions (Role, PermissionName) VALUES (N'User', 'Payments.View')", 2627)]
    [InlineData("DELETE FROM dbo.Permissions WHERE Name = 'Payments.View'", 547)]
    public async Task Role_permission_table_rejects_unknown_permissions_unknown_roles_and_duplicates(string sql, int expectedError)
    {
        var act = () => ExecuteAsync(sql);

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(expectedError);
    }

    [Fact]
    public async Task Money_and_concurrency_columns_have_the_expected_sql_types()
    {
        var columns = await QueryAsync(
            """
            SELECT c.name, t.name, c.precision, c.scale, c.max_length
            FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID('dbo.Payments') AND c.name IN ('Amount', 'Currency', 'RowVersion', 'RequestHash')
            """,
            r => (Name: r.GetString(0), Type: r.GetString(1), Precision: r.GetByte(2), Scale: r.GetByte(3), Length: r.GetInt16(4)));

        columns.Should().ContainEquivalentOf(("Amount", "decimal", (byte)18, (byte)2, (short)9));
        columns.Should().ContainEquivalentOf(("Currency", "char", (byte)0, (byte)0, (short)3));
        columns.Should().ContainEquivalentOf(("RequestHash", "char", (byte)0, (byte)0, (short)64));
        columns.Single(c => c.Name == "RowVersion").Type.Should().Be("timestamp");
    }

    [Fact]
    public async Task Expected_indexes_exist_with_the_right_shape()
    {
        var indexes = await QueryAsync(
            """
            SELECT i.name, i.type_desc, i.is_unique, i.is_primary_key, ISNULL(i.filter_definition, ''),
                   STRING_AGG(CASE WHEN ic.is_included_column = 0 THEN c.name END, ',') WITHIN GROUP (ORDER BY ic.key_ordinal),
                   ISNULL(STRING_AGG(CASE WHEN ic.is_included_column = 1 THEN c.name END, ','), '')
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id IN (OBJECT_ID('dbo.Payments'), OBJECT_ID('dbo.PaymentProviders'), OBJECT_ID('dbo.Users'), OBJECT_ID('dbo.PaymentStatusHistory'))
            GROUP BY i.name, i.type_desc, i.is_unique, i.is_primary_key, i.filter_definition
            """,
            r => (Name: r.GetString(0), Type: r.GetString(1), Unique: r.GetBoolean(2), Primary: r.GetBoolean(3), Filter: r.GetString(4), Keys: r.GetString(5), Includes: r.GetString(6)));

        var byName = indexes.ToDictionary(i => i.Name);

        byName["CX_Payments_CreatedAt_Id"].Type.Should().Be("CLUSTERED");
        byName["CX_Payments_CreatedAt_Id"].Keys.Should().Be("CreatedAt,Id");
        byName["PK_Payments"].Type.Should().Be("NONCLUSTERED");
        byName["CX_PaymentStatusHistory_ChangedAt_Id"].Type.Should().Be("CLUSTERED");
        byName["IX_Payments_UserId_Status_CreatedAt"].Keys.Should().Be("UserId,Status,CreatedAt");
        byName["IX_Payments_UserId_CreatedAt"].Keys.Should().Be("UserId,CreatedAt");
        byName["IX_Payments_Status_NextAttemptAt"].Keys.Should().Be("Status,NextAttemptAt");
        byName["IX_Payments_Status_CreatedAt"].Keys.Should().Be("Status,CreatedAt");
        byName["IX_Payments_ProviderId_CreatedAt"].Keys.Should().Be("ProviderId,CreatedAt");
        byName["IX_Payments_ProviderId_CreatedAt"].Includes.Split(',').Should().BeEquivalentTo("Status", "Amount", "Currency");
        byName["IX_Payments_Processing_LockedUntil"].Filter.Should().Contain("Processing");
        byName["UX_Payments_UserId_IdempotencyKey"].Unique.Should().BeTrue();
        byName["UX_Payments_UserId_IdempotencyKey"].Filter.Should().Contain("IdempotencyKey");
        byName["UX_PaymentProviders_Code"].Unique.Should().BeTrue();
        byName["UX_Users_Email"].Unique.Should().BeTrue();
        byName["IX_PaymentStatusHistory_PaymentId_ChangedAt"].Keys.Should().Be("PaymentId,ChangedAt");
    }

    [Fact]
    public async Task Seed_data_is_present()
    {
        var (providers, users) = await Factory.WithDbAsync(async db =>
            (await db.PaymentProviders.CountAsync(), await db.Users.CountAsync()));

        providers.Should().BeGreaterThanOrEqualTo(2);
        users.Should().Be(3);
    }

    [Theory]
    [InlineData("Amount = 0", "CK_Payments_Amount_Positive")]
    [InlineData("Amount = -10", "CK_Payments_Amount_Positive")]
    [InlineData("Currency = 'usd'", "CK_Payments_Currency_Format")]
    [InlineData("Currency = '1$X'", "CK_Payments_Currency_Format")]
    [InlineData("Status = N'Refunded'", "CK_Payments_Status")]
    [InlineData("AttemptCount = -1", "CK_Payments_AttemptCount_NonNegative")]
    [InlineData("Status = N'Completed', ProviderTransactionId = NULL", "CK_Payments_Completed_HasTransaction")]
    public async Task Check_constraints_reject_invalid_rows_written_outside_the_domain(string assignment, string constraint)
    {
        var payment = await CreatePayment(await AsUser2(), StubPaymentProviderClient.DeclineAmount);
        await WaitForFinalStatus(await AsUser2(), payment.Id);

        var act = () => ExecuteAsync($"UPDATE dbo.Payments SET {assignment} WHERE Id = '{payment.Id}'");

        (await act.Should().ThrowAsync<SqlException>()).Which.Message.Should().Contain(constraint);
    }

    [Fact]
    public async Task Unique_index_blocks_duplicate_idempotency_keys_but_allows_many_nulls()
    {
        var client = await AsUser2();
        var keyA = "db-key-" + Guid.NewGuid();
        await PostPayment(client, PaymentBody(10m), keyA);
        var second = await PostPayment(client, PaymentBody(11m), "db-key-" + Guid.NewGuid());
        var secondId = (await second.Content.ReadDataAsync<PaymentDto>(Json))!.Id;
        await CreatePayment(client);
        await CreatePayment(client);

        var duplicate = () => ExecuteAsync($"UPDATE dbo.Payments SET IdempotencyKey = N'{keyA}' WHERE Id = '{secondId}'");
        var nullKeys = await Factory.WithDbAsync(db => db.Payments.CountAsync(p => p.UserId == Guid.Parse("0196a000-0000-7000-8000-000000000103") && p.IdempotencyKey == null));

        (await duplicate.Should().ThrowAsync<SqlException>()).Which.Number.Should().BeOneOf(2601, 2627);
        nullKeys.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Provider_with_payments_cannot_be_deleted()
    {
        await CreatePayment(await AsUser());

        var act = () => ExecuteAsync($"DELETE FROM dbo.PaymentProviders WHERE Id = '{PaymentApiFactory.FakePayId}'");

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
    }

    [Fact]
    public async Task Concurrent_updates_of_the_same_payment_are_detected_by_rowversion()
    {
        var payment = await CreatePayment(await AsUser2(), StubPaymentProviderClient.DeclineAmount);
        await WaitForFinalStatus(await AsUser2(), payment.Id);

        await using var scopeA = Factory.Services.CreateAsyncScope();
        await using var scopeB = Factory.Services.CreateAsyncScope();
        var repoA = scopeA.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var uowA = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var uowB = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var copyA = (await repoA.GetByIdAsync(payment.Id))!;
        var copyB = (await repoB.GetByIdAsync(payment.Id))!;
        copyA.RequeueForRetry(DateTimeOffset.UtcNow, "A");
        copyB.RequeueForRetry(DateTimeOffset.UtcNow, "B");

        await uowA.SaveChangesAsync();
        var second = () => uowB.SaveChangesAsync();

        await second.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Hot_queries_use_index_seeks_instead_of_table_scans()
    {
        var userId = "0196a000-0000-7000-8000-000000000102";
        var queries = new Dictionary<string, string>
        {
            ["user list"] = $"SELECT TOP (20) Id, CreatedAt FROM dbo.Payments WHERE UserId = '{userId}' ORDER BY CreatedAt DESC, Id DESC",
            ["user list by status"] = $"SELECT TOP (20) Id, CreatedAt FROM dbo.Payments WHERE UserId = '{userId}' AND Status = N'Failed' ORDER BY CreatedAt DESC",
            ["idempotency lookup"] = $"SELECT Id FROM dbo.Payments WHERE UserId = '{userId}' AND IdempotencyKey = N'abc'",
            ["pending pickup"] = "SELECT TOP (100) Id FROM dbo.Payments WHERE Status = N'Pending' ORDER BY CreatedAt",
            ["expired leases"] = "SELECT TOP (100) Id FROM dbo.Payments WHERE Status = N'Processing' AND LockedUntil <= SYSDATETIMEOFFSET()",
            ["retry job"] = "SELECT TOP (100) Id FROM dbo.Payments WHERE Status = N'Failed' AND NextAttemptAt <= SYSDATETIMEOFFSET()",
            ["report range"] = "SELECT ProviderId, CreatedAt, Status, Currency, Amount FROM dbo.Payments WHERE ProviderId = '0196a000-0000-7000-8000-000000000001' AND CreatedAt >= '2026-01-01' AND CreatedAt < '2027-01-01'",
            ["history"] = "SELECT PaymentId, ChangedAt FROM dbo.PaymentStatusHistory WHERE PaymentId = '0196a000-0000-7000-8000-000000000001' ORDER BY ChangedAt"
        };

        await using var connection = await Factory.OpenConnectionAsync();
        foreach (var (name, sql) in queries)
        {
            await using (var on = new SqlCommand("SET SHOWPLAN_XML ON", connection))
            {
                await on.ExecuteNonQueryAsync();
            }

            string plan;
            await using (var command = new SqlCommand(sql, connection))
            {
                plan = (string)(await command.ExecuteScalarAsync())!;
            }

            await using (var off = new SqlCommand("SET SHOWPLAN_XML OFF", connection))
            {
                await off.ExecuteNonQueryAsync();
            }

            plan.Should().Contain("Index Seek", $"'{name}' must seek an index");
            plan.Should().NotContain("PhysicalOp=\"Table Scan\"", $"'{name}' must not scan the table");
            if (name == "report range")
            {
                plan.Should().NotContain("Lookup=\"1\"", "the report index must cover every column the stored procedure reads");
            }
        }
    }
}
