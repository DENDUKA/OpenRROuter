# Credits Top-Up History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display the full VirtualTopUp ledger history for the authenticated user on the `/workspace/credits` page.

**Architecture:** Add a `ListTopUps(userId)` query path from the gRPC proto through BillingService domain/repository layers to the WebApp's Credits page. Filter `billing.ledger_entries` by `user_id` and `type = 'VirtualTopUp'`, ordered by date descending.

**Tech Stack:** .NET 8, gRPC/protobuf, Dapper/PostgreSQL, ASP.NET Core Razor Pages

---

## File Map

| Action | File |
|--------|------|
| Modify | `src/OpenRussiaRouter.Contracts/Protos/billing.proto` |
| Modify | `src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs` |
| Modify | `src/OpenRussiaRouter.BillingService/Application/Services/InMemoryBillingRepository.cs` |
| Modify | `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs` |
| Modify | `src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs` |
| Modify | `src/OpenRussiaRouter.BillingService/Application/Services/GrpcBillingService.cs` |
| Create | `src/OpenRussiaRouter.WebApp/Services/Billing/TopUpInfo.cs` |
| Modify | `src/OpenRussiaRouter.WebApp/Services/Billing/BillingClient.cs` |
| Modify | `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml.cs` |
| Modify | `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml` |
| Modify | `tests/OpenRussiaRouter.BillingService.Tests/Application/BillingApplicationServiceTests.cs` |
| Modify | `tests/OpenRussiaRouter.BillingService.Tests/Application/GrpcBillingServiceTests.cs` |

---

### Task 1: Add ListTopUps RPC to billing.proto

**Files:**
- Modify: `src/OpenRussiaRouter.Contracts/Protos/billing.proto`

- [ ] **Step 1: Add RPC declaration and messages to billing.proto**

  In the `service BillingService { ... }` block, add the new RPC after `ListActivityByToken`:

  ```protobuf
  rpc ListTopUps (ListTopUpsRequest) returns (ListTopUpsResponse);
  ```

  Add these message definitions at the end of the file (before the closing of the file):

  ```protobuf
  message ListTopUpsRequest {
    string user_id = 1;
  }

  message TopUpRecord {
    string id = 1;
    string amount_rub = 2;
    string reason = 3;
    string created_at_utc = 4;
  }

  message ListTopUpsResponse {
    repeated TopUpRecord records = 1;
  }
  ```

- [ ] **Step 2: Build the solution to verify proto code generation succeeds**

  ```powershell
  dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
  ```

  Expected: Build succeeded. The generated `Billing.cs` and `BillingGrpc.cs` in the `obj/` folder will now contain `ListTopUpsRequest`, `TopUpRecord`, `ListTopUpsResponse`, and `BillingServiceBase.ListTopUps`.

- [ ] **Step 3: Commit**

  ```powershell
  git add src/OpenRussiaRouter.Contracts/Protos/billing.proto
  git commit -m "feat: add ListTopUps RPC to billing proto"
  ```

---

### Task 2: Write failing tests for BillingApplicationService.ListTopUps

**Files:**
- Modify: `tests/OpenRussiaRouter.BillingService.Tests/Application/BillingApplicationServiceTests.cs`

- [ ] **Step 1: Add two failing tests at the end of `BillingApplicationServiceTests`**

  Add after the existing `RecordUsage_NegativeTokenCount_CreatesAnomaly` test, before the private helpers:

  ```csharp
  [Fact]
  public void ListTopUps_AfterTopUps_ReturnsTopUpsForUser()
  {
      var service = CreateService();
      var now = DateTimeOffset.UtcNow;
      service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "first", "key-1", now);
      service.CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(200), "second", "key-2", now.AddSeconds(1));

      var topUps = service.ListTopUps("user-1");

      Assert.Equal(2, topUps.Count);
  }

  [Fact]
  public void ListTopUps_OtherUserTopUps_NotReturned()
  {
      var service = CreateService();
      service.CreateVirtualTopUp("admin-1", "user-2", MoneyAmount.Rub(100), "other", "key-3", DateTimeOffset.UtcNow);

      var topUps = service.ListTopUps("user-1");

      Assert.Empty(topUps);
  }
  ```

