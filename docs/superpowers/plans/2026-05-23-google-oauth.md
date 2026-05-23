# Google OAuth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Google OAuth as an alternative sign-in option — any Google account creates a User-role account automatically; existing email accounts are linked on first Google login.

**Architecture:** WebApp handles the OAuth callback via ASP.NET Core `AddGoogle` middleware, then calls a new `LoginOrRegisterWithGoogle` gRPC method on AuthService. AuthService stores the mapping in a new `auth.external_logins` table (extensible to future providers) and returns the same `LoginResponse` / session token as email/password login. WebApp signs the cookie identically to the existing path.

**Tech Stack:** ASP.NET Core 8 Cookie + Google OAuth middleware, gRPC/Protobuf, Dapper/PostgreSQL, xUnit + FluentAssertions.

---

## File Map

| File | Action |
|------|--------|
| `db/migrations/auth/005_add_external_logins.sql` | Create |
| `src/OpenRussiaRouter.AuthService/Domain/Entities/ExternalLogin.cs` | Create |
| `src/OpenRussiaRouter.AuthService/Domain/Entities/UserAccount.cs` | Modify — nullable PasswordHash + factory |
| `src/OpenRussiaRouter.AuthService/Application/Services/LoginService.cs` | Modify — guard null PasswordHash |
| `src/OpenRussiaRouter.AuthService/Application/Services/IAuthRepository.cs` | Modify — 3 new methods |
| `src/OpenRussiaRouter.AuthService/Application/Services/InMemoryAuthRepository.cs` | Modify — implement 3 new methods |
| `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/ExternalLoginEntity.cs` | Create |
| `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/AuthPersistenceMapper.cs` | Modify — ToDomain for ExternalLogin |
| `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs` | Modify — 3 new methods |
| `src/OpenRussiaRouter.AuthService/Application/Services/GoogleAuthService.cs` | Create |
| `src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs` | Modify — new RPC handler + ctor param |
| `src/OpenRussiaRouter.AuthService/Application/Extensions/AuthServiceExtensions.cs` | Modify — register GoogleAuthService |
| `src/OpenRussiaRouter.Contracts/Protos/auth.proto` | Modify — new message + RPC |
| `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj` | Modify — NuGet ref |
| `src/OpenRussiaRouter.WebApp/Program.cs` | Modify — external cookie + AddGoogle |
| `src/OpenRussiaRouter.WebApp/appsettings.json` | Modify — Google section |
| `src/OpenRussiaRouter.WebApp/Services/Auth/AuthClient.cs` | Modify — new method |
| `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml` | Modify — Google button |
| `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs` | Modify — OnPostGoogle handler |
| `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml` | Create |
| `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml.cs` | Create |
| `tests/OpenRussiaRouter.AuthService.Tests/Application/GoogleAuthServiceTests.cs` | Create |

---

## Task 1: DB Migration

**Files:**
- Create: `db/migrations/auth/005_add_external_logins.sql`

- [ ] **Step 1: Create the migration file**

```sql
alter table auth.users
    alter column password_hash drop not null;

create table auth.external_logins (
    id             varchar(64)  primary key,
    user_id        varchar(64)  not null references auth.users(id),
    provider       varchar(64)  not null,
    provider_sub   varchar(256) not null,
    created_at_utc timestamptz  not null
);

create unique index ux_auth_external_logins_provider_sub
    on auth.external_logins(provider, provider_sub);

create index ix_auth_external_logins_user_id
    on auth.external_logins(user_id);
```

- [ ] **Step 2: Commit**

```bash
git add db/migrations/auth/005_add_external_logins.sql
git commit -m "feat(auth): add external_logins table and nullable password_hash"
```

---

## Task 2: Domain Entity + UserAccount Changes + LoginService Guard

**Files:**
- Create: `src/OpenRussiaRouter.AuthService/Domain/Entities/ExternalLogin.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Domain/Entities/UserAccount.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/LoginService.cs`

- [ ] **Step 1: Create ExternalLogin domain entity**

