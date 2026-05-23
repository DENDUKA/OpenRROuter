# Google OAuth — Design Spec

**Date:** 2026-05-23  
**Status:** Draft

## Context

OpenRussiaRouter currently supports only email/password login with invite-based registration. Users are requesting Google OAuth as an additional sign-in option. Goal: any Google account can sign in — a new user account (role=User) is created automatically on first login, and if an email already exists (invited user) the Google identity is linked to it.

Other OAuth providers may be added in the future (GitHub, etc.), so the design must be extensible.

---

## Architecture

### Chosen approach: `auth.external_logins` linking table

A universal table mapping `(provider, provider_sub)` → `user_id`. Adding a new provider later requires no schema changes.

The overall flow remains the same as email/password:
1. WebApp handles the OAuth callback (ASP.NET Core `AddGoogle` middleware)
2. WebApp calls AuthService via gRPC with `(google_sub, email)`
3. AuthService creates/links the user, returns a session token (same `LoginResponse`)
4. WebApp signs the cookie — identical to existing email/password path

---

## 1. Database Migration

**File:** `db/migrations/auth/005_add_external_logins.sql`

```sql
-- Google-only users have no password
ALTER TABLE auth.users ALTER COLUMN password_hash DROP NOT NULL;

CREATE TABLE auth.external_logins (
    id             VARCHAR(64)  PRIMARY KEY,
    user_id        VARCHAR(64)  NOT NULL REFERENCES auth.users(id),
    provider       VARCHAR(64)  NOT NULL,    -- 'google', 'github', etc.
    provider_sub   VARCHAR(256) NOT NULL,    -- provider's unique user identifier
    created_at_utc TIMESTAMPTZ  NOT NULL
);

CREATE UNIQUE INDEX ux_auth_external_logins_provider_sub
    ON auth.external_logins(provider, provider_sub);
CREATE INDEX ix_auth_external_logins_user_id
    ON auth.external_logins(user_id);
```

Account resolution logic in AuthService (ordered):
1. Find by `(provider='google', provider_sub)` → existing Google user, create session
2. Not found → find by email → existing invited user → add `external_logins` row, create session (keeps role)
3. Not found → create new `UserAccount` (role=User, `password_hash=NULL`) + `external_logins` row transactionally, create session

---

## 2. AuthService — Domain & Repository

**New entity** `src/OpenRussiaRouter.AuthService/Domain/Entities/ExternalLogin.cs`:
```csharp
public sealed record ExternalLogin(
    string Id, string UserId, string Provider,
    string ProviderSub, DateTimeOffset CreatedAtUtc);
```

**`UserAccount`** — change `PasswordHash` from `string` to `string?` (nullable).  
Add factory: `UserAccount.CreateForExternalLogin(email, role, createdAt)` (no password hash).

**`IAuthRepository`** — new methods:
```csharp
Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub);
Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login);   // transactional
Task AddExternalLoginAsync(ExternalLogin login);                              // link to existing user
```

**`AuthRepository`** — Dapper implementations of the above.  
**`InMemoryAuthRepository`** — in-memory implementations for tests.

---

## 3. AuthService — Application Layer

**New service** `src/OpenRussiaRouter.AuthService/Application/Services/GoogleAuthService.cs`:

Logic (see account resolution above). After resolving/creating user:
- Delegates session creation to the same `SessionService` (or `SecretGenerator` + `IAuthRepository.AddSessionAsync`) used by `LoginService`.
- Returns `LoginResult` identical to successful email/password login.

**`GrpcAuthService`** — new RPC handler:
```csharp
public override async Task<LoginResponse> LoginOrRegisterWithGoogle(
    LoginWithGoogleRequest request, ServerCallContext context)
```
Delegates to `GoogleAuthService`.

---

## 4. gRPC Contract

**File:** `src/OpenRussiaRouter.Contracts/Protos/auth.proto`

Add to the `AuthService` service block (reuse existing `LoginResponse`):
```proto
message LoginWithGoogleRequest {
    string google_sub = 1;
    string email      = 2;
}

rpc LoginOrRegisterWithGoogle (LoginWithGoogleRequest) returns (LoginResponse);
```

---

## 5. WebApp

### 5a. NuGet

```xml
<!-- src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj -->
<PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="8.*" />
```

### 5b. Program.cs

Add an external temporary cookie scheme and Google middleware:
```csharp
services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options => { /* existing */ })
    .AddCookie("External", o => o.Cookie.Name = ".orr.ext")
    .AddGoogle(options =>
    {
        options.SignInScheme   = "External";
        options.ClientId       = configuration["Google:ClientId"]!;
        options.ClientSecret   = configuration["Google:ClientSecret"]!;
        options.CallbackPath   = "/signin-google";
        options.Scope.Add("email");
    });
```

