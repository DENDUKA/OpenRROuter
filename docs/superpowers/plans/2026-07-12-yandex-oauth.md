# Yandex OAuth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Yandex OAuth as an alternative sign-in option — any Yandex account creates a User-role account automatically; existing email accounts are linked on first Yandex login.

**Architecture:** Mirrors the existing Google OAuth implementation. WebApp handles the OAuth callback via `AspNet.Security.OAuth.Yandex` middleware, then calls a new `LoginOrRegisterWithYandex` gRPC method on AuthService. AuthService reuses the existing `auth.external_logins` table with `provider = 'yandex'`. WebApp signs the cookie identically to the existing Google/email paths.

**Tech Stack:** ASP.NET Core 8 Cookie + Yandex OAuth middleware (`AspNet.Security.OAuth.Yandex`), gRPC/Protobuf, Dapper/PostgreSQL, xUnit + FluentAssertions.

---

## File Map

| File | Action |
|------|--------|
| `Directory.Packages.props` | Modify — add Yandex package version |
| `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj` | Modify — add Yandex package ref |
| `src/OpenRussiaRouter.AuthService/Domain/Entities/ExternalLogin.cs` | No change (already supports any provider) |
| `src/OpenRussiaRouter.Contracts/Protos/auth.proto` | Modify — new `LoginOrRegisterWithYandex` RPC |
| `src/OpenRussiaRouter.AuthService/Application/Services/YandexAuthService.cs` | Create |
| `src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs` | Modify — new RPC handler |
| `src/OpenRussiaRouter.AuthService/Application/Extensions/AuthServiceExtensions.cs` | Modify — register YandexAuthService |
| `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs` | No change (provider-agnostic) |
| `src/OpenRussiaRouter.WebApp/Program.cs` | Modify — add Yandex middleware |
| `src/OpenRussiaRouter.WebApp/appsettings.json` | Modify — Yandex config section |
| `src/OpenRussiaRouter.WebApp/Services/Auth/AuthClient.cs` | Modify — new method |
| `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml` | Modify — Yandex button |
| `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs` | Modify — OnPostYandex handler |
| `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml` | Modify — handle Yandex (already generic) |
| `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml.cs` | Modify — handle Yandex provider |
| `tests/OpenRussiaRouter.AuthService.Tests/Application/YandexAuthServiceTests.cs` | Create |

---

## Task 1: Add NuGet Package References

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj`

- [ ] **Step 1: Add package version to central management**

In `Directory.Packages.props`, after line 18 (`Microsoft.AspNetCore.Authentication.Google`), add:

```xml
<PackageVersion Include="AspNet.Security.OAuth.Yandex" Version="8.1.0" />
```

- [ ] **Step 2: Add package reference to WebApp**

In `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj`, after line 16 (`Microsoft.AspNetCore.Authentication.Google`), add:

```xml
<PackageReference Include="AspNet.Security.OAuth.Yandex"  />
```

- [ ] **Step 3: Verify restore**

```powershell
dotnet restore src/OpenRussiaRouter.WebApp --configfile NuGet.Config -nr:false -m:1
```

Expected: Package restored successfully.

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj
git commit -m "deps: add AspNet.Security.OAuth.Yandex package"
```

---

## Task 2: gRPC Contract — Yandex RPC

**Files:**
- Modify: `src/OpenRussiaRouter.Contracts/Protos/auth.proto`

- [ ] **Step 1: Add LoginOrRegisterWithYandex RPC to auth.proto**

In `src/OpenRussiaRouter.Contracts/Protos/auth.proto`, add to the `AuthService` service block:

```proto
rpc LoginOrRegisterWithYandex (LoginWithYandexRequest) returns (LoginResponse);
```

Add the new message at the end of the file:

```proto
message LoginWithYandexRequest {
  string yandex_sub = 1;
  string email      = 2;
}
```

