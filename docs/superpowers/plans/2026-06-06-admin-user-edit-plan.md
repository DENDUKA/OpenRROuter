# Admin User Edit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement admin functionality to edit user details (`DisplayName` and `MaxApiKeys`) and enforce API key limits.

**Architecture:** 
1. Database migration to add `display_name` and `max_api_keys` to `auth.users`.
2. Update Domain models and Repositories in `AuthService`.
3. Add API key limit enforcement in `AuthAccountApplicationService`.
4. Add `UpdateUser` and `GetUser` gRPC endpoints.
5. Create `UserEdit.cshtml` in WebApp and update the `Users.cshtml` list.

**Tech Stack:** C#, .NET 8, PostgreSQL (Dapper), gRPC, ASP.NET Core Razor Pages

---

### Task 1: Database Migration and Domain Model Updates

**Files:**
- Create: `db/migrations/auth/008_add_user_display_name_and_api_key_limit.sql`
- Modify: `src/OpenRussiaRouter.AuthService/Domain/Entities/UserAccount.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/UserAccountEntity.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/AuthPersistenceMapper.cs`

- [ ] **Step 1: Write the database migration**

Create `db/migrations/auth/008_add_user_display_name_and_api_key_limit.sql`:

```sql
alter table auth.users
add column if not exists display_name varchar(128) null,
add column if not exists max_api_keys int not null default 10;
```

- [ ] **Step 2: Update UserAccountEntity**

Modify `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/UserAccountEntity.cs` to add properties:

```csharp
    public string? DisplayName { get; set; }

    public int MaxApiKeys { get; set; } = 10;
```

- [ ] **Step 3: Update UserAccount Domain Entity**

Modify `src/OpenRussiaRouter.AuthService/Domain/Entities/UserAccount.cs`:

Add properties:
```csharp
    public string? DisplayName { get; private set; }

    public int MaxApiKeys { get; private set; } = 10;
```

Add `UpdateDetails` method:
```csharp
    public void UpdateDetails(string? displayName, int maxApiKeys)
    {
        DisplayName = displayName;
        MaxApiKeys = Preconditions.RequirePositiveOrZero(maxApiKeys, nameof(maxApiKeys));
    }
```

Update `CreateForExternalLogin`:
```csharp
    public static UserAccount CreateForExternalLogin(string id, string email, UserRole role, DateTimeOffset createdAtUtc)
        => new(id, email, role, null, createdAtUtc) { MaxApiKeys = 10 };
```

Update `CreateForEmailSignup`:
```csharp
    public static UserAccount CreateForEmailSignup(string id, string email, UserRole role, string passwordHash, DateTimeOffset createdAtUtc)
    {
        var user = new UserAccount(id, email, role, passwordHash, createdAtUtc);
        user.Status = UserStatus.PendingVerification;
        user.MaxApiKeys = 10;
        return user;
    }
```

Update `Restore`:
```csharp
    public static UserAccount Restore(string id, string email, UserRole role, string? passwordHash, UserStatus status, DateTimeOffset createdAtUtc, string? displayName = null, int maxApiKeys = 10)
    {
        var user = new UserAccount(id, email, role, passwordHash, createdAtUtc);
        user.Status = status;
        user.DisplayName = displayName;
        user.MaxApiKeys = maxApiKeys;
        return user;
    }
```

- [ ] **Step 4: Update AuthPersistenceMapper**

Modify `src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/AuthPersistenceMapper.cs`:

In `ToEntity`:
```csharp
            PasswordHash = user.PasswordHash,
            CreatedAtUtc = user.CreatedAtUtc,
            DisplayName = user.DisplayName,
            MaxApiKeys = user.MaxApiKeys
```

In `ToDomain`:
```csharp
            entity.PasswordHash,
            entity.Status,
            entity.CreatedAtUtc,
            entity.DisplayName,
            entity.MaxApiKeys);
```

- [ ] **Step 5: Run tests to verify build**

