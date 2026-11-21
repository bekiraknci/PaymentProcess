# PaymentSystem

Payment processing and compliance API built with ASP.NET Core (.NET 10), EF Core 10 and SQL Server, following Clean Architecture and DDD.

```
src/
  PaymentSystem.Domain/          Entities, value objects, domain rules (no dependencies)
  PaymentSystem.Application/     Use-case handlers, validation, abstractions
  PaymentSystem.Infrastructure/  EF Core, migrations, JWT, password hashing
  PaymentSystem.Api/             Controllers, authentication/authorization, Swagger
  FakePaymentProvider.Api/       Stand-alone simulated payment provider ("Service B")
tests/
  PaymentSystem.Domain.Tests/       Unit tests for the domain model
  PaymentSystem.Application.Tests/  Use-case tests (idempotency, processing, retry)
```

Payments are accepted synchronously as `Pending` and processed asynchronously. A background worker claims each payment, calls the provider through a resilient typed `HttpClient` and marks it `Completed` or `Failed`.

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

Two services run side by side:

| Service | URL |
|---|---|
| PaymentSystem API (Swagger) | `http://localhost:5000/swagger` |
| FakePaymentProvider | `http://localhost:5100` |

### Visual Studio (IIS Express)

1. Open `PaymentSystem.slnx`.
2. In the startup-project dropdown, select the **Api + FakePaymentProvider** profile. The profile is defined in `PaymentSystem.slnLaunch`; it starts the API on IIS Express and the fake provider on Kestrel.
3. Press **F5**. Swagger opens at `http://localhost:5000/swagger`.

### Command line

Run each command in its own terminal:

```powershell
dotnet run --project src/FakePaymentProvider.Api --launch-profile http
```

```powershell
dotnet run --project src/PaymentSystem.Api --launch-profile http
```

If the fake provider is not running, payments are still accepted. They end up `Failed` with "Provider is unreachable".

### Tests

```powershell
dotnet test
```

| Project | Scope |
|---|---|
| `PaymentSystem.Domain.Tests` | Value objects and payment state machine |
| `PaymentSystem.Application.Tests` | Use cases with in-memory fakes |
| `PaymentSystem.Api.IntegrationTests` | Full HTTP pipeline against a real SQL Server: functional, security, database (schema, constraints, query plans) and load/concurrency tests |

Integration tests create a throw-away database named `PaymentSystem_IT_<guid>` and drop it afterwards. They never touch the application database: startup aborts if the app is not pointed at the test database. By default they use LocalDB. To run them against another SQL Server (for example in CI), set `PAYMENTSYSTEM_TEST_SQLSERVER`, e.g. `localhost,1433`.

### Health checks

| Endpoint | Purpose |
|---|---|
| `GET /health/live` | Process is up (no dependencies checked) |
| `GET /health/ready` | Database is reachable |

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

Seeded providers:

| Code | Id | State |
|---|---|---|
| `FAKEPAY` | `0196a000-0000-7000-8000-000000000001` | active, EUR/GBP/TRY/USD |
| `LEGACYPAY` | `0196a000-0000-7000-8000-000000000002` | inactive, TRY |

## API overview

| Method | Route | Permission | Notes |
|---|---|---|---|
| POST | `/api/auth/login` | anonymous | Returns a JWT |
| POST | `/api/payments` | `Payments.Create` | 201; optional `Idempotency-Key` header |
| GET | `/api/payments/{id}` | `Payments.View` | 404 for another user's payment |
| GET | `/api/payments?status=&page=&pageSize=` | `Payments.View` | Users see their own, admins see all |
| GET | `/api/payments/{id}/history` | `Payments.View` | Status transitions |
| POST | `/api/payments/{id}/retry` | `Payments.Process` | 202 for `Failed`, 409 otherwise |
| GET/POST | `/api/providers` | `Providers.Manage` | List / create |
| GET/PUT | `/api/providers/{id}` | `Providers.Manage` | Get / update |
| PATCH | `/api/providers/{id}/activate`, `/deactivate` | `Providers.Manage` | 204 |

All errors are returned as `application/problem+json`.

### Idempotency

When `POST /api/payments` carries an `Idempotency-Key` header (scoped per user):

- The same key with the same body returns the original payment with `201` and `Idempotent-Replayed: true`.
- The same key with a different body returns `409`.
- Concurrent duplicates are resolved by the unique index `UX_Payments_UserId_IdempotencyKey`.

The worker sends the payment id as the provider's idempotency key, so a payment is never charged twice.

### Deterministic provider outcomes

