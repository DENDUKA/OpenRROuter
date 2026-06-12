# Deep Refactoring Plan — OpenRussiaRouter

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate god classes, DIP violations, dead code, concurrency bugs, and test coverage gaps across all 6 services.

**Architecture:** Each service follows Domain → Application → Infrastructure layers. Refactoring preserves existing boundaries while fixing violations.

**Tech Stack:** .NET 8, C# 12, Dapper, Npgsql, gRPC, xUnit, FluentAssertions, Moq

---

## Phase 1: BuildingBlocks (Shared Foundation)

### Task 1: Remove Dead Code — `ApiError` Record

**Files:**
- Delete: `src/OpenRussiaRouter.BuildingBlocks/Errors/ApiError.cs`
- Modify: `src/OpenRussiaRouter.BuildingBlocks/OpenRussiaRouter.BuildingBlocks.csproj` (if needed)

- [ ] **Step 1: Verify no runtime usages**

Run: `rg "ApiError" src/ tests/ --include "*.cs" | grep -v "test"`
Expected: Only test file references, no production code.

- [ ] **Step 2: Delete the file**

```bash
rm src/OpenRussiaRouter.BuildingBlocks/Errors/ApiError.cs
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/OpenRussiaRouter.BuildingBlocks --no-restore`
Expected: Build succeeds.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/OpenRussiaRouter.BuildingBlocks.Tests --no-build`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.BuildingBlocks/Errors/ApiError.cs
git commit -m "chore: remove unused ApiError record from BuildingBlocks"
```

---

### Task 2: Fix `GrpcDeadlineInterceptor` — Add Missing Call Types

**Files:**
- Modify: `src/OpenRussiaRouter.BuildingBlocks/Grpc/GrpcDeadlineInterceptor.cs:15-33`

- [ ] **Step 1: Read current file**

```csharp
// Current implementation covers 3 of 5 call types
public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
    TRequest request,
    ClientInterceptorContext<TRequest, TResponse> context,
    AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
{
    if (context.Options.Deadline == null)
    {
        context = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host,
            context.Options.WithDeadline(DateTimeOffset.UtcNow.Add(DefaultDeadline)));
    }
    return continuation(request, context);
}
```

- [ ] **Step 2: Add `AsyncClientStreamingCall` override**

```csharp
public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
    ClientInterceptorContext<TRequest, TResponse> context,
    AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
{
    if (context.Options.Deadline == null)
    {
        context = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host,
            context.Options.WithDeadline(DateTimeOffset.UtcNow.Add(DefaultDeadline)));
    }
    return continuation(context);
}
```

- [ ] **Step 3: Add `AsyncDuplexStreamingCall` override**

```csharp
public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
    ClientInterceptorContext<TRequest, TResponse> context,
    AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
{
    if (context.Options.Deadline == null)
    {
        context = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host,
            context.Options.WithDeadline(DateTimeOffset.UtcNow.Add(DefaultDeadline)));
    }
    return continuation(context);
}
```

- [ ] **Step 4: Build and test**

