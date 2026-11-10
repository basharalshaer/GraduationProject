# Tafaqqad (تفقّد) – Backend API

Tafaqqad is a property-inspection platform. A person who wants to buy an apartment, villa, commercial unit or land books an inspection from a licensed engineering office. The office assigns one of its engineers, the engineer inspects the property and uploads photos and notes, and the client receives a report with everything that needs repair.

This repository is the **ASP.NET Core Web API**. The React frontend (Vite, Arabic, right-to-left) talks to it over JSON.

---

## Contents

1. [Tech stack](#tech-stack)
2. [Run it on your machine](#run-it-on-your-machine)
3. [Configuration](#configuration)
4. [How the system works](#how-the-system-works)
5. [API reference](#api-reference)
6. [Errors, limits and security](#errors-limits-and-security)
7. [Project structure](#project-structure)
8. [Troubleshooting](#troubleshooting)
9. [Team](#team)

---

## Tech stack

| Part | Technology |
|---|---|
| Framework | .NET 10, ASP.NET Core Web API (controllers) |
| Database | SQL Server LocalDB, Entity Framework Core (code-first migrations) |
| Auth | JWT Bearer tokens, roles: `Client`, `Office`, `Engineer`, `Admin` |
| Passwords | `PasswordHasher<User>` (hashing only, not full Identity) |
| API docs | Swagger UI (Swashbuckle), with an **Authorize** button for the JWT |
| Email | `ConsoleEmailSender`: emails are printed to the log during development |
| Files | `LocalFileStorage`: uploads are saved in a private `Uploads/` folder |
| Frontend | React 19 + Vite + react-router (separate project) |

---

## Run it on your machine

### 1. Install the tools (once)

- **Visual Studio 2026** with the **ASP.NET and web development** workload. This also installs the **.NET 10 SDK** and **SQL Server LocalDB**.
- **Git**.
- **Node.js LTS**, only if you also run the frontend. It includes `npm`.

### 2. Get the code

```bash
git clone <repo-url>
cd GraduationProject
git checkout master
```

Open the solution (`.sln` / `.slnx`) in Visual Studio.

### 3. Create your User Secrets

Secrets are **never** committed. Each developer creates their own.

In Visual Studio: right-click the **Tafaqqad.Api** project → **Manage User Secrets**, and paste:

```json
{
  "Jwt": {
    "Key": "put-a-random-string-of-at-least-32-characters-here"
  },
  "Seed": {
    "AdminEmail": "admin@tafaqqad.local",
    "AdminPassword": "ChooseAStrongPassword1"
  }
}
```

- `Jwt:Key` must be **at least 32 characters**, or the app refuses to start.
- `Seed:AdminEmail` / `Seed:AdminPassword` create the **first admin account** the first time the app runs.
- Never share your `secrets.json`. Make your own values.

Command-line alternative (run inside the project folder):

```bash
dotnet user-secrets set "Jwt:Key" "put-a-random-string-of-at-least-32-characters-here"
dotnet user-secrets set "Seed:AdminEmail" "admin@tafaqqad.local"
dotnet user-secrets set "Seed:AdminPassword" "ChooseAStrongPassword1"
```

### 4. Create the database

In Visual Studio: **Tools → NuGet Package Manager → Package Manager Console**, then:

```powershell
Update-Database
```

This creates `TafaqqadDb` in LocalDB with all tables. When the app first starts, the seeder adds the admin account and the 5 report section types.

Command-line alternative: `dotnet ef database update` (needs `dotnet tool install --global dotnet-ef`).

### 5. Run the API

Press **Ctrl+F5** (or **F5** to debug) with the **https** profile.

- Swagger opens at **https://localhost:7281/swagger**.
- If the browser warns about the certificate, run once: `dotnet dev-certs https --trust`.
- To call protected endpoints in Swagger: `POST /api/auth/login`, copy the `token`, click **Authorize**, and paste it.

### 6. Run the frontend (optional)

In the frontend folder:

```bash
npm install
```

Create a `.env` file next to `package.json`:

```
VITE_API_URL=https://localhost:7281/api
```

Then:

```bash
npm run dev
```

Open **http://localhost:5173**. The port must be listed in `Cors:Origins` (see [Configuration](#configuration)). If Vite picks another port such as 5176, close other running dev servers and start again.

### 7. Where to find emails, codes and links

Email is not really sent during development. OTP codes, set-password links and reset links are **printed to the log**:

- **F5:** the Visual Studio **Output** window (show output from: *Tafaqqad.Api*).
- **Ctrl+F5:** the console window that opens with the app.

---

## Configuration

### `appsettings.json` (committed, no secrets)

| Key | Example | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | `Server=(localdb)\MSSQLLocalDB;Database=TafaqqadDb;...` | Database connection (Windows login, no password) |
| `Jwt:Issuer` | `Tafaqqad.Api` | Token issuer |
| `Jwt:Audience` | `Tafaqqad.Client` | Token audience |
| `Jwt:ExpiresMinutes` | `120` | Login token lifetime |
| `App:FrontendBaseUrl` | `http://localhost:5173` | Used to build the links in emails (`/set-password?token=...`) |
| `Cors:Origins` | `["http://localhost:5173", ...]` | Frontend addresses allowed to call the API |

### User Secrets (per developer, never committed)

| Key | Rule |
|---|---|
| `Jwt:Key` | At least 32 characters |
| `Seed:AdminEmail` | First admin's email |
| `Seed:AdminPassword` | First admin's password |

### Startup checks

The app **refuses to start** with a clear message if `Jwt:Key` is missing or too short, or if `Jwt:Issuer`, `Jwt:Audience`, `ConnectionStrings:Default`, `App:FrontendBaseUrl` or `Cors:Origins` is missing.

> User Secrets load **only in the Development environment**. On a real server, set the same keys as environment variables, using a double underscore: `Jwt__Key`, `Seed__AdminEmail`, and so on.

---

## How the system works

### Accounts

| Role | How the account is created |
|---|---|
| **Client** | Registers alone, then verifies the email with a **6-digit code** (OTP, 10 minutes, 5 tries, 60-second resend cooldown) |
| **Office** | Fills a **public request form** with license and commercial-register files and gets a reference number like `REQ-2026-0001`. The **admin** approves it, rejects it with a reason, or asks for more information. On approval the office receives a **set-password link** (48 hours) |
| **Engineer** | Created by **their office**, and receives a set-password link (48 hours) |
| **Admin** | The first admin comes from the seeder (User Secrets) |

**Login order:** a wrong or missing password returns **401**, then a suspended account returns **403**, then an account that isn't activated returns **403** with `code: "EMAIL_NOT_VERIFIED"` (client) or `code: "NOT_ACTIVATED"` (office or engineer).

### Office status

| Status | Visible to clients? | Meaning |
|---|---|---|
| `Incomplete` | No | Just approved, or missing something |
| `Active` | **Yes** | Has at least 1 active engineer, at least 1 price, a daily capacity above 0, and at least 1 open working day |
| `Suspended` | No | Suspended by the admin. The office can log in and view its data, but every change returns 403 |

The status switches between `Incomplete` and `Active` **automatically** after every change to prices, schedule or engineers (`OfficeStatusService`). Only the admin moves an office into or out of `Suspended`. Reactivating runs the same rule, so the office goes back to Active only if it is complete.

### Audit log

Every important change writes an `AuditLog` row in the **same database save** as the change: request approved or rejected, prices, capacity, engineers added or deactivated, the office going live, suspensions. The admin can search it, and it can't be edited or deleted.

### Planned (Person B)

Bookings, price approval and payment, the inspection report (5 weighted sections with photos, an AI-assisted summary), and reviews. The tables already exist; the endpoints are in progress.

---

## API reference

Base URL: `https://localhost:7281/api`. JSON uses camelCase. Enum values are sent and returned as **strings** (for example `"Apartment"`).

**Property types:** `Apartment`, `Villa`, `Commercial`, `Land`.

### Auth – `api/auth`

| Method | Route | Who | Purpose |
|---|---|---|---|
| POST | `/register` | public | Client sign-up; emails an OTP |
| POST | `/verify-otp` | public | `{ email, code }` activates and logs in the client |
| POST | `/resend-otp` | public | Sends a new code (always 200) |
| POST | `/login` | public | `{ email, password }` returns `{ token, user }` |
| GET | `/me` | logged in | The current user |
| POST | `/set-password` | public (token) | Office or engineer activation |
| POST | `/forgot-password` | public | Emails a 1-hour reset link (always 200) |
| POST | `/reset-password` | public (token) | Sets a new password |

### Office account requests – `api/office-requests` (public)

| Method | Route | Purpose |
|---|---|---|
| POST | `/` | Submit the request (**multipart**: fields and `LicenseFile`, `CommercialRegFile`, optional `LogoFile`; JPG, PNG or PDF, max 5 MB, checked by magic bytes). Returns `{ referenceNo }` |
| POST | `/track` | `{ referenceNo, email }` returns the status and the admin's message |
| POST | `/respond` | Answer an info request with a new description or files |

### Admin – request review – `api/admin/office-requests` (Admin)

| Method | Route | Purpose |
|---|---|---|
| GET | `/?status=&search=&page=&pageSize=` | Request queue with counts per status |
| GET | `/{id}` | Details, documents, automatic duplicate checks |
| GET | `/{id}/documents/{docId}` | Download a document |
| POST | `/{id}/approve` | Creates the office user, the office and the set-password link |
| POST | `/{id}/resend-activation` | A new 48-hour link |
| POST | `/{id}/reject` | `{ reason }` |
| POST | `/{id}/request-info` | `{ message }` |

### Admin – offices – `api/admin/offices` (Admin)

| Method | Route | Purpose |
|---|---|---|
| GET | `/?status=&search=&page=&pageSize=` | All offices (any status) with `statusCounts` |
| GET | `/{id}` | Owner, completeness checklist, engineers, last 20 audit rows |
| POST | `/{id}/suspend` | `{ reason }` (5–500 characters); hides the office and emails the owner |
| POST | `/{id}/reactivate` | Lifts the suspension; Active again only if complete |

### Admin – audit log – `api/admin/audit-logs` (Admin)

| Method | Route | Purpose |
|---|---|---|
| GET | `/?officeId=&action=&actor=&search=&from=&to=&page=&pageSize=` | Search all audit rows, newest first (`to` includes the whole day) |
| GET | `/actions` | The action names, for a dropdown |

### Office portal – `api/office/...` (Office)

The office is always taken from the logged-in user's token, never from the request.

| Method | Route | Purpose |
|---|---|---|
| GET / PUT | `/profile` | Name, city, phone, description, plus the completeness checklist |
| GET / PUT | `/prices` | The whole price list (PUT replaces it) |
| GET / PUT | `/schedule` | Daily capacity (1–50) and all 7 working days |
| GET / POST | `/engineers` | List engineers / create one (emails a set-password link) |
| PUT | `/engineers/{id}` | Edit an engineer |
| POST | `/engineers/{id}/deactivate` | Deactivate an engineer (they can't log in) |
| POST | `/engineers/{id}/reactivate` | Reactivate an engineer |
| POST | `/engineers/{id}/resend-activation` | A new set-password link |
| GET | `/dashboard` | Status, live or not, checklist, counts |
| GET | `/activity` | The office's last 20 audit rows |

### Public offices – `api/offices` (no login)

Only **Active** offices are returned. An Incomplete, Suspended or unknown office gives **404**.

| Method | Route | Purpose |
|---|---|---|
| GET | `/?city=&propertyType=&maxPrice=&minRating=&search=&sort=rating\|price\|newest&page=&pageSize=` | Office list with `total` and `cities` |
| GET | `/{id}` | Details: description, prices, working hours (week starts Saturday), engineer count |
| GET | `/{id}/logo` | The logo image (PNG or JPG only) |
| GET | `/compare?ids=id1,id2,...` | Compare 2–4 offices side by side; unavailable ones are listed in `missingIds` |

---

## Errors, limits and security

### Error format

Every error has a JSON body the frontend can read:

```json
{ "message": "Human-readable reason", "traceId": "00-..." }
```

- Some errors add a `code` (for example `EMAIL_NOT_VERIFIED`, `NOT_ACTIVATED`).
- Validation errors use the standard ASP.NET `errors` object.
- Unexpected errors return **500** with a generic message. Full details appear only in Development (`detail`), and every error is logged with its `traceId`.

| Status | Meaning |
|---|---|
| 400 | Invalid input |
| 401 | Not logged in, or a wrong email or password |
| 403 | Logged in but not allowed (wrong role, suspended, not activated) |
| 404 | Not found (or hidden, for offices that aren't Active) |
| 409 | Conflicts with existing data (duplicate, wrong state) |
| 413 | Request too large |
| 429 | Too many requests; see `retryAfterSeconds` and the `Retry-After` header |
| 500 | Server error |

### Rate limits (per IP address)

| Policy | Limit | Endpoints |
|---|---|---|
| `login` | 10 per 5 minutes | login, verify OTP, set password, reset password |
| `send-code` | 5 per 15 minutes | register, resend OTP, forgot password, resend engineer activation |
| `public-form` | 5 per hour | office request form, respond, track |
| global | 300 per minute | everything |

Counters are kept in memory; restarting the API resets them.

### Security measures

- Passwords are hashed; email-link tokens are stored only as SHA-256 hashes and are single-use. OTP codes are stored as HMAC-SHA256.
- Uploads are checked by **magic bytes**, stored outside `wwwroot`, and served only through `ResolvePath`, which blocks path traversal.
- Offices only ever see their own data (the office id comes from the JWT).
- Secrets live in User Secrets or environment variables, never in the repo.
- Security headers on every response: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, a strict CSP on JSON, and HSTS outside Development.
- Swagger is enabled in Development only.

---

## Project structure

```
Controllers/
  Accounts/AuthController.cs
  Admin/AdminOfficeRequestsController.cs, AdminOfficesController.cs, AdminAuditLogsController.cs
  Offices/OfficeApiController.cs (base), OfficeProfile/Prices/Schedule/Engineers/Dashboard controllers,
          OfficeRequestsController.cs
  Public/OfficesController.cs
Data/            AppDbContext.cs (tables and rules), DbSeeder.cs
Dtos/            request and response shapes
Infrastructure/  GlobalExceptionHandler.cs
Migrations/      EF Core migrations
Models/Entities  one class per table
Models/Enums     all enums
Services/        JWT, email-link tokens, OTP, email sender, file storage, OfficeStatusService
Program.cs       configuration checks, services, middleware pipeline
```

---

## Troubleshooting

| Problem | Fix |
|---|---|
| The app won't start: *"Tafaqqad cannot start because of configuration problems"* | Read the list in the message. Usually your User Secrets are missing or `Jwt:Key` is shorter than 32 characters |
| The frontend shows *"تعذّر الاتصال بالخادم"* | Is the API running? Is the frontend port in `Cors:Origins`? Close extra `npm run dev` terminals so Vite uses 5173 |
| 401 on every request | The token expired (120 minutes) or was made with a different `Jwt:Key`. Log in again |
| 429 Too Many Requests | Wait `retryAfterSeconds`, or restart the API during development |
| `Cannot open database "TafaqqadDb"` | Run `Update-Database` |
| `'vite' is not recognized` | Run `npm install` in the frontend folder. If the project is inside OneDrive, move it out and reinstall |
| `npm` is not recognized | Install Node.js LTS and reopen the terminal |
| Visual Studio stops on an exception while testing | That's the debugger. Press F5 to continue, or run with Ctrl+F5 |

---

## Team

| Member | Area |
|---|---|
| Mahmoud (backend A) | Accounts and roles, office onboarding, admin tools, office management, public offices, hardening |
| Backend B | Bookings, payments, inspection reports, AI report, reviews |
| Frontend team | React pages and API linking |

*Graduation project, 2026.*
