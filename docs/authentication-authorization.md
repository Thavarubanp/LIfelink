# LifeLink Core Authentication & Authorization Documentation

This document serves as the technical guide for the shared **Core Authentication and Authorization Foundation** in the LifeLink backend.

---

## 1. Overview & Architecture

Authentication and authorization form the **shared foundation** for all four student components of LifeLink. The system uses:
- **ASP.NET Core Web API** (.NET 8)
- **Entity Framework Core 8** with **Npgsql**
- **PostgreSQL / Neon PostgreSQL**
- **JWT Bearer Tokens** (HMAC-SHA256)
- **PBKDF2 Password Hashing** (`IPasswordHasher<User>`)
- **Crypto-Secure Hashed Reset Tokens** (SHA-256)

---

## 2. Database Schema

The database consists of the following 4 core tables:

### `Users` Table
- `UserId` (`uuid`, Primary Key)
- `FirstName` (`varchar(100)`, Required)
- `LastName` (`varchar(100)`, Required)
- `Email` (`varchar(256)`, Unique Index, Normalized)
- `PasswordHash` (`text`, Required)
- `PhoneNumber` (`varchar(20)`)
- `DateOfBirth` (`timestamp with time zone`)
- `Gender` (`varchar(20)`)
- `Address` (`varchar(500)`)
- `AccountStatus` (`text` - `Active`, `Pending`, `Suspended`, `Inactive`)
- `CreatedAt` (`timestamp with time zone`)
- `UpdatedAt` (`timestamp with time zone`)

### `Roles` Table
- `RoleId` (`int`, Primary Key)
- `Name` (`varchar(50)`, Unique Index)

Seeded data:
1. `User`
2. `HospitalStaff`
3. `Doctor`
4. `Admin`

### `UserRoles` Table (Join Table)
- `UserId` (`uuid`, Foreign Key → `Users`)
- `RoleId` (`int`, Foreign Key → `Roles`)
- Composite Primary Key (`UserId`, `RoleId`)

### `PasswordResetTokens` Table
- `PasswordResetTokenId` (`uuid`, Primary Key)
- `UserId` (`uuid`, Foreign Key → `Users`)
- `TokenHash` (`text`, Indexed) — *Raw token is NEVER stored in database*
- `ExpiresAt` (`timestamp with time zone`)
- `UsedAt` (`timestamp with time zone`, Nullable)
- `CreatedAt` (`timestamp with time zone`)

---

## 3. API Endpoints

All routes are under `/api/auth`:

| Method | Endpoint | Auth Required | Description |
|---|---|---|---|
| `POST` | `/api/auth/register` | No | Register new user (default role: `User`, status: `Active`) |
| `POST` | `/api/auth/login` | No | Authenticate user & return JWT token |
| `POST` | `/api/auth/logout` | No | Stateless logout signal for client token removal |
| `GET` | `/api/auth/me` | Yes (`Bearer`) | Return currently authenticated user profile & roles |
| `POST` | `/api/auth/forgot-password` | No | Trigger password reset link dispatch (anti-enumeration response) |
| `POST` | `/api/auth/reset-password` | No | Reset password using valid raw reset token |
| `POST` | `/api/auth/change-password` | Yes (`Bearer`) | Change password for logged-in user |

---

## 4. System Roles & Usage for Future Student Components

The system establishes 4 roles:
- `User`: Standard platform users (Student 1 target)
- `HospitalStaff`: Hospital & Blood Bank operators (Student 2 target)
- `Doctor`: Medical professionals (Student 1 eligibility target)
- `Admin`: System Administrators (Student 4 target)

### How Future Students Protect Endpoints

Use standard ASP.NET Core `[Authorize]` attributes on Controllers or Actions:

```csharp
// Require any authenticated user
[Authorize]
[HttpGet("my-donations")]
public IActionResult GetMyDonations() { ... }

// Require specific role
[Authorize(Roles = "HospitalStaff")]
[HttpPost("inventory/add")]
public IActionResult AddBloodInventory() { ... }

// Require multiple roles
[Authorize(Roles = "Doctor,Admin")]
[HttpPut("eligibility/review")]
public IActionResult ReviewEligibility() { ... }
```

### How Future Students Access Current Authenticated User Information

Inject `ICurrentUserService` into any controller or service:

```csharp
public class BloodRequestService
{
    private readonly ICurrentUserService _currentUserService;

    public BloodRequestService(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public void ProcessRequest()
    {
        Guid? currentUserId = _currentUserService.UserId;
        string? userEmail = _currentUserService.Email;
        IEnumerable<string> roles = _currentUserService.Roles;
        bool isLoggedIn = _currentUserService.IsAuthenticated;
    }
}
```

---

## 5. Security Decisions

1. **Password Protection**:
   - Uses ASP.NET Core Identity PBKDF2 with HMAC-SHA256 password hashing and salt.
   - Passwords and password hashes are **NEVER** returned in any DTO or log.

2. **Password Reset Token Security**:
   - Raw tokens are 32-byte cryptographically secure random strings.
   - Database stores ONLY the SHA-256 hash (`TokenHash`).
   - Tokens expire after 15 minutes and are single-use (`UsedAt`).
   - Requesting a new token invalidates previous unused tokens for the user.

3. **Anti-Enumeration Protection**:
   - `/api/auth/forgot-password` always returns: `"If an account exists for this email, a password reset link has been sent."` regardless of whether the email exists.

4. **Account Status Enforcement**:
   - Accounts with `AccountStatus = Suspended` or `Inactive` or `Pending` are blocked during login attempt.

5. **Client Role Elevation Prevention**:
   - Clients cannot specify role (e.g. `Admin`) during registration. Default is strictly `User`.

---

## 6. Environment & Configuration Variables

In production, set environment variables:
- `ConnectionStrings__DefaultConnection`: PostgreSQL / Neon Connection String
  `Host=...;Database=neondb;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true;`
- `Jwt__Key`: Min 32-character secret key.
- `Jwt__Issuer`: `LifeLinkAPI`
- `Jwt__Audience`: `LifeLinkApp`
- `Jwt__ExpiryMinutes`: `120`

---

## 7. Frontend Integration Contract (React & Flutter)

### Login Flow:
1. Client POSTs credentials to `POST /api/auth/login`.
2. Response contains:
   ```json
   {
     "success": true,
     "message": "Login successful.",
     "data": {
       "accessToken": "eyJhbGciOiJIUzI1Ni...",
       "expiresAt": "2026-09-08T22:00:00Z",
       "user": {
         "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
         "firstName": "John",
         "lastName": "Doe",
         "email": "john.doe@example.com",
         "roles": ["User"],
         "accountStatus": "Active"
       }
     }
   }
   ```
3. Client stores `accessToken` securely (e.g. `localStorage`/`SecureStorage`).
4. Client attaches `Authorization` header for protected endpoints:
   `Authorization: Bearer <accessToken>`

---

## 8. Swagger Testing

1. Run API (`dotnet run --project backend/backend.csproj`).
2. Navigate to `http://localhost:5xxx/swagger`.
3. Call `POST /api/auth/login` to obtain an `accessToken`.
4. Click **Authorize** at top right of Swagger.
5. Enter: `Bearer <your_access_token>`.
6. Test `GET /api/auth/me` or other protected endpoints.