| Amount | Outcome |
|---|---|
| `666` | Always declined |
| `503` | Provider outage (HTTP 503, retried, then `Failed`) |
| `999` | Provider timeout (retried, then `Failed`) |
| anything else | Approved, with random declines, outages and timeouts as configured under `Scenarios` in `src/FakePaymentProvider.Api/appsettings.json` |

## Configuration

| Key | Description |
|---|---|
| `ConnectionStrings:PaymentSystem` | SQL Server connection string |
| `Jwt:Issuer`, `Jwt:Audience` | Token issuer and audience |
| `Jwt:SigningKey` | HMAC signing key, at least 32 characters. The development key is in `appsettings.Development.json`; provide a real secret in other environments. |
| `Jwt:AccessTokenLifetimeMinutes` | Access token lifetime |
| `Database:MigrateOnStartup` | Apply migrations on startup (enabled in Development) |
| `PaymentProcessing:WorkerCount` | Number of concurrent queue consumers |
| `PaymentProcessing:LeaseSeconds` | Processing lease; payments stuck longer than this are recovered |
| `PaymentProcessing:PollIntervalSeconds` | Safety-net database poll interval for pending payments |
| `PaymentProcessing:MaxAutomaticAttempts` | Attempts after which no automatic retry is scheduled |
| `PaymentProcessing:RetryBaseDelayMinutes` | Base for the exponential retry delay (`base × 2^attempt` minutes) |
| `PaymentProvider:BaseUrl` | Provider base URL |
| `PaymentProcessing:QueueCapacity` | In-memory queue capacity. When it is full, new payments are still persisted and the database poll picks them up |
| `PaymentProvider:AttemptTimeoutSeconds`, `TotalTimeoutSeconds`, `MaxRetryAttempts` | HTTP resilience settings |
| `RateLimiting:Enabled` | Turns all rate limits on or off |
| `RateLimiting:LoginPermitLimit`, `LoginWindowSeconds` | Login attempts per client IP per window (brute-force protection) |
| `RateLimiting:PaymentCreateBurst`, `PaymentCreatePerSecond` | Token bucket for `POST /api/payments` per user |
| `RateLimiting:GlobalBurst`, `GlobalPerSecond` | Token bucket for all requests per user (or per IP when anonymous) |

## Security hardening

- JWT validation pins `HS256` and requires signed, expiring tokens with a valid issuer and audience. Ownership is enforced in queries, so another user's payment returns 404.
- Rate limits answer `429` with `Retry-After` and a problem+json body.
- Request bodies are limited to 64 KB on both Kestrel and IIS.
- API responses send `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Cache-Control: no-store` and a restrictive CSP, and the `Server` header is removed. HSTS and HTTPS redirection are enabled outside Development.
- Error responses never include exception details, only a `traceId`.

## Indexing strategy

Payment ids are `Guid` v7. SQL Server sorts `uniqueidentifier` values by their last bytes, which are random in v7, so a clustered index on `Id` would scatter inserts across the table. Instead, the primary keys of `Payments` and `PaymentStatusHistory` are **non-clustered**, and the clustered index follows insertion time: `(CreatedAt, Id)` and `(ChangedAt, Id)`. New rows are appended at the end, and date-range scans (reports) read contiguous pages.

| Index | Serves |
|---|---|
| `CX_Payments_CreatedAt_Id` (clustered) | Admin listing newest-first, date-range reports |
| `UX_Payments_UserId_IdempotencyKey` (unique, filtered `IS NOT NULL`) | Idempotency lookup and duplicate protection under concurrency |
| `IX_Payments_UserId_CreatedAt` | A user's payment list, newest first |
| `IX_Payments_UserId_Status_CreatedAt` | A user's payment list filtered by status |
| `IX_Payments_Status_CreatedAt` | Pending pickup by the worker, admin list filtered by status |
| `IX_Payments_Status_NextAttemptAt` | Retry job: failed payments that are due |
| `IX_Payments_Processing_LockedUntil` (filtered `Status = 'Processing'`) | Recovery of expired processing leases; stays tiny |
| `IX_Payments_ProviderId_CreatedAt` INCLUDE (`Status`, `Amount`) | Provider/date reports without key lookups |
| `IX_PaymentStatusHistory_PaymentId_ChangedAt` | Payment history |

Check constraints (`Amount > 0`, three upper-case letters for the currency, a known status, non-negative attempts, a transaction id required for `Completed`) protect the data even from writes that bypass the domain. The integration tests verify the schema and assert, from execution plans, that every hot query uses an index seek.
