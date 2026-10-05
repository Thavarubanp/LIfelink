# LifeLink web application

The web client is a React 19 single-page application. It calls only the ASP.NET Core API; browsers never call the Python agents directly.

## Setup and commands

```powershell
cd frontend
npm install
npm run dev
```

Other commands:

```powershell
npm run build
npm run lint
npm run preview
```

`VITE_API_BASE_URL` optionally overrides the default `/api`. During local development Vite proxies `/api` to the backend.

## Structure

| Path | Purpose |
|---|---|
| `src/api/` | Axios client and domain API modules |
| `src/pages/` | Role-oriented auth, donor, hospital, doctor, admin, governance, and profile pages |
| `src/components/` | Shared layout, tables, dialogs, workflow, assistant, screening, and notification UI |
| `src/context/` | Authentication, toast notifications, theme, and admin-attention state |
| `src/session/` | Cross-tab session activity and idempotency-key helpers |
| `src/utils/` | Role, error, and file utilities |

Routes live in `src/App.jsx`. `ProtectedRoute` sends unauthenticated users to login, suspended accounts to governance, unapproved hospitals to the waiting page, doctors with a temporary password to password change, and wrong-role users to their own dashboard.

`AuthContext` owns the authenticated user. The API client attaches the JWT, uses `X-LifeLink-Activity` only for user activity, handles ended sessions, and supports `Idempotency-Key` for protected create operations. `NotificationContext` provides toasts; theme state follows the system initially and can be changed for the current session.

See [docs/README.md](../docs/README.md#frontend) for the complete route and page map.

