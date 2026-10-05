# LifeLink

LifeLink is a university group project for coordinating blood requests, donor screening, hospital blood inventory, transfers, emergencies, and account governance in Sri Lanka. Human hospital staff and doctors make every operational and clinical decision; the AI agents organise information and produce recommendations or notification text.

## Components

| Component | Purpose | Technology |
|---|---|---|
| `backend/` | The only public API and system of record | ASP.NET Core 8, EF Core 8, PostgreSQL |
| `frontend/` | Browser application | React 19, Vite 8, React Router, Axios |
| `mobile/` | Android application | Flutter, Riverpod, `go_router`, Dio |
| `Agents/` | Internal orchestration, screening, alerts, and inventory analysis | Python, FastAPI, LangGraph, Gemini |

Roles are `User` (donor/patient), `HospitalStaff`, `Doctor`, and `Admin`. The four student areas are donor/request workflows, doctor/verification workflows, inventory/transfers, and administration/governance.

## Quick start

Install .NET 8, Node.js/npm, Python 3.11+, Flutter for mobile work, and access to PostgreSQL. Configure local values with environment variables or ignored development files; never commit credentials.

Start services in this order:

1. PostgreSQL/Neon.
2. Request Management (`8001`), Notification (`8000`), and Inventory Management (`8003`).
3. Supervisor (`8004`).
4. Backend API (`5231`).
5. React (`5173`) and/or the Flutter app.

Component commands and configuration names are documented in the component READMEs.

## Documentation

- [Complete technical documentation](docs/README.md)
- [Backend setup](backend/README.md)
- [Frontend setup](frontend/README.md)
- [Mobile setup](mobile/README.md)
- [Agent architecture](Agents/README.md)
- [Repository fact report](docs/report-facts.md)

