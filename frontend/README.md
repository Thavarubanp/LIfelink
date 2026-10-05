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

## Donor dashboard and public requests

The donor dashboard derives every statistic from existing authenticated API responses. Acceptance metrics use `/Acceptances/my`: Active Acceptances count `Accepted`, `ScreeningPending`, `ScreeningCompleted`, and `Verified`; Successful Donations count `Matched`; Rejected by Doctor counts `Rejected`; and Withdrawn / Closed counts `Cancelled`. Created-request metrics use `/BloodRequests/my`: Requests Posted counts every returned record, Open Requests count `Pending`, `Verified`, and `Approved`, and the remaining cards count `Completed`, `Rejected`, and `Cancelled` respectively. Creator-deleted requests are not returned by that endpoint and are therefore excluded.

Available public requests are loaded before an empty state is shown. For donor/patient accounts, request IDs from non-cancelled acceptance records are marked Already Accepted and are excluded from dashboard opportunities; a cancelled acceptance does not prevent a later acceptance because the backend permits that transition. Admin and other non-donor roles may view public request details but are not shown donation or acceptance actions. Admin retains the separately authorized Create Blood Request route.

See [docs/README.md](../docs/README.md#frontend) for the complete route and page map.