Create `src/OpenRussiaRouter.AuthService/Domain/Entities/ExternalLogin.cs`:

```csharp
namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed record ExternalLogin(
    string Id,
    string UserId,
    string Provider,
    string ProviderSub,
    DateTimeOffset CreatedAtUtc);
```

- [ ] **Step 2: Make UserAccount.PasswordHash nullable and add factory**

Replace the entire content of `src/OpenRussiaRouter.AuthService/Domain/Entities/UserAccount.cs`:

```csharp
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed class UserAccount
{

    public UserAccount(string id, string email, UserRole role, string? passwordHash, DateTimeOffset createdAtUtc)
    {
        Id = Preconditions.RequireValue(id, nameof(id));
        Email = EmailValidator.Normalize(email);
        Role = role;
        PasswordHash = passwordHash;
        CreatedAtUtc = createdAtUtc;
    }

    public string Id { get; }

    public string Email { get; }

    public UserRole Role { get; }

    public UserStatus Status { get; private set; } = UserStatus.Active;

    public string? PasswordHash { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public void Disable() => Status = UserStatus.Disabled;

    public static UserAccount CreateForExternalLogin(string id, string email, UserRole role, DateTimeOffset createdAtUtc)
        => new(id, email, role, null, createdAtUtc);

}
```

- [ ] **Step 3: Update UserAccountEntity to match nullable PasswordHash**

In `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/UserAccountEntity.cs`, change `PasswordHash` from `string` to `string?`:

```csharp
using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public sealed class UserAccountEntity
{
    public string Id { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public UserStatus Status { get; set; }

    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
```

- [ ] **Step 4: Guard null PasswordHash in LoginService**

In `src/OpenRussiaRouter.AuthService/Application/Services/LoginService.cs`, add a null check before password verification (after the `user.Status == Disabled` check):

```csharp
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class LoginService(Pbkdf2PasswordHasher passwordHasher, ISecretGenerator secretGenerator)
{
    private const string SessionPrefix = "orr_session";

    public LoginResult Login(
        UserAccount? user,
        string password,
        DateTimeOffset nowUtc,
        TimeSpan sessionLifetime)
    {
        if (sessionLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionLifetime), "Session lifetime must be positive.");
        }

        if (user is null)
        {
            return LoginResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        if (user.Status == UserStatus.Disabled)
        {
            return LoginResult.Failed(AuthErrorCodes.UserDisabled);
        }

        if (user.PasswordHash is null)
        {
            return LoginResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        if (!passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return LoginResult.Failed(AuthErrorCodes.InvalidCredentials);
        }

        var session = new UserSession(
            secretGenerator.Generate(SessionPrefix, 32),
            user.Id,
            user.Role,
            nowUtc,
            nowUtc.Add(sessionLifetime));

        return LoginResult.Success(session);
    }
}

public sealed record LoginResult(bool Succeeded, UserSession? Session, string? ErrorCode)
{

    public static LoginResult Success(UserSession session) => new(true, session, null);

    public static LoginResult Failed(string errorCode) => new(false, null, errorCode);
}
```

- [ ] **Step 5: Build to confirm no compile errors**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Run existing auth tests**

```powershell
dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build -nr:false -m:1
```

Expected: All existing tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Domain/Entities/ExternalLogin.cs
git add src/OpenRussiaRouter.AuthService/Domain/Entities/UserAccount.cs
git add src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/UserAccountEntity.cs
git add src/OpenRussiaRouter.AuthService/Application/Services/LoginService.cs
git commit -m "feat(auth): ExternalLogin entity, nullable PasswordHash, LoginService null guard"
```

---

## Task 3: Repository Interface + InMemory Implementation

**Files:**
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/IAuthRepository.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/InMemoryAuthRepository.cs`

- [ ] **Step 1: Add 3 new methods to IAuthRepository**

Append these three method signatures to `IAuthRepository.cs` (inside the interface, before the closing `}`):

```csharp
Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default);
Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default);
Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default);
```

The full file after edits:

