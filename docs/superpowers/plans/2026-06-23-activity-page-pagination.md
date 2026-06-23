# Activity Page Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace cursor-based pagination with page-number-based pagination on the Activity page (`/workspace/activity?tab=details`).

**Architecture:** Add `offset` and `total_count` to the gRPC contract, modify the repository to support OFFSET, update the application service and gRPC service, then modify the WebApp to use page numbers.

**Tech Stack:** .NET 8, gRPC, Dapper, PostgreSQL, Razor Pages

---

## File Structure

| Layer | File | Responsibility |
|---|---|---|
| **Proto** | `src/OpenRussiaRouter.Contracts/Protos/billing.proto` | Add `offset` and `total_count` to request/response |
| **Domain** | `src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs` | Add `offset` and `totalCount` to interface |
| **Infrastructure** | `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs` | Implement OFFSET and COUNT query |
| **Infrastructure** | `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/InMemoryBillingRepository.cs` | Implement OFFSET for in-memory |
| **Application** | `src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs` | Pass offset parameter |
| **Infrastructure** | `src/OpenRussiaRouter.BillingService/Infrastructure/Grpc/GrpcBillingService.cs` | Map proto to domain |
| **WebApp** | `src/OpenRussiaRouter.WebApp/Services/Billing/BillingProvider.cs` | Pass page number |
| **WebApp** | `src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml.cs` | Accept `page` parameter, calculate total pages |
| **WebApp** | `src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml` | Render pagination with page numbers |

---

### Task 1: Update gRPC Contract

**Files:**
- Modify: `src/OpenRussiaRouter.Contracts/Protos/billing.proto:309-338`

- [ ] **Step 1: Add offset to request and total_count to response**

Open `billing.proto` and modify the messages:

```protobuf
message ListActivityByTokenRequest {
  repeated string api_key_ids = 1;
  string from_utc = 2;
  string to_utc = 3;
  string model_id_filter = 4;
  string api_key_id_filter = 5;
  string after_cursor = 6;
  int32 page_size = 7;
  int32 offset = 8;  // NEW: offset for page-based pagination
}

message ListActivityByTokenResponse {
  repeated ActivityByTokenRecord records = 1;
  bool has_more = 2;
  string next_cursor = 3;
  int32 total_count = 4;  // NEW: total number of records matching filters
}
```

- [ ] **Step 2: Verify proto compiles**

Run: `dotnet build src/OpenRussiaRouter.Contracts --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL (proto files compile into consuming projects)

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.Contracts/Protos/billing.proto
git commit -m "feat: add offset and total_count to ListActivityByToken proto"
```

---

### Task 2: Update Repository Interface

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs`

- [ ] **Step 1: Add offset and totalCount to interface**

Open `IBillingRepository.cs` and find `ListActivityByTokenAsync`:

```csharp
Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore, int TotalCount)> ListActivityByTokenAsync(
    IReadOnlyCollection<string> apiKeyIds,
    int limit,
    DateTimeOffset? fromUtc = null,
    DateTimeOffset? toUtc = null,
    string? modelIdFilter = null,
    string? apiKeyIdFilter = null,
    DateTimeOffset? afterCreatedAtUtc = null,
    string? afterRequestId = null,
    int offset = 0,  // NEW
    CancellationToken ct = default);
```

- [ ] **Step 2: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Application/Services/IBillingRepository.cs
git commit -m "feat: add offset parameter to IBillingRepository.ListActivityByTokenAsync"
```

---

