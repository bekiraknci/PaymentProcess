# PaymentSystem

Payment processing and compliance API built with ASP.NET Core (.NET 10), EF Core 10 and SQL Server, following Clean Architecture and DDD.

```
src/
  PaymentSystem.Domain/          Entities, value objects, domain rules (no dependencies)
  PaymentSystem.Application/     Use-case handlers, validation, abstractions
  PaymentSystem.Infrastructure/  EF Core, migrations, JWT, password hashing
  PaymentSystem.Api/             Controllers, authentication/authorization, Swagger
tests/
  PaymentSystem.Domain.Tests/    Unit tests for the domain model
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server **LocalDB** (installed with Visual Studio's *ASP.NET and web development* workload) or any SQL Server instance
- Optional: Visual Studio 2026 (required for .NET 10 projects and IIS Express debugging)

## Getting started

```powershell
git clone https://github.com/bekiraknci/PaymentProcess.git
cd PaymentProcess
dotnet tool restore
```

The database is created automatically. In the `Development` environment the API applies all EF Core migrations, including the seed data, on startup.

### Visual Studio (IIS Express)

1. Open `PaymentSystem.slnx`.
2. Set `PaymentSystem.Api` as the startup project and select the **IIS Express** profile.
3. Press **F5**. Swagger opens at `http://localhost:5000/swagger`.

### Command line

```powershell
dotnet run --project src/PaymentSystem.Api --launch-profile http
```

Then open `http://localhost:5000/swagger`.

### Tests

```powershell
dotnet test
```

## Database

The default connection string targets LocalDB with Windows authentication:

```
Server=(localdb)\MSSQLLocalDB;Database=PaymentSystem;Trusted_Connection=True;TrustServerCertificate=True
```

To use a different SQL Server instance or SQL authentication, override it locally with user secrets. Do not put credentials in `appsettings.json`.

```powershell
dotnet user-secrets set "ConnectionStrings:PaymentSystem" "Server=.;Database=PaymentSystem;User Id=<user>;Password=<password>;TrustServerCertificate=True" --project src/PaymentSystem.Api
```

You can also set the `ConnectionStrings__PaymentSystem` environment variable.

Migrations can be applied manually as well:

```powershell
dotnet ef database update -p src/PaymentSystem.Infrastructure -s src/PaymentSystem.Api
```

## Test accounts

| Email | Password | Role | Permissions |
|---|---|---|---|
| `admin@test.com` | `Admin123!` | Admin | `Payments.Create`, `Payments.View`, `Payments.Process`, `Providers.Manage` |
| `user@test.com` | `User123!` | User | `Payments.Create`, `Payments.View` |
| `user2@test.com` | `User123!` | User | `Payments.Create`, `Payments.View` |

Calling `POST /api/auth/login` from Swagger authorizes the UI with the returned token automatically. To call an endpoint without a token, use **Authorize → Logout**.

Seeded providers: `FAKEPAY` (active, EUR/GBP/TRY/USD) and `LEGACYPAY` (inactive, TRY).

## Configuration

| Key | Description |
|---|---|
| `ConnectionStrings:PaymentSystem` | SQL Server connection string |
| `Jwt:Issuer`, `Jwt:Audience` | Token issuer and audience |
| `Jwt:SigningKey` | HMAC signing key, at least 32 characters. The development key is in `appsettings.Development.json`; provide a real secret in other environments. |
| `Jwt:AccessTokenLifetimeMinutes` | Access token lifetime |
| `Database:MigrateOnStartup` | Apply migrations on startup (enabled in Development) |
