# LIFELINK – Intelligent Blood Donation & Emergency Blood Coordination Platform

LIFELINK is an ASP.NET Core backend Web API and React frontend application designed for intelligent blood donation management and emergency coordination.

## Shared Foundation: Core Authentication & Authorization

The project core authentication and role-based authorization foundation has been established as a shared baseline for all team modules.

### Technologies
- **ASP.NET Core Web API** (.NET 8)
- **Entity Framework Core 8** with **Npgsql**
- **Neon PostgreSQL**
- **JWT Bearer Token Authentication**
- **xUnit Test Suite**

---

## Quick Start & Running the API

### 1. Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- EF Core CLI (`dotnet tool install --global dotnet-ef`)

### 2. Configure Database & JWT
Ensure connection string and JWT key are configured in `backend/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  "Jwt": {
    "Key": "LifeLink_Super_Secret_Jwt_Signing_Key_2026_For_Development_Only_Must_Be_Long!",
    "Issuer": "LifeLinkAPI",
    "Audience": "LifeLinkApp",
    "ExpiryMinutes": 120
  }
}
```

### 3. Run Database Migrations
Migrations are managed via EF Core and connect to Neon PostgreSQL:

```bash
dotnet ef database update --project backend/backend.csproj --startup-project backend/backend.csproj
```

### 4. Build & Run Backend API
```bash
dotnet build backend/backend.csproj
dotnet run --project backend/backend.csproj
```

Explore API documentation and test authentication endpoints via Swagger UI at `http://localhost:5xxx/swagger`.

### 5. Run Unit Tests
```bash
dotnet test backend.Tests/backend.Tests.csproj
```

---

## Technical Documentation
For full details on authentication architecture, roles, API endpoints, DTO contracts, and instructions for future student components, see:
[docs/authentication-authorization.md](docs/authentication-authorization.md)