- [ ] **Step 2: Run tests to verify they fail with "method not found"**

  ```powershell
  dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1 --filter "ListTopUps"
  ```

  Expected: compilation error — `BillingApplicationService` does not contain a definition for `ListTopUps`.

---

### Task 3: Add ListTopUps to IBillingRepository and InMemoryBillingRepository

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs`
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/InMemoryBillingRepository.cs`

- [ ] **Step 1: Add method signature to IBillingRepository**

  In `IBillingRepository.cs`, add after `AddLedgerEntry`:

  ```csharp
  (IReadOnlyCollection<LedgerEntry> Items, bool HasMore) ListTopUps(string userId, int limit);
  ```

- [ ] **Step 2: Implement in InMemoryBillingRepository**

  In `InMemoryBillingRepository.cs`, add this `using` at the top of the file (it is not present yet):

  ```csharp
  using OpenRussiaRouter.BillingService.Domain.Enums;
  ```

  Then add the method after `AddLedgerEntry`:

  ```csharp
  public (IReadOnlyCollection<LedgerEntry> Items, bool HasMore) ListTopUps(string userId, int limit)
  {
      var items = _ledgerByIdempotencyKey.Values
          .Where(e => e.UserId == userId && e.Type == LedgerEntryType.VirtualTopUp)
          .OrderByDescending(e => e.CreatedAtUtc)
          .Take(limit + 1)
          .ToArray();

      var hasMore = items.Length > limit;
      if (hasMore)
      {
          items = items.Take(limit).ToArray();
      }

      return (items, hasMore);
  }
  ```

- [ ] **Step 3: Build to confirm both implementations compile**

  ```powershell
  dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
  ```

  Expected: Build succeeded (BillingRepository will fail to compile until Task 4).

---

### Task 4: Implement ListTopUps in BillingRepository (PostgreSQL)

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs`

- [ ] **Step 1: Add LedgerEntrySelectSql constant and ListTopUps method**

  At the bottom of `BillingRepository`, alongside the existing `private const string` constants, add:

  ```csharp
  private const string LedgerEntrySelectSql = """
      select
          id as Id,
          user_id as UserId,
          type as Type,
          amount as Amount,
          currency as Currency,
          idempotency_key as IdempotencyKey,
          reason as Reason,
          created_at_utc as CreatedAtUtc
      from billing.ledger_entries

      """;
  ```

  Then add the method to the class body (after `AddLedgerEntry` is a good place):

  ```csharp
  public (IReadOnlyCollection<LedgerEntry> Items, bool HasMore) ListTopUps(string userId, int limit)
  {
      using var connection = connectionFactory.CreateConnection();
      var items = connection.Query<LedgerEntryEntity>(
              LedgerEntrySelectSql +
              "where user_id = @UserId and type = 'VirtualTopUp'\norder by created_at_utc desc\nlimit @Limit;",
              new { UserId = userId, Limit = limit + 1 })
          .Select(BillingPersistenceMapper.ToDomain)
          .ToArray();

      var hasMore = items.Length > limit;
      if (hasMore)
      {
          items = items.Take(limit).ToArray();
      }

      return (items, hasMore);
  }
  ```

- [ ] **Step 2: Build to confirm it compiles**

  ```powershell
  dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
  ```

  Expected: Build succeeded.

- [ ] **Step 3: Commit**

  ```powershell
  git add src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs
  git add src/OpenRussiaRouter.BillingService/Application/Services/InMemoryBillingRepository.cs
  git add src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs
  git commit -m "feat: add ListTopUps to billing repository"
  ```

---

### Task 5: Add ListTopUps to BillingApplicationService and run tests

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs`

- [ ] **Step 1: Add ListTopUps method to BillingApplicationService**

  Add after `GetBalance`:

  ```csharp
  public IReadOnlyCollection<LedgerEntry> ListTopUps(string userId)
  {
      var pageSize = Math.Min(paginationOptions.Value.DefaultPageSize, paginationOptions.Value.MaxPageSize);
      return repository.ListTopUps(RequireValue(userId, nameof(userId)), pageSize).Items;
  }
  ```

- [ ] **Step 2: Run the previously written tests to confirm they pass**

  ```powershell
  dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1 --filter "ListTopUps"
  ```

  Expected: 2 tests pass — `ListTopUps_AfterTopUps_ReturnsTopUpsForUser` and `ListTopUps_OtherUserTopUps_NotReturned`.