Run: `dotnet test src/OpenRussiaRouter.AuthService`
Expected: PASS (or build error if I missed a precondition, fix if needed)

- [ ] **Step 6: Commit**

```bash
git add db/migrations/auth/008_add_user_display_name_and_api_key_limit.sql src/OpenRussiaRouter.AuthService/Domain/Entities/UserAccount.cs src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/UserAccountEntity.cs src/OpenRussiaRouter.AuthService/Infrastructure/Persistence/AuthPersistenceMapper.cs
git commit -m "feat(auth): add display name and max api keys to user domain"
```

---

### Task 2: Update Repository Data Access

**Files:**
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/IAuthRepository.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/InMemoryAuthRepository.cs`

- [ ] **Step 1: Update IAuthRepository**

Add to `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/IAuthRepository.cs`:
```csharp
    Task UpdateUserAsync(UserAccount user, CancellationToken ct = default);
    Task<int> CountApiKeysAsync(string userId, CancellationToken ct = default);
```

- [ ] **Step 2: Update AuthRepository SQL Queries**

Modify `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs`.

Update `UserSelectSql`:
```csharp
    private const string UserSelectSql = """
        select
            id as Id,
            email as Email,
            role as Role,
            status as Status,
            password_hash as PasswordHash,
            created_at_utc as CreatedAtUtc,
            display_name as DisplayName,
            max_api_keys as MaxApiKeys
        from auth.users
        """;
```

Update `ToParameters`:
```csharp
    private static object ToParameters(UserAccount user) => new
    {
        user.Id,
        user.Email,
        Role = user.Role.ToString(),
        Status = user.Status.ToString(),
        user.PasswordHash,
        user.CreatedAtUtc,
        user.DisplayName,
        user.MaxApiKeys
    };