Run: `dotnet build src/OpenRussiaRouter.BuildingBlocks --no-restore && dotnet test tests/OpenRussiaRouter.BuildingBlocks.Tests --no-build`

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.BuildingBlocks/Grpc/GrpcDeadlineInterceptor.cs
git commit -m "fix: add missing client-streaming and duplex deadline coverage"
```

---

### Task 3: Add Generic `Preconditions.RequireNotNull<T>`

**Files:**
- Modify: `src/OpenRussiaRouter.BuildingBlocks/Common/Preconditions.cs`

- [ ] **Step 1: Add generic overload**

```csharp
public static T RequireNotNull<T>(T? value, string name) where T : class
{
    ArgumentNullException.ThrowIfNull(value, name);
    return value;
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.BuildingBlocks --no-restore && dotnet test tests/OpenRussiaRouter.BuildingBlocks.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.BuildingBlocks/Common/Preconditions.cs
git commit -m "feat: add generic Preconditions.RequireNotNull<T>"
```

---

## Phase 2: AuthService

### Task 4: Fix DIP — Extract Interfaces for Security Services

**Files:**
- Create: `src/OpenRussiaRouter.AuthService/Application/Abstractions/IPasswordHasher.cs`
- Create: `src/OpenRussiaRouter.AuthService/Application/Abstractions/ISecretHasher.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/AuthAccountApplicationService.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/LoginService.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/InviteRegistrationService.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/ApiKeyFactory.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/EmailVerificationService.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/GoogleAuthService.cs`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Extensions/AuthServiceExtensions.cs`

- [ ] **Step 1: Create `IPasswordHasher`**

```csharp
namespace OpenRussiaRouter.AuthService.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
```

- [ ] **Step 2: Create `ISecretHasher`**

```csharp
namespace OpenRussiaRouter.AuthService.Application.Abstractions;

public interface ISecretHasher
{
    string Hash(string secret);
}
```

- [ ] **Step 3: Make `Pbkdf2PasswordHasher` implement `IPasswordHasher`**

```csharp
// In Infrastructure/Security/Pbkdf2PasswordHasher.cs
public sealed class Pbkdf2PasswordHasher : IPasswordHasher { ... }
```

- [ ] **Step 4: Make `Sha256SecretHasher` implement `ISecretHasher`**

```csharp
// In Infrastructure/Security/Sha256SecretHasher.cs
public sealed class Sha256SecretHasher : ISecretHasher { ... }
```

- [ ] **Step 5: Update `AuthAccountApplicationService` constructor**

```csharp
public sealed class AuthAccountApplicationService(
    IAuthRepository repository,
    IPasswordHasher passwordHasher,
    ISecretHasher secretHasher,
    ISecretGenerator secretGenerator,
    ILogger<AuthAccountApplicationService> logger)
{
    // Remove: using OpenRussiaRouter.AuthService.Infrastructure.Security;
}
```

- [ ] **Step 6: Apply same change to `LoginService`, `InviteRegistrationService`, `ApiKeyFactory`, `EmailVerificationService`, `GoogleAuthService`**

Replace `Pbkdf2PasswordHasher` → `IPasswordHasher`, `Sha256SecretHasher` → `ISecretHasher` in each constructor.

- [ ] **Step 7: Update DI registration**

```csharp
// In AuthServiceExtensions.cs
services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
services.AddSingleton<ISecretHasher, Sha256SecretHasher>();
```

- [ ] **Step 8: Build and run tests**

Run: `dotnet build src/OpenRussiaRouter.AuthService --no-restore && dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build`

- [ ] **Step 9: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Application/Abstractions/ src/OpenRussiaRouter.AuthService/Application/Services/ src/OpenRussiaRouter.AuthService/Application/Extensions/
git commit -m "refactor(auth): extract IPasswordHasher and ISecretHasher to fix DIP violation"
```

---

### Task 5: Fix InMemoryAuthRepository Behavioral Inconsistency

**Files:**
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/InMemoryAuthRepository.cs:136-164`

- [ ] **Step 1: Fix `DisableApiKeyAsync` to match PG behavior**

```csharp
public Task<bool> DisableApiKeyAsync(string apiKeyId, CancellationToken ct = default)
{
    lock (_gate)
    {
        if (!_apiKeys.TryGetValue(apiKeyId, out var key) || key.DisabledAtUtc.HasValue)
            return Task.FromResult(false); // Already disabled or not found

        _apiKeys[apiKeyId] = key with { DisabledAtUtc = DateTimeOffset.UtcNow };
        return Task.FromResult(true);
    }
}
```

- [ ] **Step 2: Fix `EnableApiKeyAsync` to match PG behavior**

```csharp
public Task<bool> EnableApiKeyAsync(string apiKeyId, CancellationToken ct = default)
{
    lock (_gate)
    {
        if (!_apiKeys.TryGetValue(apiKeyId, out var key) || !key.DisabledAtUtc.HasValue)
            return Task.FromResult(false); // Already enabled or not found

        _apiKeys[apiKeyId] = key with { DisabledAtUtc = null };
        return Task.FromResult(true);
    }
}
```

- [ ] **Step 3: Add `lock` to `ListUsersAsync`**

```csharp
public Task<IReadOnlyList<UserAccount>> ListUsersAsync(int limit, string? cursor, CancellationToken ct = default)
{
    lock (_gate)
    {
        // ... existing logic
    }
}
```

- [ ] **Step 4: Build and test**

Run: `dotnet build src/OpenRussiaRouter.AuthService --no-restore && dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build`

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Infrastructure/Repository/InMemoryAuthRepository.cs
git commit -m "fix(auth): align InMemoryAuthRepository behavior with PostgreSQL implementation"
```

---

### Task 6: Add Unit Tests for `AuthAccountApplicationService`

**Files:**
- Create: `tests/OpenRussiaRouter.AuthService.Tests/Application/AuthAccountApplicationServiceTests.cs`

- [ ] **Step 1: Create test class with key test cases**

```csharp
using FluentAssertions;
using Moq;
using OpenRussiaRouter.AuthService.Application.Abstractions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Entities;
using Xunit;

namespace OpenRussiaRouter.AuthService.Tests.Application;

public class AuthAccountApplicationServiceTests
{
    private readonly Mock<IAuthRepository> _repository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ISecretHasher> _secretHasher = new();
    private readonly Mock<ISecretGenerator> _secretGenerator = new();
    private readonly Mock<ILogger<AuthAccountApplicationService>> _logger = new();

    private AuthAccountApplicationService CreateSut() =>
        new(_repository.Object, _passwordHasher.Object, _secretHasher.Object,
            _secretGenerator.Object, _logger.Object);

    [Fact]
    public async Task RegisterUserAsync_NewUser_ReturnsSuccess()
    {
        // Arrange
        _repository.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);
        _repository.Setup(r => r.CreateUserAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserAccount.CreateForEmailSignup("test@example.com", "hashed", UserRole.User));

        var sut = CreateSut();

        // Act
        var result = await sut.RegisterUserAsync("test@example.com", "password123", default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.UserId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RegisterUserAsync_ExistingEmail_ReturnsFailed()
    {
        // Arrange
        _repository.Setup(r => r.GetUserByEmailAsync("taken@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserAccount.Restore("id", "taken@example.com", "hash", UserRole.User, UserStatus.Active, DateTimeOffset.UtcNow, null, null, null, null, 10, null));

        var sut = CreateSut();

        // Act
        var result = await sut.RegisterUserAsync("taken@example.com", "password123", default);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(AuthErrorCodes.EmailAlreadyRegistered);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task RegisterUserAsync_InvalidEmail_ReturnsFailed(string email)
    {
        var sut = CreateSut();
        var result = await sut.RegisterUserAsync(email, "password123", default);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CreateApiKeyAsync_ExceedsLimit_ReturnsFailed()
    {
        // Arrange
        var user = UserAccount.Restore("uid", "a@b.com", "hash", UserRole.User, UserStatus.Active,
            DateTimeOffset.UtcNow, null, null, null, null, 1, null); // max_api_keys = 1
        _repository.Setup(r => r.GetUserAsync("uid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _repository.Setup(r => r.CountActiveApiKeysAsync("uid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = CreateSut();

        // Act
        var result = await sut.CreateApiKeyAsync("uid", "My Key", default);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(AuthErrorCodes.ApiKeyLimitExceeded);
    }

    [Fact]
    public async Task DisableApiKeyAsync_NonexistentKey_Throws()
    {
        _repository.Setup(r => r.DisableApiKeyAsync("bad-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => sut.DisableApiKeyAsync("bad-id", default));
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/OpenRussiaRouter.AuthService.Tests --filter "FullyQualifiedName~AuthAccountApplicationServiceTests" --no-build`

- [ ] **Step 3: Commit**

```bash
git add tests/OpenRussiaRouter.AuthService.Tests/Application/
git commit -m "test(auth): add unit tests for AuthAccountApplicationService facade"
```

---

### Task 7: Eliminate Double Read in `UpdateUser`

**Files:**
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Grpc/GrpcAuthService.cs:414-439`
- Modify: `src/OpenRussiaRouter.AuthService/Application/Services/AuthAccountApplicationService.cs:139-170`

- [ ] **Step 1: Remove the first read in `GrpcAuthService.UpdateUser`**

```csharp
// BEFORE: GrpcAuthService reads user, then passes partial fields
var user = await repository.GetUserAsync(request.UserId, ct); // REMOVE THIS
var result = await accounts.UpdateUserAsync(request.UserId, request.DisplayName, ...);

// AFTER: Pass all fields, let facade handle optional semantics
var result = await accounts.UpdateUserAsync(
    request.UserId,
    request.HasDisplayName ? request.DisplayName : null,
    request.HasDisplayCurrency ? request.DisplayCurrency : null,
    request.HasMaxApiKeys ? request.MaxApiKeys : null,
    ct);
```

- [ ] **Step 2: Update `AuthAccountApplicationService.UpdateUserAsync` to handle null semantics**

Ensure the method uses null to mean "don't change" (current behavior via `coalesce` in SQL).

- [ ] **Step 3: Build and test**

Run: `dotnet build src/OpenRussiaRouter.AuthService --no-restore && dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-build`

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.AuthService/Infrastructure/Grpc/GrpcAuthService.cs src/OpenRussiaRouter.AuthService/Application/Services/AuthAccountApplicationService.cs
git commit -m "fix(auth): eliminate double database read in UpdateUser flow"
```

---

## Phase 3: BillingService

### Task 8: Fix Operator Precedence Bug

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs:1136`
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/InMemoryBillingRepository.cs:505`

- [ ] **Step 1: Add explicit parentheses**

```csharp
// BEFORE (BillingRepository.cs:1136)
if (target == PaymentStatus.Refunded || target == PaymentStatus.ChargedBack && autoReverseChargeback)

// AFTER
if (target == PaymentStatus.Refunded || (target == PaymentStatus.ChargedBack && autoReverseChargeback))
```

Apply same fix to `InMemoryBillingRepository.cs:505`.

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore && dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs src/OpenRussiaRouter.BillingService/Infrastructure/Repository/InMemoryBillingRepository.cs
git commit -m "fix(billing): add explicit parentheses to OR/AND condition in payment logic"
```

---

### Task 9: Rename `adminBypassDisabled` to `adminBypassEnabled`

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs:81-87`
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Grpc/GrpcBillingService.cs` (all callers)

- [ ] **Step 1: Rename parameter**

```csharp
// BEFORE
public async Task<BalanceCheckResult> CheckBalanceAndModelAsync(
    string userId, string modelId, bool adminBypassDisabled = false, ...)

// AFTER
public async Task<BalanceCheckResult> CheckBalanceAndModelAsync(
    string userId, string modelId, bool adminBypassEnabled = false, ...)
```

- [ ] **Step 2: Update body logic (swap condition)**

```csharp
var model = adminBypassEnabled
    ? await repository.GetModelAsync(resolvedModelId, ct)
    : await repository.GetEnabledModelAsync(resolvedModelId, ct);
```

- [ ] **Step 3: Update all callers in `GrpcBillingService`**

Replace `adminBypassDisabled: request.AdminBypassDisabled` → `adminBypassEnabled: request.AdminBypassDisabled`.

- [ ] **Step 4: Build and test**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore && dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build`

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs src/OpenRussiaRouter.BillingService/Infrastructure/Grpc/GrpcBillingService.cs
git commit -m "refactor(billing): rename adminBypassDisabled to adminBypassEnabled for clarity"
```

---

### Task 10: Fix `ListPublicModels` O(N*M) Lookup

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Grpc/GrpcBillingService.cs:49-83`

- [ ] **Step 1: Build dictionary by `ModelId` instead of `RouterModelId`**

```csharp
// BEFORE
var pricesByRouterId = (await billing.ListAllModelsAsync(ct).ConfigureAwait(false))
    .ToDictionary(model => model.RouterModelId, StringComparer.Ordinal);
// ...
var price = pricesByRouterId.Values.FirstOrDefault(p => p.ModelId == model.Id);

// AFTER
var pricesById = (await billing.ListAllModelsAsync(ct).ConfigureAwait(false))
    .ToDictionary(model => model.ModelId, StringComparer.Ordinal);
// ...
var price = pricesById.GetValueOrDefault(model.Id);
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore && dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Infrastructure/Grpc/GrpcBillingService.cs
git commit -m "fix(billing): use ModelId as dictionary key in ListPublicModels for O(1) lookup"
```

---

### Task 11: Remove Dead Code from `IBillingRepository`

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs:24,74`
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs`
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/InMemoryBillingRepository.cs`

- [ ] **Step 1: Remove `AddUsageAsync` from interface and implementations**

Delete the method declaration from `IBillingRepository.cs:24` and implementations in both repositories.

- [ ] **Step 2: Remove `FindPaymentAsync` from interface and implementations**

Delete the method declaration from `IBillingRepository.cs:74` and implementations in both repositories.

- [ ] **Step 3: Build and test**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore && dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build`

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/
git commit -m "chore(billing): remove unused AddUsageAsync and FindPaymentAsync from repository"
```

---

### Task 12: Extract `PaymentOptions` from `FxQuoteService.cs`

**Files:**
- Create: `src/OpenRussiaRouter.BillingService/Application/Services/PaymentOptions.cs`
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/FxQuoteService.cs`

- [ ] **Step 1: Create `PaymentOptions.cs`**

```csharp
namespace OpenRussiaRouter.BillingService.Application.Services;

public sealed class PaymentOptions
{
    public bool AutoReverseChargeback { get; set; }
    public int LeaseDurationSeconds { get; set; } = 30;
}
```

- [ ] **Step 2: Remove `PaymentOptions` from `FxQuoteService.cs`**

Delete lines 22-27 (the `PaymentOptions` record).

- [ ] **Step 3: Build and test**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore && dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build`

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Application/Services/PaymentOptions.cs src/OpenRussiaRouter.BillingService/Application/Services/FxQuoteService.cs
git commit -m "refactor(billing): extract PaymentOptions to separate file"
```

---

## Phase 4: ControlPlane

### Task 13: Fix `NodeModelCapabilityStore.ReplaceAll()` Atomicity

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/Application/Services/NodeModelCapabilityStore.cs:30-36`

- [ ] **Step 1: Implement atomic swap**

```csharp
// BEFORE (non-atomic: Clear + add loop)
public void ReplaceAll(IReadOnlyDictionary<string, IReadOnlyList<string>> nodeModels)
{
    _nodeModels.Clear();
    foreach (var (nodeId, modelIds) in nodeModels)
        _nodeModels[nodeId] = modelIds.ToList();
}

// AFTER (atomic reference swap)
public void ReplaceAll(IReadOnlyDictionary<string, IReadOnlyList<string>> nodeModels)
{
    var newStore = new ConcurrentDictionary<string, IReadOnlyList<string>>(
        StringComparer.Ordinal);
    foreach (var (nodeId, modelIds) in nodeModels)
        newStore[nodeId] = modelIds.ToList();
    Volatile.Write(ref _nodeModels, newStore);
}
```

- [ ] **Step 2: Update `_nodeModels` field to be `volatile`-compatible**

```csharp
private volatile ConcurrentDictionary<string, IReadOnlyList<string>> _nodeModels = new();
```

- [ ] **Step 3: Build and test**

Run: `dotnet build src/OpenRussiaRouter.ControlPlane --no-restore && dotnet test tests/OpenRussiaRouter.ControlPlane.Tests --no-build`

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.ControlPlane/Application/Services/NodeModelCapabilityStore.cs
git commit -m "fix(control): make NodeModelCapabilityStore.ReplaceAll atomic via reference swap"
```

---

### Task 14: Fix `CheckNodeIntegrationKeysSync` Semantic Mismatch

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Grpc/GrpcRouterControlPlaneService.cs:425-454`

- [ ] **Step 1: Fix dictionary key to use `RawKey` consistently**

```csharp
// BEFORE (line 425)
dbKeysDict = dbKeysResponse.Keys.ToDictionary(k => k.RawKey, k => k);
// BEFORE (line 435)
nodeKeysDict.TryGetValue(dbKey.RawKey, out var nodeKey)

// The fix depends on what field the node uses as ID.
// If node uses name/keyPreview as ID, match accordingly.
// If node has RawKey in its response, use that.
```

- [ ] **Step 2: Verify node key structure and align matching**

Read `NodeAdminContracts.cs` to confirm what `RouterIntegrationKeyInfo.Id` represents, then fix the matching logic.

- [ ] **Step 3: Build and test**

Run: `dotnet build src/OpenRussiaRouter.ControlPlane --no-restore && dotnet test tests/OpenRussiaRouter.ControlPlane.Tests --no-build`

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.ControlPlane/Infrastructure/Grpc/GrpcRouterControlPlaneService.cs
git commit -m "fix(control): fix semantic mismatch in CheckNodeIntegrationKeysSync key comparison"
```

---

### Task 15: Stop Leaking `ControlToken` in `ListRouterNodes`

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Grpc/GrpcRouterControlPlaneService.cs:471-483`

- [ ] **Step 1: Remove `ControlToken` from `MapNodeSummary`**

```csharp
// BEFORE
private static RouterNodeSummary MapNodeSummary(RouterNode node) => new()
{
    NodeId = node.Id,
    Name = node.Name,
    EndpointUrl = node.EndpointUrl.ToString(),
    Status = MapStatus(node.Status),
    Enabled = node.Enabled,
    ControlToken = node.ControlToken // REMOVE THIS
};

// AFTER
private static RouterNodeSummary MapNodeSummary(RouterNode node) => new()
{
    NodeId = node.Id,
    Name = node.Name,
    EndpointUrl = node.EndpointUrl.ToString(),
    Status = MapStatus(node.Status),
    Enabled = node.Enabled
};
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.ControlPlane --no-restore && dotnet test tests/OpenRussiaRouter.ControlPlane.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.ControlPlane/Infrastructure/Grpc/GrpcRouterControlPlaneService.cs
git commit -m "fix(control): stop leaking ControlToken in ListRouterNodes response"
```

---

### Task 16: Fix `ControlPlanePersistenceMapper.ToDomain()` Side Effects

**Files:**
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Persistence/ControlPlanePersistenceMapper.cs:27-56`
- Modify: `src/OpenRussiaRouter.ControlPlane/Domain/Entities/RouterNode.cs`

- [ ] **Step 1: Add state-restoring constructor to `RouterNode`**

```csharp
// In RouterNode.cs — add factory for restoring from DB
public static RouterNode Restore(
    string id, string name, Uri endpointUrl, string controlToken,
    RouterNodeStatus status, bool enabled, int inFlightRequests,
    int capacityWeight, DateTimeOffset createdAtUtc) => new(...)
{
    // Set fields directly without calling Lease/Disable methods
};
```

- [ ] **Step 2: Update mapper to use `Restore` instead of method calls**

```csharp
// BEFORE
var node = new RouterNode(...);
if (!entity.Enabled) node.Disable();
node.ApplyHealth(MapStatus(entity.Status));
for (var i = 0; i < entity.InFlightRequests; i++) node.Lease();

// AFTER
var node = RouterNode.Restore(
    entity.Id, entity.Name, new Uri(entity.EndpointUrl), entity.ControlToken,
    MapStatus(entity.Status), entity.Enabled, entity.InFlightRequests,
    entity.CapacityWeight, entity.CreatedAtUtc);
```

- [ ] **Step 3: Build and test**

Run: `dotnet build src/OpenRussiaRouter.ControlPlane --no-restore && dotnet test tests/OpenRussiaRouter.ControlPlane.Tests --no-build`

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.ControlPlane/Infrastructure/Persistence/ControlPlanePersistenceMapper.cs src/OpenRussiaRouter.ControlPlane/Domain/Entities/RouterNode.cs
git commit -m "refactor(control): eliminate side effects in ControlPlanePersistenceMapper.ToDomain"
```

---

## Phase 5: ApiGateway

### Task 17: Fix `StreamingHttpClient.Timeout`

**Files:**
- Modify: `src/OpenRussiaRouter.ApiGateway/Router/YarpRouterProxyProvider.cs:28-36`

- [ ] **Step 1: Set explicit timeout**

```csharp
private static readonly HttpClient StreamingHttpClient = new(new SocketsHttpHandler
{
    PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
    ConnectTimeout = TimeSpan.FromSeconds(30)
})
{
    Timeout = TimeSpan.FromMinutes(30) // Allow long-running streams
};
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.ApiGateway --no-restore && dotnet test tests/OpenRussiaRouter.ApiGateway.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.ApiGateway/Router/YarpRouterProxyProvider.cs
git commit -m "fix(gateway): set StreamingHttpClient timeout to 30 minutes for long streams"
```

---

### Task 18: Extract `SocketsHttpHandler` Factory to BuildingBlocks

**Files:**
- Create: `src/OpenRussiaRouter.BuildingBlocks/Grpc/GrpcClientBuilderExtensions.cs`
- Modify: `src/OpenRussiaRouter.ApiGateway/Application/Extensions/ApiGatewayExtensions.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs`
- Modify: `src/OpenRussiaRouter.ControlPlane/Infrastructure/Startup/Startup.cs`
- Modify: `src/OpenRussiaRouter.CollectorService/Infrastructure/Startup/Startup.cs`
- Modify: `src/OpenRussiaRouter.CollectorService/Infrastructure/Startup/Startup.cs`

- [ ] **Step 1: Create shared extension method**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Grpc.Net.Client;
using System.Net;

namespace OpenRussiaRouter.BuildingBlocks.Grpc;

public static class GrpcClientBuilderExtensions
{
    public static IServiceCollection AddStandardGrpcClient<TClient>(
        this IServiceCollection services, string address)
        where TClient : class
    {
        services.AddGrpcClient<TClient>(options =>
        {
            options.Address = new Uri(address);
        })
        .ConfigureChannel(options =>
        {
            options.HttpHandler = CreateHandler();
        });
        return services;
    }

    private static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        ConnectTimeout = TimeSpan.FromSeconds(30)
    };
}
```

- [ ] **Step 2: Update ApiGateway to use shared extension**

Replace 3 duplicate `SocketsHttpHandler` blocks in `ApiGatewayExtensions.cs` with `AddStandardGrpcClient<T>(url)`.

- [ ] **Step 3: Update WebApp `Program.cs`**

Replace 4 duplicate blocks with `AddStandardGrpcClient<T>(url)`.

- [ ] **Step 4: Update ControlPlane `Startup.cs`**

Replace 2 duplicate blocks.

- [ ] **Step 5: Update CollectorService `Startup.cs`**

Replace 2 duplicate blocks.

- [ ] **Step 6: Delete dead `GatewayOptions.cs`**

Remove `GatewayGrpcOptions` and `GatewayHttpClientOptions` from `ApiGateway/Application/Extensions/GatewayOptions.cs`.

- [ ] **Step 7: Build all services and run tests**

```bash
dotnet build OpenRussiaRouter.sln --no-restore
dotnet test OpenRussiaRouter.sln --no-build
```

- [ ] **Step 8: Commit**

```bash
git add src/OpenRussiaRouter.BuildingBlocks/Grpc/ src/OpenRussiaRouter.ApiGateway/ src/OpenRussiaRouter.WebApp/ src/OpenRussiaRouter.ControlPlane/ src/OpenRussiaRouter.CollectorService/
git commit -m "refactor: extract SocketsHttpHandler factory to BuildingBlocks, eliminate 10+ duplicates"
```

---

### Task 19: Fix `GatewayErrors.WriteAsync` Missing Correlation Headers

**Files:**
- Modify: `src/OpenRussiaRouter.ApiGateway/Http/GatewayErrors.cs:29-47`

- [ ] **Step 1: Add correlation header to `WriteAsync`**

```csharp
public static async Task WriteAsync(HttpContext context, int statusCode, string type, string code, string message, string? correlationId = null)
{
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json";

    if (!string.IsNullOrEmpty(correlationId))
        context.Response.Headers["x-correlation-id"] = correlationId;

    var payload = JsonSerializer.Serialize(new
    {
        error = new { type, code, message, correlation_id = correlationId }
    });
    await context.Response.WriteAsync(payload);
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.ApiGateway --no-restore && dotnet test tests/OpenRussiaRouter.ApiGateway.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.ApiGateway/Http/GatewayErrors.cs
git commit -m "fix(gateway): add x-correlation-id header to GatewayErrors.WriteAsync"
```

---

## Phase 6: WebApp

### Task 20: Extract Interfaces for Provider Classes

**Files:**
- Create: `src/OpenRussiaRouter.WebApp/Services/Auth/IAuthProvider.cs`
- Create: `src/OpenRussiaRouter.WebApp/Services/Billing/IBillingProvider.cs`
- Create: `src/OpenRussiaRouter.WebApp/Services/ControlPlane/IControlPlaneProvider.cs`
- Create: `src/OpenRussiaRouter.WebApp/Services/Collector/ICollectorProvider.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs`
- Modify: All `Pages/**/*.cshtml.cs` that inject providers

- [ ] **Step 1: Create `IAuthProvider`**

Extract public methods from `AuthProvider` into interface.

- [ ] **Step 2: Create `IBillingProvider`**

Extract public methods from `BillingProvider` into interface.

- [ ] **Step 3: Create `IControlPlaneProvider`**

Extract public methods from `ControlPlaneProvider` into interface.

- [ ] **Step 4: Create `ICollectorProvider`**

Extract public methods from `CollectorProvider` into interface.

- [ ] **Step 5: Make concrete classes implement interfaces**

- [ ] **Step 6: Update DI registration**

```csharp
services.AddScoped<IAuthProvider, AuthProvider>();
services.AddScoped<IBillingProvider, BillingProvider>();
services.AddScoped<IControlPlaneProvider, ControlPlaneProvider>();
services.AddScoped<ICollectorProvider, CollectorProvider>();
```

- [ ] **Step 7: Update all page models to use interfaces**

Replace `BillingProvider` → `IBillingProvider`, etc. in constructors.

- [ ] **Step 8: Update test fakes to implement interfaces instead of inheriting concrete class**

- [ ] **Step 9: Build and test**

Run: `dotnet build src/OpenRussiaRouter.WebApp --no-restore && dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-build`

- [ ] **Step 10: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Services/ src/OpenRussiaRouter.WebApp/Program.cs src/OpenRussiaRouter.WebApp/Pages/
git commit -m "refactor(webapp): extract interfaces for all gRPC provider classes"
```

---

### Task 21: Fix N+1 Query in Users Admin Page

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Admin/Users.cshtml.cs:83-103`

- [ ] **Step 1: Parallelize balance lookups**

```csharp
// BEFORE (sequential N+1)
foreach (var user in users)
{
    var balance = await _billing.GetBalanceAsync(user.Id);
    userBalances[user.Id] = balance;
}

// AFTER (parallel)
var balanceTasks = users.Select(async user =>
{
    var balance = await _billing.GetBalanceAsync(user.Id);
    return (user.Id, Balance: balance);
});
var balances = await Task.WhenAll(balanceTasks);
foreach (var (userId, balance) in balances)
    userBalances[userId] = balance;
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.WebApp --no-restore && dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Pages/Admin/Users.cshtml.cs
git commit -m "perf(webapp): parallelize balance lookups in Users admin page"
```

---

### Task 22: Fix `NodeEditModel` Redundant List Fetch

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Admin/NodeEdit.cshtml.cs:108-111`

- [ ] **Step 1: Use `GetRouterNodeDetailsAsync` instead of `ListRouterNodesAsync`**

```csharp
// BEFORE
var nodes = await _controlPlane.ListRouterNodesAsync();
var node = nodes.FirstOrDefault(n => n.Id == nodeId);

// AFTER
var node = await _controlPlane.GetRouterNodeDetailsAsync(nodeId);
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.WebApp --no-restore && dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Pages/Admin/NodeEdit.cshtml.cs
git commit -m "perf(webapp): use GetRouterNodeDetailsAsync instead of listing all nodes"
```

---

### Task 23: Add Null Check in `LoginModel.OnGet`

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs:33`

- [ ] **Step 1: Add null guard**

```csharp
// BEFORE
return RedirectTo(User.FindFirstValue(ClaimTypes.Role));

// AFTER
var role = User.FindFirstValue(ClaimTypes.Role);
return RedirectTo(string.IsNullOrEmpty(role) ? "/Workspace" : $"/{role}");
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/OpenRussiaRouter.WebApp --no-restore && dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-build`

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Pages/Login.cshtml.cs
git commit -m "fix(webapp): add null check for role claim in LoginModel redirect"
```

---

## Execution Summary

| Phase | Tasks | Effort |
|-------|-------|--------|
| Phase 1: BuildingBlocks | 3 tasks | Small |
| Phase 2: AuthService | 4 tasks | Medium |
| Phase 3: BillingService | 5 tasks | Medium |
| Phase 4: ControlPlane | 4 tasks | Medium |
| Phase 5: ApiGateway | 3 tasks | Medium |
| Phase 6: WebApp | 4 tasks | Medium |
| **Total** | **23 tasks** | **~2-3 days** |

**Two execution options:**

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration
2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
