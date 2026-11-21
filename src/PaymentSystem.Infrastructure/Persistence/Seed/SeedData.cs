using PaymentSystem.Domain.Users;

namespace PaymentSystem.Infrastructure.Persistence.Seed;

public static class SeedData
{
    public static readonly Guid FakePayProviderId = Guid.Parse("0196a000-0000-7000-8000-000000000001");
    public static readonly Guid LegacyPayProviderId = Guid.Parse("0196a000-0000-7000-8000-000000000002");

    public static readonly Guid AdminUserId = Guid.Parse("0196a000-0000-7000-8000-000000000101");
    public static readonly Guid UserId = Guid.Parse("0196a000-0000-7000-8000-000000000102");
    public static readonly Guid User2Id = Guid.Parse("0196a000-0000-7000-8000-000000000103");

    private const string SeedActor = "seed";
    private static readonly DateTimeOffset SeedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static object[] Providers() =>
    [
        new
        {
            Id = FakePayProviderId,
            Code = "FAKEPAY",
            Name = "FakePay",
            BaseUrl = (string?)"http://localhost:5100",
            IsActive = true,
            _supportedCurrencies = "EUR,GBP,TRY,USD",
            CreatedAt = SeedTime,
            CreatedBy = (string?)SeedActor
        },
        new
        {
            Id = LegacyPayProviderId,
            Code = "LEGACYPAY",
            Name = "LegacyPay",
            BaseUrl = (string?)null,
            IsActive = false,
            _supportedCurrencies = "TRY",
            CreatedAt = SeedTime,
            CreatedBy = (string?)SeedActor
        }
    ];

    internal static object[] Users() =>
    [
        new
        {
            Id = AdminUserId,
            Email = "admin@test.com",
            PasswordHash = "AQAAAAIAAYagAAAAEAxnisD7T7uEPFZgtu04VY72aHHiyruvimt6dhDSL9o91Dy47a1CMw3VeMVIrvX8uA==",
            Role = Role.Admin,
            CreatedAt = SeedTime,
            CreatedBy = (string?)SeedActor
        },
        new
        {
            Id = UserId,
            Email = "user@test.com",
            PasswordHash = "AQAAAAIAAYagAAAAEOWP+IsJynXQbZPfCYDGrINR8oKmvNd947oH3BTURcX4T5t+aUQbRkoyt2DjHInAHQ==",
            Role = Role.User,
            CreatedAt = SeedTime,
            CreatedBy = (string?)SeedActor
        },
        new
        {
            Id = User2Id,
            Email = "user2@test.com",
            PasswordHash = "AQAAAAIAAYagAAAAEMnzf6hNCbGlPd156afZjh5Y/FcMA9tyvu2xlAZczceE1iLjMPzSBNnpjLmRvdG6sg==",
            Role = Role.User,
            CreatedAt = SeedTime,
            CreatedBy = (string?)SeedActor
        }
    ];
}
