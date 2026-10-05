# LifeLink backend

The backend is the only public API. It applies authentication, authorization, validation, concurrency protection, persistence, background work, and the human approval rules used by both clients and all agents.

## Prerequisites and configuration

- .NET 8 SDK
- PostgreSQL (the deployed database is Neon)
- `dotnet-ef` for creating or inspecting migrations

Use environment variables or .NET user-secrets for local values. Important names are:

| Area | Names |
|---|---|
| Database | `ConnectionStrings__DefaultConnection` |
| JWT | `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiryMinutes` |
| Sessions | `Session__IdleTimeoutMinutes`, `Session__WarningMinutes` |
| Agents | `InternalService__ApiKey`, `PlanningAgent__BaseUrl`, `NotificationAgent__BaseUrl`, `ScreeningAgent__BaseUrl` |
| Inventory monitor | `InventoryMonitoring__Enabled`, `InventoryMonitoring__IntervalMinutes` |
| Email | `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`, `Smtp__FromName`, `Smtp__FromEmail` |
| Swagger | `Swagger__Enabled` |

Never commit values for keys, passwords, tokens, or connection strings.

## Run and migrate

```powershell
cd backend
dotnet restore
dotnet run --launch-profile http
```

The application calls `Database.Migrate()` at startup. To manage migrations explicitly:

```powershell
dotnet ef database update
dotnet ef migrations list
dotnet ef migrations add <MigrationName>
```

Development-only packet seeding:

```powershell
dotnet run -- --seed-demo-data
```

The seeder refuses to run outside Development.

## Tests

```powershell
dotnet test ../backend.Tests/backend.Tests.csproj
```

PostgreSQL-specific race tests run only when `LIFELINK_PG_TESTS` identifies an appsettings file containing the test database configuration. They execute in rolled-back transactions.

## Structure

| Path | Responsibility |
|---|---|
| `Controllers/` | Routes, role attributes, HTTP responses, caller scoping |
| `DTOs/` | Request/response contracts and annotation validation |
| `Services/` | Business workflows and authorization rules |
| `Entities/` | EF Core entities and status constants |
| `Data/AppDbcontext.cs` | Mappings, indexes, constraints, seed roles, concurrency/report guards |
| `Middleware/` | Exceptions, internal-agent authentication, restricted governance mode |
| `Migrations/` | Ordered EF Core schema/data migrations |
| `Common/` | Shared validation, idempotency, conflict, and attachment helpers |

See the [complete technical documentation](../docs/README.md) for every endpoint, validation rule, workflow, and database relationship.