```csharp
using OpenRussiaRouter.AuthService.Domain.Entities;

namespace OpenRussiaRouter.AuthService.Application.Services;

public interface IAuthRepository
{
    Task AddInviteAsync(Invite invite, CancellationToken ct = default);
    Task<Invite?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddUserAndMarkInviteUsedAsync(UserAccount user, Invite invite, CancellationToken ct = default);
    Task<UserAccount?> GetUserAsync(string userId, CancellationToken ct = default);
    Task<UserAccount?> GetUserByEmailAsync(string email, CancellationToken ct = default);
    Task<(IReadOnlyCollection<UserAccount> Items, bool HasMore)> ListUsersAsync(int limit, CancellationToken ct = default);
    Task AddApiKeyAsync(ApiKey apiKey, CancellationToken ct = default);
    Task<ApiKey?> GetApiKeyAsync(string apiKeyId, CancellationToken ct = default);
    Task<(IReadOnlyCollection<ApiKey> Items, bool HasMore)> ListApiKeysAsync(string userId, int limit, CancellationToken ct = default);
    Task<bool> RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken ct = default);
    Task<bool> RevokeApiKeyAsync(string userId, string apiKeyId, DateTimeOffset revokedAtUtc, CancellationToken ct = default);
    Task<IReadOnlyCollection<(string Prefix, string SecretHash)>> ListActiveApiKeyHashesAsync(CancellationToken ct = default);
    Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken ct = default);
    Task<ApiKey?> FindActiveApiKeyBySecretHashAsync(string secretHash, CancellationToken ct = default);
    Task MarkApiKeyUsedAsync(string apiKeyId, DateTimeOffset lastUsedAtUtc, CancellationToken ct = default);
    Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default);
    Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default);
    Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default);
}
```

- [ ] **Step 2: Add implementations to InMemoryAuthRepository**

Add a new field `_externalLogins` and the three method implementations to `InMemoryAuthRepository.cs`. Insert after the existing `_apiKeys` field and add the methods before the closing `}`:

```csharp
// New field — add after the existing _apiKeys field:
private readonly List<ExternalLogin> _externalLogins = [];

// New methods — add before the closing } of the class:
public Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default)
{
    lock (_gate)
    {
        return Task.FromResult(_externalLogins.SingleOrDefault(l => l.Provider == provider && l.ProviderSub == providerSub));
    }
}

public Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default)
{
    lock (_gate) { _externalLogins.Add(login); }
    return Task.CompletedTask;
}

public Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default)
{
    lock (_gate)
    {
        _users[user.Id] = user;
        _externalLogins.Add(login);
    }
    return Task.CompletedTask;
}
```