```

- [ ] **Step 3: Implement UpdateUserAsync and CountApiKeysAsync in AuthRepository**

Add to `AuthRepository.cs`:
```csharp
    public async Task UpdateUserAsync(UserAccount user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update auth.users
            set display_name = @DisplayName,
                max_api_keys = @MaxApiKeys
            where id = @Id;
            """,
            ToParameters(user),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> CountApiKeysAsync(string userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from auth.api_keys where user_id = @UserId and deleted_at_utc is null;",
            new { UserId = userId },
            cancellationToken: ct)).ConfigureAwait(false);
    }
```

- [ ] **Step 4: Implement in InMemoryAuthRepository**

Add to `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/InMemoryAuthRepository.cs`:
```csharp
    public Task UpdateUserAsync(UserAccount user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (_gate)
        {
            if (_users.ContainsKey(user.Id))
            {
                _users[user.Id] = user;
            }
        }
        return Task.CompletedTask;
    }

    public Task<int> CountApiKeysAsync(string userId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_apiKeys.Values.Count(k => k.UserId == userId && !k.IsDeleted));
        }
    }
```

- [ ] **Step 5: Run tests**

Run: `dotnet build src/OpenRussiaRouter.AuthService`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Infrastructure/Repository/
git commit -m "feat(auth): add UpdateUser and CountApiKeys repository methods"
```

---

### Task 3: Application Service Logic & Limits

**Files:**
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/AuthAccountApplicationService.cs`

- [ ] **Step 1: Enforce limit in CreateApiKeyAsync**

Modify `src/OpenRussiaRouter.AuthService/Application/Services/AuthAccountApplicationService.cs`:

Find `CreateApiKeyAsync`.
```csharp
    public async Task<CreatedApiKey> CreateApiKeyAsync(string userId, string name, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var user = await repository.GetUserAsync(userId, ct).ConfigureAwait(false);
        if (user is not { Status: UserStatus.Active })
            throw new InvalidOperationException("User is not active.");

        var currentKeysCount = await repository.CountApiKeysAsync(userId, ct).ConfigureAwait(false);
        if (currentKeysCount >= user.MaxApiKeys)
        {
            throw new InvalidOperationException($"API key limit reached. Maximum allowed: {user.MaxApiKeys}.");
        }

        var created = apiKeyFactory.Create(userId, name, nowUtc);
        await repository.AddApiKeyAsync(created.ApiKey, ct).ConfigureAwait(false);
        return created;
    }
```

- [ ] **Step 2: Add UpdateUserAsync to ApplicationService**

Add to `AuthAccountApplicationService`:
```csharp
    public async Task UpdateUserAsync(string userId, string? displayName, int maxApiKeys, CancellationToken ct = default)
    {
        var user = await repository.GetUserAsync(userId, ct).ConfigureAwait(false);
        if (user == null)
            throw new InvalidOperationException("User not found.");

        user.UpdateDetails(displayName, maxApiKeys);
        await repository.UpdateUserAsync(user, ct).ConfigureAwait(false);
    }
```

- [ ] **Step 3: Build & Commit**

Run: `dotnet build src/OpenRussiaRouter.AuthService`

```bash
git add src/OpenRussiaRouter.AuthService/Application/Services/AuthAccountApplicationService.cs
git commit -m "feat(auth): enforce API key limits and add user update logic"
```

---

### Task 4: gRPC Contracts & Endpoints

**Files:**
- Modify: `src/OpenRussiaRouter.Contracts/Protos/auth.proto`
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Grpc/GrpcAuthService.cs`

- [ ] **Step 1: Update auth.proto**

Modify `src/OpenRussiaRouter.Contracts/Protos/auth.proto`.
Add new fields to `UserInfo`:
```protobuf
message UserInfo {
  string id = 1;
  string email = 2;
  string role = 3;
  string status = 4;
  int64 created_at_unix_time_seconds = 5;
  optional string display_name = 6;
  int32 max_api_keys = 7;
}
```

Add new RPCs to `AuthService`:
```protobuf
  rpc GetUser (GetUserRequest) returns (GetUserResponse);
  rpc UpdateUser (UpdateUserRequest) returns (UpdateUserResponse);
```

Add message definitions:
```protobuf
message GetUserRequest {
  string user_id = 1;
}

message GetUserResponse {
  UserInfo user = 1;
}

message UpdateUserRequest {
  string user_id = 1;
  optional string display_name = 2;
  int32 max_api_keys = 3;
}

message UpdateUserResponse {
  bool succeeded = 1;
  string error_code = 2;
}
```

- [ ] **Step 2: Rebuild Contracts**

Run: `dotnet build src/OpenRussiaRouter.Contracts`

- [ ] **Step 3: Update GrpcAuthService.cs**

Modify `src/OpenRussiaRouter.AuthService/Infrastructure/Grpc/GrpcAuthService.cs`.
Update `ListUsers`:
```csharp
                users.Select(u => new UserInfo
                {
                    Id = u.Id,
                    Email = u.Email,
                    Role = u.Role.ToString(),
                    Status = u.Status.ToString(),
                    CreatedAtUnixTimeSeconds = u.CreatedAtUtc.ToUnixTimeSeconds(),
                    DisplayName = u.DisplayName ?? "",
                    MaxApiKeys = u.MaxApiKeys
                })
```
*Note: Due to `optional`, protobuf generator generates `HasDisplayName` and allows setting it directly to null if we check it, but setting it to empty string or directly assigning string is simpler.* Actually protobuf 3 optional strings generate a standard property.
Use:
```csharp
                    DisplayName = u.DisplayName,
                    MaxApiKeys = u.MaxApiKeys
```

Add `GetUser` method:
```csharp
    public override async Task<GetUserResponse> GetUser(GetUserRequest request, ServerCallContext context)
    {
        var user = await repository.GetUserAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        if (user == null) return new GetUserResponse();

        return new GetUserResponse
        {
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role.ToString(),
                Status = user.Status.ToString(),
                CreatedAtUnixTimeSeconds = user.CreatedAtUtc.ToUnixTimeSeconds(),
                DisplayName = user.DisplayName,
                MaxApiKeys = user.MaxApiKeys
            }
        };
    }
```

Add `UpdateUser` method:
```csharp
    public override async Task<UpdateUserResponse> UpdateUser(UpdateUserRequest request, ServerCallContext context)
    {
        try
        {
            await accounts.UpdateUserAsync(
                request.UserId,
                request.HasDisplayName ? request.DisplayName : null,
                request.MaxApiKeys,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new UpdateUserResponse { Succeeded = true };
        }
        catch (InvalidOperationException)
        {
            return new UpdateUserResponse { Succeeded = false, ErrorCode = ErrorCodes.ValidationFailed };
        }
    }
```

*Fix `CreateApiKey` error mapping (due to InvalidOperationException being thrown):*
Ensure exception is handled in `CreateApiKey`:
```csharp
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(ex, "CreateApiKey failed. UserId: {UserId}; Name: {Name}", request.UserId, request.Name);
            return new CreateApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
```
(Already handled like this).

- [ ] **Step 4: Build & Commit**

Run: `dotnet build src/OpenRussiaRouter.AuthService`

```bash
git add src/OpenRussiaRouter.Contracts/Protos/auth.proto src/OpenRussiaRouter.AuthService/Infrastructure/Grpc/GrpcAuthService.cs
git commit -m "feat(auth): add UpdateUser and GetUser gRPC endpoints"
```

---

### Task 5: WebApp Integration

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Services/Auth/UserInfo.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Services/Auth/AuthProvider.cs`
- Create: `src/OpenRussiaRouter.WebApp/Pages/Admin/UserEdit.cshtml`
- Create: `src/OpenRussiaRouter.WebApp/Pages/Admin/UserEdit.cshtml.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Admin/Users.cshtml`

- [ ] **Step 1: Update WebApp Auth Models and Provider**

Modify `src/OpenRussiaRouter.WebApp/Services/Auth/UserInfo.cs`:
```csharp
namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed record UserInfo(string Id, string Email, string Role, string Status, DateTimeOffset CreatedAt, string? DisplayName, int MaxApiKeys);
```

Modify `src/OpenRussiaRouter.WebApp/Services/Auth/AuthProvider.cs`:

Update `ListUsersAsync` mapper:
```csharp
        return response.Users.Select(u => new UserInfo(
            u.Id,
            u.Email,
            u.Role,
            u.Status,
            DateTimeOffset.FromUnixTimeSeconds(u.CreatedAtUnixTimeSeconds),
            u.HasDisplayName ? u.DisplayName : null,
            u.MaxApiKeys)).ToList();
```

Add `GetUserAsync`:
```csharp
    public async Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetUserAsync(new GetUserRequest { UserId = userId }, cancellationToken: cancellationToken);
        if (response.User == null) return null;
        var u = response.User;
        return new UserInfo(
            u.Id, u.Email, u.Role, u.Status,
            DateTimeOffset.FromUnixTimeSeconds(u.CreatedAtUnixTimeSeconds),
            u.HasDisplayName ? u.DisplayName : null,
            u.MaxApiKeys);
    }
```

Add `UpdateUserAsync`:
```csharp
    public async Task UpdateUserAsync(string userId, string? displayName, int maxApiKeys, CancellationToken cancellationToken = default)
    {
        var request = new UpdateUserRequest
        {
            UserId = userId,
            MaxApiKeys = maxApiKeys
        };
        if (displayName != null) request.DisplayName = displayName;

        var response = await client.UpdateUserAsync(request, cancellationToken: cancellationToken);
        if (!response.Succeeded) throw new AuthException(response.ErrorCode);
    }
```

- [ ] **Step 2: Create UserEdit Page Backend**

Create `src/OpenRussiaRouter.WebApp/Pages/Admin/UserEdit.cshtml.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenRussiaRouter.WebApp.Services.Auth;

namespace OpenRussiaRouter.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UserEditModel(AuthProvider authProvider) : PageModelBase
{
    public UserInfo? TargetUser { get; private set; }
    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public UserEditInput Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string userId)
    {
        TargetUser = await authProvider.GetUserAsync(userId);
        if (TargetUser == null) return NotFound();

        Form.DisplayName = TargetUser.DisplayName;
        Form.MaxApiKeys = TargetUser.MaxApiKeys;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string userId)
    {
        TargetUser = await authProvider.GetUserAsync(userId);
        if (TargetUser == null) return NotFound();

        if (!ModelState.IsValid) return Page();

        try
        {
            await authProvider.UpdateUserAsync(userId, Form.DisplayName, Form.MaxApiKeys);
            return RedirectToPage("Users", new { success = "User updated successfully." });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Update failed: {ex.Message}";
            return Page();
        }
    }
}

public class UserEditInput
{
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    [Required]
    [Range(0, 1000)]
    [Display(Name = "Max API Keys")]
    public int MaxApiKeys { get; set; } = 10;
}
```

- [ ] **Step 3: Create UserEdit Page UI**

Create `src/OpenRussiaRouter.WebApp/Pages/Admin/UserEdit.cshtml`:
```html
@page "/admin/users/{userId}/edit"
@model Admin.UserEditModel
@{ Layout = "~/Pages/Shared/_AdminLayout.cshtml"; ViewData["Title"] = "Edit User - Admin"; }

<div class="adm-section-head">
    <h2>Edit User</h2>
    <p>Update user details and limits.</p>
</div>

<div class="adm-btn-row">
    <a class="btn btn-secondary" href="/admin/users">Back to users</a>
</div>

@if (Model.ErrorMessage != null)
{
    <div class="alert alert-danger" role="alert">@Model.ErrorMessage</div>
}

@if (Model.TargetUser is { } user)
{
    <div class="adm-card">
        <div class="adm-card-header">
            <span>@user.Email</span>
            <span class="badge @(user.Status == "Active" ? "badge-success" : "badge-secondary")">@user.Status</span>
        </div>
        <div class="adm-card-body">
            <form method="post">
                <div class="adm-form-row">
                    <div class="form-group">
                        <label asp-for="Form.DisplayName" class="form-label">Display Name</label>
                        <input asp-for="Form.DisplayName" class="form-control" />
                        <span asp-validation-for="Form.DisplayName" class="validation-message"></span>
                    </div>
                </div>
                <div class="adm-form-row">
                    <div class="form-group">
                        <label asp-for="Form.MaxApiKeys" class="form-label">Max API Keys</label>
                        <input asp-for="Form.MaxApiKeys" class="form-control" type="number" min="0" />
                        <span asp-validation-for="Form.MaxApiKeys" class="validation-message"></span>
                    </div>
                </div>
                <div class="adm-btn-row">
                    <button type="submit" class="btn btn-primary">Save Changes</button>
                </div>
            </form>
        </div>
    </div>
}
```

- [ ] **Step 4: Update Users Page List UI**

Modify `src/OpenRussiaRouter.WebApp/Pages/Admin/Users.cshtml`.

First, add handling for the `success` query parameter at the top:
```html
@if (Request.Query["success"].Count > 0)
{
    <div class="alert alert-success" role="status">@Request.Query["success"]</div>
}
```

In the `<td class="adm-actions-cell">`, add the Edit button icon before the "Top Up" icon:
```html
<a class="btn-icon" title="Edit User" aria-label="Edit User" href="/admin/users/@user.Id/edit">
    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"></path>
        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"></path>
    </svg>
</a>
```

- [ ] **Step 5: Run tests and Verify build**

Run: `dotnet build src/OpenRussiaRouter.WebApp`

- [ ] **Step 6: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/
git commit -m "feat(admin): add edit user page"
```