- [ ] **Step 3: Run full test suite to confirm no regressions**

  ```powershell
  dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1
  ```

  Expected: All tests pass.

- [ ] **Step 4: Commit**

  ```powershell
  git add src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs
  git add tests/OpenRussiaRouter.BillingService.Tests/Application/BillingApplicationServiceTests.cs
  git commit -m "feat: add ListTopUps to BillingApplicationService"
  ```

---

### Task 6: Implement ListTopUps in GrpcBillingService with test

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/GrpcBillingService.cs`
- Modify: `tests/OpenRussiaRouter.BillingService.Tests/Application/GrpcBillingServiceTests.cs`

- [ ] **Step 1: Write failing gRPC test in GrpcBillingServiceTests**

  Add after `ListActivityByToken_ReturnsOnlyRequestedKeys`, before the `CreateBillingWithModelAndBalance` helper:

  ```csharp
  [Fact]
  public async Task ListTopUps_ExistingTopUp_ReturnsRecord()
  {
      var billing = CreateBillingWithModelAndBalance();
      var service = new GrpcBillingService(billing);

      var response = await service.ListTopUps(
          new ListTopUpsRequest { UserId = "user-1" },
          context: null!);

      var record = Assert.Single(response.Records);
      Assert.NotEmpty(record.Id);
      Assert.Equal("100", record.AmountRub);
      Assert.Equal("test", record.Reason);
      Assert.NotEmpty(record.CreatedAtUtc);
  }
  ```

  Note: `CreateBillingWithModelAndBalance` already calls `CreateVirtualTopUp("admin-1", "user-1", MoneyAmount.Rub(100), "test", "topup-1", ...)`, so the test data is already there.

- [ ] **Step 2: Run test to confirm it fails**

  ```powershell
  dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1 --filter "ListTopUps_ExistingTopUp"
  ```

  Expected: compilation error — `GrpcBillingService` does not override `ListTopUps`.

- [ ] **Step 3: Implement ListTopUps override in GrpcBillingService**

  Add after `ListActivityByToken` and before `ParseTimestamp`:

  ```csharp
  public override Task<ListTopUpsResponse> ListTopUps(
      ListTopUpsRequest request,
      ServerCallContext context)
  {
      var response = new ListTopUpsResponse();
      response.Records.AddRange(billing.ListTopUps(request.UserId).Select(entry => new TopUpRecord
      {
          Id = entry.Id,
          AmountRub = entry.Amount.Amount.ToString(CultureInfo.InvariantCulture),
          Reason = entry.Reason,
          CreatedAtUtc = entry.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
      }));
      return Task.FromResult(response);
  }
  ```

- [ ] **Step 4: Run all billing tests**

  ```powershell
  dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1
  ```

  Expected: All tests pass including the new `ListTopUps_ExistingTopUp_ReturnsRecord`.

- [ ] **Step 5: Commit**

  ```powershell
  git add src/OpenRussiaRouter.BillingService/Application/Services/GrpcBillingService.cs
  git add tests/OpenRussiaRouter.BillingService.Tests/Application/GrpcBillingServiceTests.cs
  git commit -m "feat: implement ListTopUps gRPC handler"
  ```

---

### Task 7: Add TopUpInfo DTO and BillingClient.ListTopUpsAsync in WebApp

**Files:**
- Create: `src/OpenRussiaRouter.WebApp/Services/Billing/TopUpInfo.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Services/Billing/BillingClient.cs`

- [ ] **Step 1: Create TopUpInfo DTO**

  Create `src/OpenRussiaRouter.WebApp/Services/Billing/TopUpInfo.cs`:

  ```csharp
  namespace OpenRussiaRouter.WebApp.Services.Billing;

  public sealed record TopUpInfo(decimal Amount, string Reason, DateTimeOffset CreatedAtUtc);
  ```

- [ ] **Step 2: Add ListTopUpsAsync to BillingClient**

  In `BillingClient.cs`, add after `CreateVirtualTopUpAsync`:

  ```csharp
  public async Task<IReadOnlyList<TopUpInfo>> ListTopUpsAsync(string userId, CancellationToken cancellationToken = default)
  {
      var response = await client.ListTopUpsAsync(
          new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListTopUpsRequest { UserId = userId },
          cancellationToken: cancellationToken);
      return response.Records.Select(r => new TopUpInfo(
          decimal.Parse(r.AmountRub, System.Globalization.CultureInfo.InvariantCulture),
          r.Reason,
          DateTimeOffset.Parse(r.CreatedAtUtc, System.Globalization.CultureInfo.InvariantCulture))).ToList();
  }
  ```

- [ ] **Step 3: Build to confirm it compiles**

  ```powershell
  dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
  ```

  Expected: Build succeeded.

- [ ] **Step 4: Commit**

  ```powershell
  git add src/OpenRussiaRouter.WebApp/Services/Billing/TopUpInfo.cs
  git add src/OpenRussiaRouter.WebApp/Services/Billing/BillingClient.cs
  git commit -m "feat: add TopUpInfo DTO and ListTopUpsAsync to BillingClient"
  ```

---

### Task 8: Update Credits page — load and display top-up history

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml`