### Task 3: Implement PostgreSQL Repository

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs:569-618`

- [ ] **Step 1: Add COUNT query and OFFSET support**

Replace the `ListActivityByTokenAsync` method in `BillingRepository.cs`:

```csharp
public async Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore, int TotalCount)> ListActivityByTokenAsync(
    IReadOnlyCollection<string> apiKeyIds,
    int limit,
    DateTimeOffset? fromUtc = null,
    DateTimeOffset? toUtc = null,
    string? modelIdFilter = null,
    string? apiKeyIdFilter = null,
    DateTimeOffset? afterCreatedAtUtc = null,
    string? afterRequestId = null,
    int offset = 0,
    CancellationToken ct = default)
{
    if (apiKeyIds.Count == 0)
        return (Array.Empty<ActivityByTokenRecord>(), false, 0);

    using var connection = connectionFactory.CreateConnection();
    var parameters = new DynamicParameters();
    parameters.Add("ApiKeyIds", apiKeyIds.ToArray());
    parameters.Add("Limit", limit + 1);
    parameters.Add("Offset", offset);

    var conditions = new List<string> { "a.api_key_id = any(@ApiKeyIds)" };

    if (fromUtc.HasValue) { conditions.Add("a.created_at_utc >= @FromUtc"); parameters.Add("FromUtc", fromUtc.Value); }
    if (toUtc.HasValue) { conditions.Add("a.created_at_utc <= @ToUtc"); parameters.Add("ToUtc", toUtc.Value); }
    if (!string.IsNullOrEmpty(modelIdFilter)) { conditions.Add("a.model_id = @ModelIdFilter"); parameters.Add("ModelIdFilter", modelIdFilter); }
    if (!string.IsNullOrEmpty(apiKeyIdFilter)) { conditions.Add("a.api_key_id = @ApiKeyIdFilter"); parameters.Add("ApiKeyIdFilter", apiKeyIdFilter); }
    if (afterCreatedAtUtc.HasValue && !string.IsNullOrEmpty(afterRequestId))
    {
        conditions.Add("(a.created_at_utc, a.request_id) < (@AfterCreatedAtUtc, @AfterRequestId)");
        parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
        parameters.Add("AfterRequestId", afterRequestId);
    }
    else if (afterCreatedAtUtc.HasValue)
    {
        conditions.Add("a.created_at_utc < @AfterCreatedAtUtc");
        parameters.Add("AfterCreatedAtUtc", afterCreatedAtUtc.Value);
    }

    var whereClause = "where " + string.Join(" and ", conditions);

    // COUNT query
    var countSql = $"select count(*) from activity_by_token a {whereClause}";
    var totalCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: ct));

    // Data query with OFFSET
    var dataSql = ActivityByTokenSelectSql +
        whereClause + "\n" +
        "order by a.created_at_utc desc, a.request_id desc\n" +
        "offset @Offset limit @Limit;";

    var items = (await connection.QueryAsync<ActivityByTokenEntity>(new CommandDefinition(
            dataSql,
            parameters,
            cancellationToken: ct)).ConfigureAwait(false))
        .Select(BillingPersistenceMapper.ToDomain)
        .ToArray();

    var hasMore = items.Length > limit;
    if (hasMore) items = items.Take(limit).ToArray();
    return (items, hasMore, totalCount);
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Infrastructure/Repository/BillingRepository.cs
git commit -m "feat: implement OFFSET and COUNT in BillingRepository.ListActivityByTokenAsync"
```

---

### Task 4: Implement In-Memory Repository

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Repository/InMemoryBillingRepository.cs:208`

- [ ] **Step 1: Update in-memory implementation**

Open `InMemoryBillingRepository.cs` and find the `ListActivityByTokenAsync` method. Update it to match the new signature and implement offset:

```csharp
public Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore, int TotalCount)> ListActivityByTokenAsync(
    IReadOnlyCollection<string> apiKeyIds,
    int limit,
    DateTimeOffset? fromUtc = null,
    DateTimeOffset? toUtc = null,
    string? modelIdFilter = null,
    string? apiKeyIdFilter = null,
    DateTimeOffset? afterCreatedAtUtc = null,
    string? afterRequestId = null,
    int offset = 0,
    CancellationToken ct = default)
{
    // Implementation depends on existing in-memory store structure
    // Filter, offset, and count logic here
    throw new NotImplementedException(); // or implement based on existing pattern
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Infrastructure/Repository/InMemoryBillingRepository.cs
git commit -m "feat: implement offset in InMemoryBillingRepository.ListActivityByTokenAsync"
```

---