- [ ] **Step 3: Build to confirm**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors. (`AuthRepository` will now have a compile error about the missing interface methods — that's expected and will be fixed in Task 6.)

Actually — `AuthRepository` already implements `IAuthRepository` and will fail to compile because it's missing the 3 new methods. Fix this temporarily by adding stub throws to `AuthRepository` until Task 6:

Add these three stub methods at the end of `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs` (before the closing `}`):

```csharp
public Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default)
    => throw new NotImplementedException();

public Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default)
    => throw new NotImplementedException();

public Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default)
    => throw new NotImplementedException();
```

Now build to confirm the stubs compile:

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run tests**

```powershell
dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build -nr:false -m:1
```

Expected: All existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Application/Services/IAuthRepository.cs
git add src/OpenRussiaRouter.AuthService/Application/Services/InMemoryAuthRepository.cs
git add src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs
git commit -m "feat(auth): add external login methods to IAuthRepository and InMemory impl"
```

---

## Task 4: GoogleAuthService Unit Tests (TDD — write failing tests first)

**Files:**
- Create: `tests/OpenRussiaRouter.AuthService.Tests/Application/GoogleAuthServiceTests.cs`

- [ ] **Step 1: Create the test file**

Create `tests/OpenRussiaRouter.AuthService.Tests/Application/GoogleAuthServiceTests.cs`:

```csharp
using FluentAssertions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public sealed class GoogleAuthServiceTests
{
    private readonly InMemoryAuthRepository _repo = new();
    private readonly ISecretGenerator _secretGenerator = new SecretGenerator();

    [Fact]
    public async Task LoginOrRegisterWithGoogle_NewUser_CreatesAccountAndReturnsSession()
    {
        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-new", "new@example.com");

        result.Succeeded.Should().BeTrue();
        result.Session.Should().NotBeNull();
        result.Session!.Token.Should().StartWith("orr_session_");
        result.Session.Role.Should().Be(UserRole.User);

        var user = await _repo.GetUserByEmailAsync("new@example.com");
        user.Should().NotBeNull();
        user!.Email.Should().Be("new@example.com");
        user.Role.Should().Be(UserRole.User);
        user.PasswordHash.Should().BeNull();

        var login = await _repo.GetExternalLoginAsync("google", "google-sub-new");
        login.Should().NotBeNull();
        login!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task LoginOrRegisterWithGoogle_ExistingGoogleUser_ReturnsSession()
    {
        var existingUser = UserAccount.CreateForExternalLogin(
            "user-existing", "existing@example.com", UserRole.Admin, DateTimeOffset.UtcNow);
        var existingLogin = new ExternalLogin(
            "login-1", existingUser.Id, "google", "google-sub-existing", DateTimeOffset.UtcNow);
        await _repo.AddUserWithExternalLoginAsync(existingUser, existingLogin);

        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-existing", "existing@example.com");

        result.Succeeded.Should().BeTrue();
        result.Session!.UserId.Should().Be("user-existing");
        result.Session.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task LoginOrRegisterWithGoogle_ExistingEmailUser_LinksAccountAndPreservesRole()
    {
        var invitedUser = new UserAccount(
            "user-invited", "invited@example.com", UserRole.Admin,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        await _repo.AddUserAndMarkInviteUsedAsync(invitedUser, MakeDummyInvite());

        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-invited", "invited@example.com");

        result.Succeeded.Should().BeTrue();
        result.Session!.UserId.Should().Be("user-invited");
        result.Session.Role.Should().Be(UserRole.Admin);

        var link = await _repo.GetExternalLoginAsync("google", "google-sub-invited");
        link.Should().NotBeNull();
        link!.UserId.Should().Be("user-invited");
    }

    [Fact]
    public async Task LoginOrRegisterWithGoogle_DisabledExistingEmailUser_ReturnsUserDisabled()
    {
        var disabledUser = new UserAccount(
            "user-disabled", "disabled@example.com", UserRole.User,
            "pbkdf2-sha256.210000.abc.def", DateTimeOffset.UtcNow);
        disabledUser.Disable();
        await _repo.AddUserAndMarkInviteUsedAsync(disabledUser, MakeDummyInvite());

        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-disabled", "disabled@example.com");

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("user_disabled");
        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task LoginOrRegisterWithGoogle_DisabledExistingGoogleUser_ReturnsUserDisabled()
    {
        var disabledUser = UserAccount.CreateForExternalLogin(
            "user-google-disabled", "gdisabled@example.com", UserRole.User, DateTimeOffset.UtcNow);
        disabledUser.Disable();
        var existingLogin = new ExternalLogin(
            "login-disabled", disabledUser.Id, "google", "google-sub-gdisabled", DateTimeOffset.UtcNow);
        await _repo.AddUserWithExternalLoginAsync(disabledUser, existingLogin);

        var sut = new GoogleAuthService(_repo, _secretGenerator);

        var result = await sut.LoginOrRegisterWithGoogleAsync("google-sub-gdisabled", "gdisabled@example.com");

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

- [ ] **Step 2: Confirm tests fail to compile (GoogleAuthService does not exist yet)**

```powershell
dotnet build tests/OpenRussiaRouter.AuthService.Tests --no-restore -nr:false -m:1
```

Expected: Build FAILED — `GoogleAuthService` not found.

- [ ] **Step 3: Commit the failing tests**

```bash
git add tests/OpenRussiaRouter.AuthService.Tests/Application/GoogleAuthServiceTests.cs
git commit -m "test(auth): add GoogleAuthService unit tests (TDD — failing)"
```

---

## Task 5: GoogleAuthService Implementation

**Files:**
- Create: `src/OpenRussiaRouter.AuthService/Application/Services/GoogleAuthService.cs`

- [ ] **Step 1: Create GoogleAuthService**

Create `src/OpenRussiaRouter.AuthService/Application/Services/GoogleAuthService.cs`:

```csharp
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class GoogleAuthService(IAuthRepository repository, ISecretGenerator secretGenerator)
{
    private const string SessionPrefix = "orr_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public async Task<LoginResult> LoginOrRegisterWithGoogleAsync(
        string googleSub,
        string email,
        CancellationToken ct = default)
    {
        var externalLogin = await repository.GetExternalLoginAsync("google", googleSub, ct);
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
                    "google",
                    googleSub,
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
                    "google",
                    googleSub,
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

Expected: All 5 `GoogleAuthServiceTests` pass. All previously passing tests still pass.

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Application/Services/GoogleAuthService.cs
git commit -m "feat(auth): implement GoogleAuthService with 3-branch account resolution"
```

---

## Task 6: AuthRepository Postgres Implementation

**Files:**
- Create: `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/ExternalLoginEntity.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/AuthPersistenceMapper.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs`

- [ ] **Step 1: Create ExternalLoginEntity**

Create `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/ExternalLoginEntity.cs`:

```csharp
namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public sealed class ExternalLoginEntity
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProviderSub { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
```

- [ ] **Step 2: Add mapper method to AuthPersistenceMapper**

Add the following method to `AuthPersistenceMapper.cs` (before the final closing `}`):

```csharp
public static ExternalLogin ToDomain(ExternalLoginEntity entity)
{
    ArgumentNullException.ThrowIfNull(entity);
    return new ExternalLogin(entity.Id, entity.UserId, entity.Provider, entity.ProviderSub, entity.CreatedAtUtc);
}
```

- [ ] **Step 3: Replace the stub methods in AuthRepository with full implementations**

Replace the three `NotImplementedException` stub methods added in Task 3 with the full Dapper implementations. The full replacement block (the three stubs → three real methods + new SQL constant):

Add `ExternalLoginSelectSql` constant alongside the other SQL constants (after `ApiKeySelectSql`):

```csharp
private const string ExternalLoginSelectSql = """
    select
        id as Id,
        user_id as UserId,
        provider as Provider,
        provider_sub as ProviderSub,
        created_at_utc as CreatedAtUtc
    from auth.external_logins
    """;
```

Replace `GetExternalLoginAsync` stub with:

```csharp
public async Task<ExternalLogin?> GetExternalLoginAsync(string provider, string providerSub, CancellationToken ct = default)
{
    using var connection = connectionFactory.CreateConnection();
    return await connection.QuerySingleOrDefaultAsync<ExternalLoginEntity>(
        ExternalLoginSelectSql + " where provider = @Provider and provider_sub = @ProviderSub;",
        new { Provider = provider, ProviderSub = providerSub }).ConfigureAwait(false) is { } entity
            ? AuthPersistenceMapper.ToDomain(entity)
            : null;
}
```

Replace `AddExternalLoginAsync` stub with:

```csharp
public async Task AddExternalLoginAsync(ExternalLogin login, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(login);
    using var connection = connectionFactory.CreateConnection();
    await connection.ExecuteAsync(
        """
        insert into auth.external_logins
            (id, user_id, provider, provider_sub, created_at_utc)
        values
            (@Id, @UserId, @Provider, @ProviderSub, @CreatedAtUtc);
        """,
        new { login.Id, login.UserId, login.Provider, login.ProviderSub, login.CreatedAtUtc }).ConfigureAwait(false);
}
```

Replace `AddUserWithExternalLoginAsync` stub with:

```csharp
public async Task AddUserWithExternalLoginAsync(UserAccount user, ExternalLogin login, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(user);
    ArgumentNullException.ThrowIfNull(login);
    using var connection = connectionFactory.CreateConnection();
    connection.Open();
    using var transaction = connection.BeginTransaction();

    await connection.ExecuteAsync(
        """
        insert into auth.users
            (id, email, role, status, password_hash, created_at_utc)
        values
            (@Id, @Email, @Role, @Status, @PasswordHash, @CreatedAtUtc);
        """,
        ToParameters(user),
        transaction).ConfigureAwait(false);

    await connection.ExecuteAsync(
        """
        insert into auth.external_logins
            (id, user_id, provider, provider_sub, created_at_utc)
        values
            (@Id, @UserId, @Provider, @ProviderSub, @CreatedAtUtc);
        """,
        new { login.Id, login.UserId, login.Provider, login.ProviderSub, login.CreatedAtUtc },
        transaction).ConfigureAwait(false);

    transaction.Commit();
}
```

- [ ] **Step 4: Build to confirm**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/ExternalLoginEntity.cs
git add src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/AuthPersistenceMapper.cs
git add src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs
git commit -m "feat(auth): implement AuthRepository Postgres methods for external logins"
```

---

## Task 7: gRPC Contract + GrpcAuthService Handler + DI Registration

**Files:**
- Modify: `src/OpenRussiaRouter.Contracts/Protos/auth.proto`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Extensions/AuthServiceExtensions.cs`

- [ ] **Step 1: Add new message and RPC to auth.proto**

In `src/OpenRussiaRouter.Contracts/Protos/auth.proto`, add the new RPC to the `AuthService` service block (after `GetFirstActiveApiKeyRaw`):

```proto
rpc LoginOrRegisterWithGoogle (LoginWithGoogleRequest) returns (LoginResponse);
```

Add the new message at the end of the file (after `GetFirstActiveApiKeyRawResponse`):

```proto
message LoginWithGoogleRequest {
  string google_sub = 1;
  string email      = 2;
}
```

The service block should now read:
```proto
service AuthService {
  rpc ValidateApiKey (ValidateApiKeyRequest) returns (ValidateApiKeyResponse);
  rpc Login (LoginRequest) returns (LoginResponse);
  rpc CreateInvite (CreateInviteRequest) returns (CreateInviteResponse);
  rpc CompleteInvite (CompleteInviteRequest) returns (CompleteInviteResponse);
  rpc CreateApiKey (CreateApiKeyRequest) returns (CreateApiKeyResponse);
  rpc RevokeApiKey (RevokeApiKeyRequest) returns (RevokeApiKeyResponse);
  rpc ListApiKeys (ListApiKeysRequest) returns (ListApiKeysResponse);
  rpc RenameApiKey (RenameApiKeyRequest) returns (RenameApiKeyResponse);
  rpc ListUsers (ListUsersRequest) returns (ListUsersResponse);
  rpc ListActiveApiKeyHashes (ListActiveApiKeyHashesRequest) returns (ListActiveApiKeyHashesResponse);
  rpc GetFirstActiveApiKeyRaw (GetFirstActiveApiKeyRawRequest) returns (GetFirstActiveApiKeyRawResponse);
  rpc LoginOrRegisterWithGoogle (LoginWithGoogleRequest) returns (LoginResponse);
}
```

- [ ] **Step 2: Build to regenerate gRPC stubs**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded. The generated gRPC client/server stubs now include `LoginOrRegisterWithGoogle`.

- [ ] **Step 3: Add GoogleAuthService to GrpcAuthService constructor and add handler**

Modify `src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs`:

Change the class declaration to add `GoogleAuthService googleAuthService` to the primary constructor:

```csharp
public sealed class GrpcAuthService(
    IAuthApiKeyValidator apiKeyValidator,
    IAuthRepository repository,
    AuthAccountApplicationService accounts,
    LoginService loginService,
    GoogleAuthService googleAuthService,
    ILogger<GrpcAuthService> logger) : AuthGrpc.AuthServiceBase
```

Add the new RPC handler method (add it immediately after the `Login` method):

```csharp
public override async Task<LoginResponse> LoginOrRegisterWithGoogle(
    LoginWithGoogleRequest request,
    ServerCallContext context)
{
    var result = await googleAuthService.LoginOrRegisterWithGoogleAsync(
        request.GoogleSub,
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

- [ ] **Step 4: Register GoogleAuthService in DI**

In `src/OpenRussiaRouter.AuthService/Application/Extensions/AuthServiceExtensions.cs`, add `services.AddSingleton<GoogleAuthService>()` inside `AddAuthApplicationServices` (after the `LoginService` registration):

```csharp
public static IServiceCollection AddAuthApplicationServices(this IServiceCollection services)
{
    services.AddSingleton<Sha256SecretHasher>();
    services.AddSingleton<Pbkdf2PasswordHasher>();
    services.AddSingleton<ISecretGenerator, SecretGenerator>();
    services.AddSingleton<InviteRegistrationService>();
    services.AddSingleton<ApiKeyFactory>();
    services.AddSingleton<LoginService>();
    services.AddSingleton<GoogleAuthService>();
    services.AddSingleton<AuthAccountApplicationService>();

    return services;
}
```

- [ ] **Step 5: Build and test**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build -nr:false -m:1
```

Expected: Build succeeded. All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.Contracts/Protos/auth.proto
git add src/OpenRussiaRouter.AuthService/Application/Services/GrpcAuthService.cs
git add src/OpenRussiaRouter.AuthService/Application/Extensions/AuthServiceExtensions.cs
git commit -m "feat(auth): add LoginOrRegisterWithGoogle RPC, handler, and DI registration"
```

---

## Task 8: WebApp OAuth Setup

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj`
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs`
- Modify: `src/OpenRussiaRouter.WebApp/appsettings.json`

- [ ] **Step 1: Add Google auth NuGet package**

In `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj`, add to the existing `<ItemGroup>` with `PackageReference` entries:

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="8.0.0" />
```

Run restore:

```powershell
dotnet restore src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj --configfile NuGet.Config -nr:false -m:1
```

Expected: Package restored successfully.

- [ ] **Step 2: Add external cookie scheme and Google middleware to Program.cs**

In `src/OpenRussiaRouter.WebApp/Program.cs`, add the following using at the top:

```csharp
using Microsoft.AspNetCore.Authentication.Google;
```

Replace the existing `AddAuthentication` chain:

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
```

with:

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    })
    .AddCookie("External", o => o.Cookie.Name = ".orr.ext")
    .AddGoogle(options =>
    {
        options.SignInScheme = "External";
        options.ClientId = builder.Configuration["Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
        options.CallbackPath = "/signin-google";
        options.Scope.Add("email");
    });
```

- [ ] **Step 3: Add Google configuration section to appsettings.json**

Replace the entire content of `src/OpenRussiaRouter.WebApp/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Google": {
    "ClientId": "",
    "ClientSecret": ""
  }
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj
git add src/OpenRussiaRouter.WebApp/Program.cs
git add src/OpenRussiaRouter.WebApp/appsettings.json
git commit -m "feat(webapp): add Google OAuth middleware and external cookie scheme"
```

---

## Task 9: WebApp AuthClient — New Method

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Services/Auth/AuthClient.cs`

- [ ] **Step 1: Add LoginOrRegisterWithGoogleAsync to AuthClient**

Add the following method to `AuthClient.cs` (after `LoginAsync`, before `CompleteInviteAsync`):

```csharp
public async Task<LoginResponse?> LoginOrRegisterWithGoogleAsync(string googleSub, string email, CancellationToken cancellationToken = default)
{
    var response = await client.LoginOrRegisterWithGoogleAsync(
        new LoginWithGoogleRequest { GoogleSub = googleSub, Email = email },
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
git commit -m "feat(webapp): add LoginOrRegisterWithGoogleAsync to AuthClient"
```

---

## Task 10: WebApp Login Page + ExternalCallback Page

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs`
- Create: `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml`
- Create: `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml.cs`

- [ ] **Step 1: Add Google button to Login.cshtml**

In `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml`, add a divider and Google button after the closing `</form>` tag of the password form (before the closing `</div>` of `auth-container`):

```html
<div class="auth-divider">или</div>
<form method="post" asp-page-handler="Google">
    <button type="submit" class="btn btn-outline-secondary w-100">Войти через Google</button>
</form>
```

Also add a CSS rule for `.auth-divider` inside the existing `<style>` block:

```css
.auth-divider { text-align: center; margin: var(--spacing-lg) 0; color: var(--color-muted, #888); font-size: 13px; }
```

- [ ] **Step 2: Add OnPostGoogle handler to Login.cshtml.cs**

In `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs`, add the using at the top:

```csharp
using Microsoft.AspNetCore.Authentication.Google;
```

Add the handler method inside `LoginModel` (after `OnPostAsync`):

```csharp
public IActionResult OnPostGoogle()
{
    if (User.Identity?.IsAuthenticated == true)
        return RedirectTo(User.FindFirstValue(ClaimTypes.Role));

    var props = new AuthenticationProperties { RedirectUri = "/Login/ExternalCallback" };
    return Challenge(GoogleDefaults.AuthenticationScheme, props);
}
```

- [ ] **Step 3: Create ExternalCallback Razor Page**

Create `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml`:

```html
@page
@model OpenRussiaRouter.WebApp.Pages.Login.ExternalCallbackModel
```

Create `src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages.Login;

public sealed class ExternalCallbackModel(AuthClient authClient) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var result = await HttpContext.AuthenticateAsync("External");
        if (!result.Succeeded)
            return RedirectToPage("/Login");

        var googleSub = result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = result.Principal!.FindFirstValue(ClaimTypes.Email);

        if (googleSub is null || email is null)
            return RedirectToPage("/Login");

        var loginResult = await authClient.LoginOrRegisterWithGoogleAsync(googleSub, email);
        if (loginResult is null)
            return RedirectToPage("/Login");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, loginResult.UserId),
            new Claim(ClaimTypes.Email, loginResult.Email),
            new Claim(ClaimTypes.Role, loginResult.Role),
            new Claim("session_token", loginResult.SessionToken),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
        await HttpContext.SignOutAsync("External");

        return loginResult.Role == "Admin"
            ? RedirectToPage("/Admin/Index")
            : RedirectToPage("/Workspace/ApiKeys");
    }
}
```

- [ ] **Step 4: Build the full solution**

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run all tests**

```powershell
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

Expected: All tests pass (unit + integration; Postgres tests skip gracefully if Docker is unavailable).

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Pages/Login.cshtml
git add src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs
git add src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml
git add src/OpenRussiaRouter.WebApp/Pages/Login/ExternalCallback.cshtml.cs
git commit -m "feat(webapp): add Google sign-in button and OAuth callback page"
```

---

## Verification

### Unit Tests

```powershell
dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build -nr:false -m:1
```

All 5 `GoogleAuthServiceTests` pass. All pre-existing tests pass.

### Manual Smoke Test

Prerequisites: Google OAuth credentials from Google Cloud Console.

1. Set credentials (do not commit these):
```powershell
cd src/OpenRussiaRouter.WebApp
dotnet user-secrets set "Google:ClientId" "<your-client-id>"
dotnet user-secrets set "Google:ClientSecret" "<your-client-secret>"
```

2. In Google Cloud Console, add `http://localhost:5004/signin-google` as an Authorized redirect URI.

3. Start WebApp:
```powershell
dotnet run --project src/OpenRussiaRouter.WebApp
```

4. Navigate to `http://localhost:5004/Login`. Verify:
   - "Войти через Google" button is visible below the password form.
   - Clicking it redirects to Google's OAuth consent screen.
   - After consent, browser redirects back to `/Login/ExternalCallback`.
   - Page redirects to `/Workspace/ApiKeys`.
   - `auth.users` table has a new row with `password_hash = NULL`.
   - `auth.external_logins` table has a row with `provider = 'google'`.

5. Log out and click "Войти через Google" again with the same account. Verify login succeeds instantly (no new `auth.users` row, no new `auth.external_logins` row).

6. Verify email/password login still works for existing users.