### 5c. AuthClient

Add to `src/OpenRussiaRouter.WebApp/Services/Auth/AuthClient.cs`:
```csharp
public async Task<LoginResult?> LoginOrRegisterWithGoogleAsync(string googleSub, string email)
```
Calls the new `LoginOrRegisterWithGoogle` gRPC method; maps response the same way as `LoginAsync`.

### 5d. Login page

**`Pages/Login.cshtml`** — add button below the existing form:
```html
<form method="post" asp-page-handler="Google">
    <button type="submit" class="btn-google">Войти через Google</button>
</form>
```

**`Pages/Login.cshtml.cs`** — add handler:
```csharp
public IActionResult OnPostGoogle()
{
    var props = new AuthenticationProperties { RedirectUri = "/Login/ExternalCallback" };
    return Challenge(GoogleDefaults.AuthenticationScheme, props);
}
```

### 5e. Callback page

**New:** `Pages/Login/ExternalCallback.cshtml.cs` (no visible UI, pure handler):

```csharp
public async Task<IActionResult> OnGetAsync()
{
    var result = await HttpContext.AuthenticateAsync("External");
    if (!result.Succeeded) return RedirectToPage("/Login");

    var googleSub = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var email     = result.Principal.FindFirstValue(ClaimTypes.Email)!;

    var loginResult = await _authClient.LoginOrRegisterWithGoogleAsync(googleSub, email);
    if (loginResult is null) return RedirectToPage("/Login");

    // Same SignInAsync as email/password path
    var claims = new[] {
        new Claim(ClaimTypes.NameIdentifier, loginResult.UserId),
        new Claim(ClaimTypes.Email, email),
        new Claim(ClaimTypes.Role, loginResult.Role),
        new Claim("session_token", loginResult.SessionToken),
    };
    var identity   = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal  = new ClaimsPrincipal(identity);
    var props      = new AuthenticationProperties { IsPersistent = true };

    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
    await HttpContext.SignOutAsync("External");

    return loginResult.Role == "Admin"
        ? RedirectToPage("/Admin/Index")
        : RedirectToPage("/Workspace/ApiKeys");
}
```

---

## 6. Configuration

**`src/OpenRussiaRouter.WebApp/appsettings.json`** — add section:
```json
"Google": {
  "ClientId": "",
  "ClientSecret": ""
}
```

Real values via:
- Local: `dotnet user-secrets set "Google:ClientId" "..."` 
- Kubernetes: env var `Google__ClientId` / `Google__ClientSecret`

**Google Cloud Console setup:**
1. APIs & Services → Credentials → OAuth 2.0 Client ID (Web application)
2. Authorized redirect URIs:
   - `http://localhost:5004/signin-google` (dev)
   - `https://{prod-domain}/signin-google` (prod)

---

## 7. Critical Files

| File | Change |
|------|--------|
| `db/migrations/auth/005_add_external_logins.sql` | New migration |
| `src/OpenRussiaRouter.AuthService/Domain/Entities/UserAccount.cs` | `PasswordHash` → nullable |
| `src/OpenRussiaRouter.AuthService/Domain/Entities/ExternalLogin.cs` | New entity |
| `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/IAuthRepository.cs` | 3 new methods |
| `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs` | Dapper impl |
| `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/InMemoryAuthRepository.cs` | In-memory impl |
| `src/OpenRussiaRouter.AuthService/Application/Services/GoogleAuthService.cs` | New service |
| `src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs` | New RPC handler |
| `src/OpenRussiaRouter.Contracts/Protos/auth.proto` | New message + RPC |
| `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj` | NuGet ref |
| `src/OpenRussiaRouter.WebApp/Program.cs` | External cookie + AddGoogle |
| `src/OpenRussiaRouter.WebApp/Services/Auth/AuthClient.cs` | New method |
| `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml` | Google button |
| `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs` | `OnPostGoogle` handler |
| `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml.cs` | New callback page |
| `src/OpenRussiaRouter.WebApp/appsettings.json` | Google section |

---

## 8. Verification

```powershell
# Unit + integration tests
dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build -nr:false -m:1
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

**New unit tests** in `AuthService.Tests` for `GoogleAuthService` — three branches:
- New user (no account, no external login) → creates user + external_login, returns session
- Existing invited user (email match, no external login) → links external_login, returns session with preserved role
- Existing Google user (external_login found) → returns session

**Manual smoke test:**
1. Configure `Google:ClientId` / `Google:ClientSecret` via user-secrets
2. `dotnet run --project src/OpenRussiaRouter.WebApp`
3. Navigate to `http://localhost:5004/Login`
4. Click "Войти через Google" → complete OAuth → verify redirect to `/Workspace/ApiKeys`
5. Check `auth.users` + `auth.external_logins` rows in DB