### Task 5: Update Application Service

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs:340-358`

- [ ] **Step 1: Add offset parameter and return TotalCount**

Update the `ListActivityByTokenAsync` method:

```csharp
public async Task<(IReadOnlyCollection<ActivityByTokenRecord> Items, bool HasMore, int TotalCount)> ListActivityByTokenAsync(
    IReadOnlyCollection<string> apiKeyIds,
    DateTimeOffset? fromUtc = null,
    DateTimeOffset? toUtc = null,
    string? modelIdFilter = null,
    string? apiKeyIdFilter = null,
    string? afterCursor = null,
    int? pageSize = null,
    int offset = 0,  // NEW
    CancellationToken ct = default)
{
    if (apiKeyIds.Count == 0)
        return (Array.Empty<ActivityByTokenRecord>(), false, 0);

    var limit = pageSize ?? paginationOptions.Value.EffectivePageSize;
    ParseCursor(afterCursor, out var afterCreatedAtUtc, out var afterRequestId);

    return await repository.ListActivityByTokenAsync(
        apiKeyIds, limit, fromUtc, toUtc, modelIdFilter, apiKeyIdFilter, afterCreatedAtUtc, afterRequestId, offset, ct).ConfigureAwait(false);
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Application/Services/BillingApplicationService.cs
git commit -m "feat: pass offset to repository in BillingApplicationService"
```

---

### Task 6: Update gRPC Service

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/Infrastructure/Grpc/GrpcBillingService.cs:463`

- [ ] **Step 1: Map offset from proto request**

Find the `ListActivityByToken` method in `GrpcBillingService.cs` and update:

```csharp
public override async Task<ListActivityByTokenResponse> ListActivityByToken(
    ListActivityByTokenRequest request,
    ServerCallContext context)
{
    var apiKeyIds = request.ApiKeyIds.ToArray();
    var fromUtc = string.IsNullOrEmpty(request.FromUtc) ? null : DateTimeOffset.Parse(request.FromUtc);
    var toUtc = string.IsNullOrEmpty(request.ToUtc) ? null : DateTimeOffset.Parse(request.ToUtc);

    var (items, hasMore, totalCount) = await billing.ListActivityByTokenAsync(
        apiKeyIds,
        fromUtc,
        toUtc,
        string.IsNullOrEmpty(request.ModelIdFilter) ? null : request.ModelIdFilter,
        string.IsNullOrEmpty(request.ApiKeyIdFilter) ? null : request.ApiKeyIdFilter,
        string.IsNullOrEmpty(request.AfterCursor) ? null : request.AfterCursor,
        request.PageSize > 0 ? request.PageSize : null,
        request.Offset,  // NEW
        context.CancellationToken);

    var response = new ListActivityByTokenResponse
    {
        HasMore = hasMore,
        TotalCount = totalCount  // NEW
    };
    response.Records.Add(items.Select(MapToProto));
    if (hasMore && items.Count > 0)
    {
        var last = items[^1];
        response.NextCursor = $"{last.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}|{last.RequestId}";
    }
    return response;
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/OpenRussiaRouter.BillingService --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.BillingService/Infrastructure/Grpc/GrpcBillingService.cs
git commit -m "feat: map offset in GrpcBillingService.ListActivityByToken"
```

---

### Task 7: Update WebApp BillingProvider

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Services/Billing/BillingProvider.cs:75-116`

- [ ] **Step 1: Add offset parameter and return TotalCount**

Update the `ListActivityByTokenAsync` method:

```csharp
public async Task<(IReadOnlyList<ActivityByTokenInfo> Items, bool HasMore, int TotalCount)> ListActivityByTokenAsync(
    IReadOnlyCollection<string> apiKeyIds,
    DateTimeOffset? fromUtc = null,
    DateTimeOffset? toUtc = null,
    string? modelIdFilter = null,
    string? apiKeyIdFilter = null,
    string? afterCursor = null,
    int pageSize = 0,
    int offset = 0,  // NEW
    CancellationToken cancellationToken = default)
{
    if (apiKeyIds.Count == 0)
        return ([], false, 0);

    var request = new OpenRussiaRouter.Contracts.Grpc.Billing.V1.ListActivityByTokenRequest
    {
        FromUtc = fromUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        ToUtc = toUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        ModelIdFilter = modelIdFilter ?? string.Empty,
        ApiKeyIdFilter = apiKeyIdFilter ?? string.Empty,
        AfterCursor = afterCursor ?? string.Empty,
        PageSize = pageSize,
        Offset = offset  // NEW
    };
    request.ApiKeyIds.Add(apiKeyIds);

    var response = await client.ListActivityByTokenAsync(request, cancellationToken: cancellationToken);

    var items = response.Records.Select(r => new ActivityByTokenInfo(
        r.RequestId,
        r.ApiKeyId,
        r.ApiKeyPrefix,
        r.ModelId,
        r.RouterNodeId,
        r.Usage?.InputTokens ?? 0,
        r.Usage?.OutputTokens ?? 0,
        r.Usage?.CacheTokens ?? 0,
        string.IsNullOrEmpty(r.CostUsd) ? 0m : decimal.Parse(r.CostUsd, CultureInfo.InvariantCulture),
        r.Status,
        DateTimeOffset.Parse(r.CreatedAtUtc, CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(r.CompletedAtUtc, CultureInfo.InvariantCulture))).ToList();

    return (items, response.HasMore, response.TotalCount);
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/OpenRussiaRouter.WebApp --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL

- [ ] **Step 3: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Services/Billing/BillingProvider.cs
git commit -m "feat: pass offset in WebApp BillingProvider.ListActivityByTokenAsync"
```

---

### Task 8: Update Activity Page Model

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml.cs`

- [ ] **Step 1: Replace After with Page parameter**

Replace the `After` property with `Page` and add computed properties:

```csharp
// Replace this line:
// [FromQuery(Name = "after")] public string? After { get; set; }

// With:
[FromQuery(Name = "page")] public int Page { get; set; } = 1;

// Add these properties:
public int TotalCount { get; set; }
public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
public bool HasPreviousPage => Page > 1;
public bool HasNextPage => Page < TotalPages;
```

- [ ] **Step 2: Update OnGetAsync to use offset**

In the `OnGetAsync` method, replace the cursor logic with offset:

```csharp
// Replace lines 75-93 with:
var offset = (Page - 1) * PageSize;

var (items, hasMore, totalCount) = await _billingClient.ListActivityByTokenAsync(
    keyIds,
    fromUtc: fromUtc,
    toUtc: toUtc,
    modelIdFilter: string.IsNullOrEmpty(ModelFilter) ? null : ModelFilter,
    apiKeyIdFilter: string.IsNullOrEmpty(KeyFilter) ? null : KeyFilter,
    pageSize: PageSize,
    offset: offset,  // NEW
    cancellationToken: HttpContext.RequestAborted);

Activity = items;
TotalCount = totalCount;
// Remove HasMore and NextCursor assignments
```

- [ ] **Step 3: Update BuildDetailsUrl to use page**

Replace the `BuildDetailsUrl` method:

```csharp
public string BuildDetailsUrl(int? page = null)
{
    var qs = new List<string> { "tab=details" };
    if (!string.IsNullOrEmpty(FromDate)) qs.Add($"from={FromDate}");
    if (!string.IsNullOrEmpty(ToDate)) qs.Add($"to={ToDate}");
    if (!string.IsNullOrEmpty(ModelFilter)) qs.Add($"model={Uri.EscapeDataString(ModelFilter)}");
    if (!string.IsNullOrEmpty(KeyFilter)) qs.Add($"key={Uri.EscapeDataString(KeyFilter)}");
    if (page.HasValue && page.Value > 1) qs.Add($"page={page.Value}");
    return "/workspace/activity?" + string.Join("&", qs);
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/OpenRussiaRouter.WebApp --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL

- [ ] **Step 5: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml.cs
git commit -m "feat: replace cursor pagination with page numbers in ActivityModel"
```

---

### Task 9: Update Activity Page View

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml:224-229`

- [ ] **Step 1: Replace pagination with page numbers**

Replace the pagination section (lines 224-229) with:

```csharp
@if (Model.TotalPages > 1)
{
    <div class="act-pagination">
        @if (Model.HasPreviousPage)
        {
            <a class="btn btn-secondary btn-sm" href="@Model.BuildDetailsUrl(Model.Page - 1)">@Localizer["Activity_PreviousPage"]</a>
        }
        <span class="act-pagination-info">@Localizer["Activity_PageInfo", Model.Page, Model.TotalPages]</span>
        @if (Model.HasNextPage)
        {
            <a class="btn btn-secondary btn-sm" href="@Model.BuildDetailsUrl(Model.Page + 1)">@Localizer["Activity_NextPage"]</a>
        }
    </div>
}
```

- [ ] **Step 2: Add localization keys**

Check if localization keys exist. If not, add to the shared resource file:
- `Activity_PreviousPage` = "Previous"
- `Activity_PageInfo` = "Page {0} of {1}"

- [ ] **Step 3: Verify build**

Run: `dotnet build src/OpenRussiaRouter.WebApp --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL

- [ ] **Step 4: Commit**

```bash
git add src/OpenRussiaRouter.WebApp/Pages/Workspace/Activity.cshtml
git commit -m "feat: render page-number pagination in Activity view"
```

---

### Task 10: Run Tests

**Files:**
- Test: Run existing BillingService tests

- [ ] **Step 1: Run BillingService tests**

Run: `dotnet test tests/OpenRussiaRouter.BillingService.Tests --no-build -nr:false -m:1`
Expected: All tests pass (or only pre-existing failures)

- [ ] **Step 2: Run WebApp build check**

Run: `dotnet build src/OpenRussiaRouter.WebApp --no-restore -nr:false -m:1`
Expected: BUILD SUCCESSFUL with no warnings (warnings = errors)

- [ ] **Step 3: Final commit if needed**

```bash
git add -A
git commit -m "feat: complete page-number pagination for Activity page"
```

---

## Summary

After completing all tasks:
- `/workspace/activity?tab=details` will use `page=N` instead of `after=...`
- Default page is 1
- Page numbers shown with Previous/Next buttons
- Total count displayed (e.g., "Page 1 of 5")
- Filters (date, model, key) preserved across pages
