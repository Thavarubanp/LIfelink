# LIFELINK – Intelligent Blood Donation & Emergency Blood Coordination Platform

LIFELINK is an ASP.NET Core backend Web API and React frontend application designed for intelligent blood donation management and emergency coordination, supported by AI agents coordinated by a LangGraph Supervisor (see [AI Agents](#ai-agents)).

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
  },
  "Jwt": {
    "Key": "LifeLink_Super_Secret_Jwt_Signing_Key_2026_For_Development_Only_Must_Be_Long!",
    "Issuer": "LifeLinkAPI",
    "Audience": "LifeLinkApp",
    "ExpiryMinutes": 120
  }
}
```

### 3. Run Database Migrations
Migrations are managed via EF Core and connect to Neon PostgreSQL. The backend applies pending migrations
automatically at startup. To apply them manually instead:

```bash
dotnet ef database update --project backend/backend.csproj --startup-project backend/backend.csproj
```

> The Neon database may be shared by teammates. Before starting a branch that adds a migration, check with the team
> that nobody is working against the same database on an older branch.

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

### 6. Run the Frontend
```bash
cd frontend
npm install
npm run dev        # Vite proxies /api to http://127.0.0.1:5231
```

### 7. Demo Stock (Development only, optional)
```bash
dotnet run --project backend/backend.csproj -- --seed-demo-data
```

This creates audited demo blood packets for approved hospitals that have none yet, then exits. It refuses to run
outside the Development environment. Real stock only comes from recorded donations and completed hospital
transfers.

---

## AI Agents

The backend talks to one agent, the **Supervisor**. The Supervisor routes work to three worker agents. All agents
bind to `127.0.0.1` and require the `X-Internal-Key` header, which must equal the backend's `InternalService:ApiKey`.
Gemini keys are optional: without one, every agent falls back to rules and templates.

| Agent | Folder | Port | Role |
|---|---|---|---|
| Supervisor | `Agents/Supervisor` | 8004 | Routes events and assistant chat. Planning and the knowledge base (RAG) run inside it |
| Request Management | `Agents/RequestManagement` | 8001 | Donor screening interview and versioned screening reports |
| Notification | `Agents/Notification` | 8000 | Donor and hospital alerts |
| Inventory | `Agents/InventoryManagement` | 8003 | Shortage, expiry, emergency and transfer recommendations |

In each agent folder, run `pip install -r requirements.txt`, copy `.env.example` to `.env`, then start it:

```bash
cd Agents/Supervisor && python app.py
cd Agents/RequestManagement && python main.py
cd Agents/Notification && python app.py
cd Agents/InventoryManagement && uvicorn api.app:app --host 127.0.0.1 --port 8003
```

### Internal service key

The key committed in `backend/appsettings.json` and the agents' `.env.example` files
(`LifeLink-Internal-Agent-Key-2026`) is a **local-development default**. Anyone with the repository knows it. With
the key, a caller can read donor screening profiles and submit screening reports, so treat it like a password.

**Shared and production environments must override it** with their own secret. Use the same value in the backend
and all four agents, and never commit it:

- **Backend:** set the environment variable `InternalService__ApiKey`; this works in every environment.
- **Developer machine:** user-secrets also work, but only in the Development environment. Run
  `dotnet user-secrets init` once in `backend/` (this adds a `UserSecretsId` to `backend.csproj`), then
  `dotnet user-secrets set "InternalService:ApiKey" "<value>"`.
- **Agents:** set the environment variable `INTERNAL_SERVICE_API_KEY`, or put it in the agent's git-ignored `.env`
  file.

Each agent's README covers its endpoints, configuration and tests. Two one-off steps:

- **Supervisor knowledge base:** embedded automatically on startup when a Gemini key is set. To rebuild it
  manually, run `python -m knowledge.ingest --rebuild`.
- **Request Management database:** a local `screening_agent.db` created before report versioning needs
  `alembic upgrade head`. See that agent's README.

The AI never approves or rejects donors, verifies donations or makes medical decisions. Those actions stay with the
hospital's doctors and authorised staff.

---

## Technical Documentation
For full details on authentication architecture, roles, API endpoints, DTO contracts, and instructions for future student components, see:
[docs/authentication-authorization.md](docs/authentication-authorization.md)