- [ ] **Step 2: Build to regenerate gRPC stubs**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.Contracts/Protos/auth.proto
git commit -m "feat(auth): add LoginOrRegisterWithYandex RPC"
```

---

## Task 3: YandexAuthService Unit Tests (TDD)

**Files:**
- Create: `tests/OpenRussiaRouter.AuthService.Tests/Application/YandexAuthServiceTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class YandexAuthServiceTests
{
    private readonly InMemoryAuthRepository _repo = new();
    private readonly ISecretGenerator _secretGenerator = new SecretGenerator();

    [Fact]
    public async Task LoginOrRegisterWithYandex_NewUser_CreatesAccountAndReturnsSession()
    {
        var sut = new YandexAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithYandexAsync("yandex-sub-new", "new@yandex.ru");

        result.Succeeded.Should().BeTrue();
        result.Session.Should().NotBeNull();
        result.Session!.Token.Should().StartWith("orr_session_");
        result.Session.Role.Should().Be(UserRole.User);

        var user = await _repo.GetUserByEmailAsync("new@yandex.ru");
        user.Should().NotBeNull();
        user!.Email.Should().Be("new@yandex.ru");
        user.Role.Should().Be(UserRole.User);
        user.PasswordHash.Should().BeNull();

        var login = await _repo.GetExternalLoginAsync("yandex", "yandex-sub-new");
        login.Should().NotBeNull();
        login!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task LoginOrRegisterWithYandex_ExistingYandexUser_ReturnsSession()
    {
        var existingUser = UserAccount.CreateForExternalLogin(
            "user-existing", "existing@yandex.ru", UserRole.Admin, DateTimeOffset.UtcNow);
        var existingLogin = new ExternalLogin(
            "login-1", existingUser.Id, "yandex", "yandex-sub-existing", DateTimeOffset.UtcNow);
        await _repo.AddUserWithExternalLoginAsync(existingUser, existingLogin);

        var sut = new YandexAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithYandexAsync("yandex-sub-existing", "existing@yandex.ru");

        result.Succeeded.Should().BeTrue();
        result.Session!.UserId.Should().Be("user-existing");
        result.Session.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task LoginOrRegisterWithYandex_ExistingEmailUser_LinksAccountAndPreservesRole()
    {
        var invitedUser = new UserAccount(
            "user-invited", "invited@example.com", UserRole.Admin,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        await _repo.AddUserAndMarkInviteUsedAsync(invitedUser, MakeDummyInvite());

        var sut = new YandexAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithYandexAsync("yandex-sub-invited", "invited@example.com");

        result.Succeeded.Should().BeTrue();
        result.Session!.UserId.Should().Be("user-invited");
        result.Session.Role.Should().Be(UserRole.Admin);

        var link = await _repo.GetExternalLoginAsync("yandex", "yandex-sub-invited");
        link.Should().NotBeNull();
        link!.UserId.Should().Be("user-invited");
    }

    [Fact]
    public async Task LoginOrRegisterWithYandex_DisabledExistingEmailUser_ReturnsUserDisabled()
    {
        var disabledUser = new UserAccount(
            "user-disabled", "disabled@example.com", UserRole.User,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        disabledUser.Disable();
        await _repo.AddUserAndMarkInviteUsedAsync(disabledUser, MakeDummyInvite());

        var sut = new YandexAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithYandexAsync("yandex-sub-disabled", "disabled@example.com");

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("user_disabled");
        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task LoginOrRegisterWithYandex_DisabledExistingYandexUser_ReturnsUserDisabled()
    {
        var disabledUser = UserAccount.CreateForExternalLogin(
            "user-yandex-disabled", "ydisabled@yandex.ru", UserRole.User, DateTimeOffset.UtcNow);
        disabledUser.Disable();
        var existingLogin = new ExternalLogin(
            "login-disabled", disabledUser.Id, "yandex", "yandex-sub-ydisabled", DateTimeOffset.UtcNow);
        await _repo.AddUserWithExternalLoginAsync(disabledUser, existingLogin);

        var sut = new YandexAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithYandexAsync("yandex-sub-ydisabled", "ydisabled@yandex.ru");

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("user_disabled");
    }

    private static Invite MakeDummyInvite() => new(
        "invite-dummy", "dummy@example.com", UserRole.User,
        "dummy-token-hash",
        DateTimeOffset.UtcNow.AddDays(7),
        "admin-1",
        DateTimeOffset.UtcNow);
}
```

- [ ] **Step 2: Confirm tests fail to compile (YandexAuthService does not exist yet)**

```powershell
dotnet build tests/OpenRussiaRouter.AuthService.Tests --no-restore -nr:false -m:1
```

Expected: Build FAILED — `YandexAuthService` not found.

- [ ] **Step 3: Commit the failing tests**

```bash
git add tests/OpenRussiaRouter.AuthService.Tests/Application/YandexAuthServiceTests.cs
git commit -m "test(auth): add YandexAuthService unit tests (TDD — failing)"
```

---

## Task 4: YandexAuthService Implementation

**Files:**
- Create: `src/OpenRussiaRouter.AuthService/Application/Services/YandexAuthService.cs`

- [ ] **Step 1: Create YandexAuthService**

```csharp
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class YandexAuthService(IAuthRepository repository, ISecretGenerator secretGenerator)
{
    private const string SessionPrefix = "orr_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public async Task<LoginResult> LoginOrRegisterWithYandexAsync(
        string yandexSub,
        string email,
        CancellationToken ct = default)
    {
        var externalLogin = await repository.GetExternalLoginAsync("yandex", yandexSub, ct);
        UserAccount user;

        if (externalLogin is not null)
        {
            var existing = await repository.GetUserAsync(externalLogin.UserId, ct);
            if (existing is null || existing.Status == UserStatus.Disabled)
                return LoginResult.Failed(AuthErrorCodes.UserDisabled);
            user = existing;
        }
        else
        {
            var byEmail = await repository.GetUserByEmailAsync(email, ct);
            if (byEmail is not null)
            {
                if (byEmail.Status == UserStatus.Disabled)
                    return LoginResult.Failed(AuthErrorCodes.UserDisabled);
                var link = new ExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    byEmail.Id,
                    "yandex",
                    yandexSub,
                    DateTimeOffset.UtcNow);
                await repository.AddExternalLoginAsync(link, ct);
                user = byEmail;
            }
            else
            {
                var newUser = UserAccount.CreateForExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    email,
                    UserRole.User,
                    DateTimeOffset.UtcNow);
                var newLogin = new ExternalLogin(
                    Guid.NewGuid().ToString("N"),
                    newUser.Id,
                    "yandex",
                    yandexSub,
                    DateTimeOffset.UtcNow);
                await repository.AddUserWithExternalLoginAsync(newUser, newLogin, ct);
                user = newUser;
            }
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var token = secretGenerator.Generate(SessionPrefix, 32);
        var session = new UserSession(token, user.Id, user.Role, nowUtc, nowUtc.Add(SessionLifetime));
        return LoginResult.Success(session);
    }
}
```

- [ ] **Step 2: Run the unit tests to verify they pass**

```powershell
dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build -nr:false -m:1
```

Expected: All 5 `YandexAuthServiceTests` pass. All previously passing tests still pass.

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Application/Services/YandexAuthService.cs
git commit -m "feat(auth): implement YandexAuthService with 3-branch account resolution"
```

---

## Task 5: GrpcAuthService Handler + DI Registration

**Files:**
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Extensions/AuthServiceExtensions.cs`

- [ ] **Step 1: Add YandexAuthService to GrpcAuthService constructor**

Change the class declaration to add `YandexAuthService yandexAuthService` to the primary constructor:

```csharp
public sealed class GrpcAuthService(
    IAuthApiKeyValidator apiKeyValidator,
    IAuthRepository repository,
    AuthAccountApplicationService accounts,
    LoginService loginService,
    GoogleAuthService googleAuthService,
    YandexAuthService yandexAuthService,
    ILogger<GrpcAuthService> logger) : AuthGrpc.AuthServiceBase
```

Add the new RPC handler method (after the Google handler):

```csharp
public override async Task<LoginResponse> LoginOrRegisterWithYandex(
    LoginWithYandexRequest request,
    ServerCallContext context)
{
    var result = await yandexAuthService.LoginOrRegisterWithYandexAsync(
        request.YandexSub,
        request.Email,
        context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

    if (!result.Succeeded)
        return new LoginResponse { Succeeded = false, ErrorCode = result.ErrorCode ?? ErrorCodes.AuthenticationRequired };

    return new LoginResponse
    {
        Succeeded = true,
        SessionToken = result.Session!.Token,
        UserId = result.Session.UserId,
        Role = result.Session.Role.ToString()
    };
}
```

- [ ] **Step 2: Register YandexAuthService in DI**

In `AuthServiceExtensions.cs`, add `services.AddSingleton<YandexAuthService>()` after `GoogleAuthService`:

```csharp
services.AddSingleton<GoogleAuthService>();
services.AddSingleton<YandexAuthService>();
```

- [ ] **Step 3: Build and test**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build -nr:false -m:1
```

Expected: Build succeeded. All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs
git add src/OpenRussiaRouter.AuthService/Application/Extensions/AuthServiceExtensions.cs
git commit -m "feat(auth): add LoginOrRegisterWithYandex RPC handler and DI registration"
```

---

## Task 6: WebApp — Yandex OAuth Middleware + Config

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs`
- Modify: `src/OpenRussiaRouter.WebApp/appsettings.json`

- [ ] **Step 1: Add Yandex middleware to Program.cs**

Add using at top:

```csharp
using AspNet.Security.OAuth.Yandex;
```

Extend the authentication chain after `.AddGoogle(...)`:

```csharp
.AddYandex(options =>
{
    options.SignInScheme = "External";
    options.ClientId = builder.Configuration["Yandex:ClientId"]!;
    options.ClientSecret = builder.Configuration["Yandex:ClientSecret"]!;
    options.CallbackPath = "/signin-yandex";
    options.Scope.Add("login:email");
    options.Scope.Add("login:info");
});
```

- [ ] **Step 2: Add Yandex config section to appsettings.json**

```json
{
  "Yandex": {
    "ClientId": "",
    "ClientSecret": ""
  }
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Program.cs
git add src/OpenRussiaRouter.WebApp/appsettings.json
git commit -m "feat(webapp): add Yandex OAuth middleware and config"
```

---

## Task 7: WebApp AuthClient — Yandex Method

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Services/Auth/AuthClient.cs`

- [ ] **Step 1: Add LoginOrRegisterWithYandexAsync to AuthClient**

Add the following method after `LoginOrRegisterWithGoogleAsync`:

```csharp
public async Task<LoginResponse?> LoginOrRegisterWithYandexAsync(string yandexSub, string email, CancellationToken cancellationToken = default)
{
    var response = await client.LoginOrRegisterWithYandexAsync(
        new LoginWithYandexRequest { YandexSub = yandexSub, Email = email },
        cancellationToken: cancellationToken);
    if (!response.Succeeded)
        return null;
    return new LoginResponse(response.SessionToken, response.UserId, response.Role, email);
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/OpenRussiaRouter.WebApp --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Services/Auth/AuthClient.cs
git commit -m "feat(webapp): add LoginOrRegisterWithYandexAsync to AuthClient"
```

---

## Task 8: WebApp Login Page — Yandex Button + Callback

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml.cs`

- [ ] **Step 1: Add Yandex button to Login.cshtml**

After the Google button form, add:

```html
<form method="post" asp-page-handler="Yandex">
    <button type="submit" class="btn btn-outline-secondary w-100">Войти через Яндекс</button>
</form>
```

- [ ] **Step 2: Add OnPostYandex handler to Login.cshtml.cs**

Add using:

```csharp
using AspNet.Security.OAuth.Yandex;
```

Add handler:

```csharp
public IActionResult OnPostYandex()
{
    if (User.Identity?.IsAuthenticated == true)
        return RedirectTo(User.FindFirstValue(ClaimTypes.Role));

    var props = new AuthenticationProperties { RedirectUri = "/Login/ExternalCallback" };
    return Challenge(YandexAuthenticationDefaults.AuthenticationScheme, props);
}
```

- [ ] **Step 3: Update ExternalCallback to handle Yandex provider**

In `ExternalCallback.cshtml.cs`, update the `OnGetAsync` method to detect the Yandex provider and call the appropriate AuthClient method. The external login result's claims will contain the provider info — check for Yandex-specific claims or pass the provider through the authentication properties.

- [ ] **Step 4: Build the full solution**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run all tests**

```powershell
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Pages/Login.cshtml
git add src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs
git add src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml.cs
git commit -m "feat(webapp): add Yandex sign-in button and callback handling"
```

---

## Verification

### Unit Tests

```powershell
dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build -nr:false -m:1
```

All 5 `YandexAuthServiceTests` pass. All pre-existing tests pass.

### Manual Smoke Test

Prerequisites: Yandex OAuth credentials from Yandex OAuth.

1. Set credentials (do not commit these):
```powershell
cd src/OpenRussiaRouter.WebApp
dotnet user-secrets set "Yandex:ClientId" "<your-client-id>"
dotnet user-secrets set "Yandex:ClientSecret" "<your-client-secret>"
```

2. In Yandex OAuth settings, add `http://localhost:5004/signin-yandex` as a callback URL.

3. Start WebApp:
```powershell
dotnet run --project src/OpenRussiaRouter.WebApp
```

4. Navigate to `http://localhost:5004/Login`. Verify:
   - "Войти через Яндекс" button is visible.
   - Clicking it redirects to Yandex OAuth consent screen.
   - After consent, browser redirects back to `/Login/ExternalCallback`.
   - Page redirects to `/Workspace/ApiKeys`.
   - `auth.external_logins` table has a row with `provider = 'yandex'`.

5. Log out and click "Войти через Яндекс" again. Verify login succeeds instantly.

6. Verify Google and email/password login still work.