- [ ] **Step 1: Update Credits.cshtml.cs to load top-up history**

  Replace the entire file content:

  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using OpenRussiaRouter.WebApp.Services.Billing;
  using OpenRussiaRouter.WebApp.Services.State;

  namespace OpenRussiaRouter.WebApp.Pages.Workspace;

  [Authorize]
  public class CreditsModel : PageModelBase
  {
      private readonly BillingClient _billingClient;

      public BalanceInfo? Balance { get; set; }
      public IReadOnlyList<TopUpInfo> TopUps { get; set; } = [];
      public string? ErrorMessage { get; set; }

      public CreditsModel(BillingClient billingClient) => _billingClient = billingClient;

      public async Task OnGetAsync()
      {
          try { Balance = await _billingClient.GetBalanceAsync(User.GetUserId()); }
          catch { ErrorMessage = "Failed to load balance."; }

          try { TopUps = await _billingClient.ListTopUpsAsync(User.GetUserId()); }
          catch { /* show empty table on error */ }
      }
  }
  ```

- [ ] **Step 2: Update Credits.cshtml to add the top-up history table**

  Replace the entire file content:

  ```razor
  @page
  @model Workspace.CreditsModel
  @{ Layout = "~/Pages/Shared/_WorkspaceLayout.cshtml"; ViewData["Title"] = "Credits"; }

  <div class="ws-section-header">
      <h2>Credits</h2>
      <p class="ws-section-desc">Your current RUB balance used for API request billing.</p>
  </div>

  @if (Model.ErrorMessage != null)
  {
      <div class="alert alert-danger">@Model.ErrorMessage</div>
  }

  <div class="ws-panel">
      <div class="ws-panel-header">Current Balance</div>
      <div class="ws-panel-body">
          @if (Model.Balance == null)
          {
              <p class="text-muted">Unable to load balance.</p>
          }
          else
          {
              <div class="ws-balance-amount">
                  @Model.Balance.Amount.ToString("N2") <span class="ws-balance-currency">RUB</span>
              </div>
          }
      </div>
  </div>

  <div class="ws-panel">
      <div class="ws-panel-header">Top-Up History</div>
      <div class="ws-panel-body">
          @if (Model.TopUps.Count == 0)
          {
              <p class="text-muted">No top-ups yet.</p>
          }
          else
          {
              <table class="table">
                  <thead>
                      <tr>
                          <th>Date</th>
                          <th>Amount</th>
                          <th>Reason</th>
                      </tr>
                  </thead>
                  <tbody>
                      @foreach (var topUp in Model.TopUps)
                      {
                          <tr>
                              <td>@topUp.CreatedAtUtc.ToLocalTime().ToString("g")</td>
                              <td>@topUp.Amount.ToString("N2") RUB</td>
                              <td>@topUp.Reason</td>
                          </tr>
                      }
                  </tbody>
              </table>
          }
      </div>
  </div>
  ```

- [ ] **Step 3: Build the full solution**

  ```powershell
  dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
  ```

  Expected: Build succeeded.

- [ ] **Step 4: Run all tests**

  ```powershell
  dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
  ```

  Expected: All tests pass.

- [ ] **Step 5: Commit**

  ```powershell
  git add src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml.cs
  git add src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml
  git commit -m "feat: display top-up history on credits page"
  ```
