# ApiGateway Retry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement exhaustive retry for ApiGateway on 5xx and 4xx errors by passing excluded nodes to ControlPlane.

**Architecture:** Modify ControlPlane gRPC contracts to accept `excluded_node_ids`. Update ApiGateway to keep track of failed node IDs per request and pass them when acquiring a new lease. Wrap the request execution in a loop that continues as long as ControlPlane provides a lease. Modify ControlPlane to filter out excluded nodes.

**Tech Stack:** C#, gRPC, ASP.NET Core, Protobuf

---

### Task 1: Update Protobuf Contracts

**Files:**
- Modify: `src/OpenRussiaRouter.Contracts/Protos/control-plane.proto`

- [ ] **Step 1: Add `excluded_node_ids` to `SelectRouterNodeRequest`**
  Modify the message to include the repeated field.

```protobuf
message SelectRouterNodeRequest {
  string model_id = 1;
  string request_id = 2;
  string preferred_node_id = 3;
  string preferred_node_control_token = 4;
  repeated string excluded_node_ids = 5;
}
```

- [ ] **Step 2: Build the project to generate new gRPC code**
  Run: `dotnet build src/OpenRussiaRouter.Contracts/OpenRussiaRouter.Contracts.csproj`
  Expected: Build succeeds.

- [ ] **Step 3: Commit**
```bash
git add src/OpenRussiaRouter.Contracts/Protos/control-plane.proto
git commit -m "feat(contracts): add excluded_node_ids to SelectRouterNodeRequest"
```

---

### Task 2: Update ApiGateway ControlPlane Provider

**Files:**
- Modify: `src/OpenRussiaRouter.ApiGateway/ControlPlane/IControlPlaneProvider.cs`
- Modify: `src/OpenRussiaRouter.ApiGateway/ControlPlane/GrpcControlPlaneProvider.cs`

- [ ] **Step 1: Update `IControlPlaneProvider` signature**

```csharp
    Task<GatewayRouterLease?> SelectRouterNodeAsync(
        string modelId,
        string requestId,
        string? preferredNodeId,
        string? preferredNodeControlToken,
        IReadOnlyCollection<string>? excludedNodeIds,
        CancellationToken cancellationToken);
```

- [ ] **Step 2: Update `GrpcControlPlaneProvider` implementation**

```csharp
    public async Task<GatewayRouterLease?> SelectRouterNodeAsync(
        string modelId,
        string requestId,
        string? preferredNodeId,
        string? preferredNodeControlToken,
        IReadOnlyCollection<string>? excludedNodeIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new SelectRouterNodeRequest
            {
                ModelId = modelId,
                RequestId = requestId,
                PreferredNodeId = preferredNodeId ?? string.Empty,
                PreferredNodeControlToken = preferredNodeControlToken ?? string.Empty
            };

            if (excludedNodeIds != null)
            {
                request.ExcludedNodeIds.AddRange(excludedNodeIds);
            }

            var response = await controlClient.SelectRouterNodeAsync(
                request,
                cancellationToken: cancellationToken);
// ... keep rest of method the same
```

- [ ] **Step 3: Build the ApiGateway project to ensure it compiles**
  Run: `dotnet build src/OpenRussiaRouter.ApiGateway/OpenRussiaRouter.ApiGateway.csproj`
  Expected: Build fails in GatewayPipeline and ChatCompletionHandler because we changed the signature.

- [ ] **Step 4: Commit**
```bash
git add src/OpenRussiaRouter.ApiGateway/ControlPlane/
git commit -m "feat(apigateway): pass excluded node ids to control plane provider"
```

---

### Task 3: Update GatewayPipeline

**Files:**
- Modify: `src/OpenRussiaRouter.ApiGateway/Application/Services/GatewayPipeline.cs`

- [ ] **Step 1: Update `IGatewayPipeline` interface**

```csharp
    Task<GatewayAuthorizedContext?> AuthorizeAndAcquireLeaseAsync(
        HttpContext context,
        string? model,
        string requestId,
        IReadOnlyCollection<string>? excludedNodeIds,
        CancellationToken cancellationToken);
```

- [ ] **Step 2: Update `GatewayPipeline` implementation**
  Add the `excludedNodeIds` parameter and pass it to `SelectRouterNodeAsync`.

```csharp
    public async Task<GatewayAuthorizedContext?> AuthorizeAndAcquireLeaseAsync(
        HttpContext context,
        string? model,
        string requestId,
        IReadOnlyCollection<string>? excludedNodeIds,
        CancellationToken cancellationToken)
// ... inside method ...
        var lease = await controlPlaneProvider.SelectRouterNodeAsync(
            balanceResult.RouterModelId,
            requestId,
            preferredNodeId,
            preferredNodeControlToken,
            excludedNodeIds,
            cancellationToken);
```

- [ ] **Step 3: Commit**
```bash
git add src/OpenRussiaRouter.ApiGateway/Application/Services/GatewayPipeline.cs
git commit -m "feat(apigateway): thread excludedNodeIds through GatewayPipeline"
```

---

### Task 4: Implement Retry Loop in ChatCompletionHandler (Non-Streaming)

**Files:**
- Modify: `src/OpenRussiaRouter.ApiGateway/Application/Endpoints/ChatCompletionHandler.cs`

- [ ] **Step 1: Rewrite `HandleAsync` non-streaming part with a while loop**

```csharp
// Inside HandleAsync, replace the existing pipeline call and downstream proxying with:

            HashSet<string>? excludedNodes = null;
            RouterProxyResponse? lastResponse = null;

            while (true)
            {
                var auth = await pipeline.AuthorizeAndAcquireLeaseAsync(context, body.Metadata.Model, requestId, excludedNodes, context.RequestAborted);
                
                // If auth is null on the first try, pipeline already wrote the error response.
                // If it's null on a subsequent try, it means no more nodes are available.
                if (auth is null)
                {
                    if (lastResponse != null)
                    {
                        // We exhausted nodes and had a previous error. Return it.
                        context.Response.StatusCode = lastResponse.StatusCode;
                        context.Response.ContentType = lastResponse.ContentType;
                        await context.Response.WriteAsync(lastResponse.Body);
                    }
                    return;
                }

                var patchedJson = GeminiSchemaPatcher.SanitizeIfGeminiModel(
                    SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId),
                    auth.RouterModelId,
                    body.Metadata.Model ?? "");
                var startedAt = DateTimeOffset.UtcNow;

                var response = await routerProxy.SendChatCompletionAsync(auth.Lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);
                var completedAt = DateTimeOffset.UtcNow;

                bool isError = response.StatusCode >= 400;

                await controlPlaneProvider.ReleaseRouterNodeAsync(
                    auth.Lease.LeaseId,
                    isError ? "failed" : "completed",
                    context.RequestAborted);

                if (isError)
                {
                    logger.LogWarning("Chat completion downstream error. StatusCode: {StatusCode}, Node: {NodeId}, Body: {Body}", response.StatusCode, auth.Lease.NodeId, response.Body);
                    
                    excludedNodes ??= new HashSet<string>();
                    excludedNodes.Add(auth.Lease.NodeId);
                    lastResponse = response;
                    
                    // Loop again
                    continue;
                }

                // Success!
                var hasUsage = RouterUsageExtractor.TryExtract(response.Body, out var usage) && usage.TotalTokens > 0;
                var status = hasUsage ? "completed" : "usage_missing";

                RecordUsageFireAndForget(requestId, auth, body.Metadata.Model!, response.ProviderName, response.ProviderAccount, usage, status, startedAt, completedAt);

                context.Response.StatusCode = response.StatusCode;
                context.Response.ContentType = response.ContentType;
                await context.Response.WriteAsync(response.Body);
                return;
            }
```

- [ ] **Step 2: Commit**
```bash
git add src/OpenRussiaRouter.ApiGateway/Application/Endpoints/ChatCompletionHandler.cs
git commit -m "feat(apigateway): implement exhaustive retry loop for non-streaming requests"
```

---

### Task 5: Implement Retry Loop in ChatCompletionHandler (Streaming)

**Files:**
- Modify: `src/OpenRussiaRouter.ApiGateway/Application/Endpoints/ChatCompletionHandler.cs`

- [ ] **Step 1: Rewrite `ExecuteStreamingAsync` with a while loop**

```csharp
    private async Task ExecuteStreamingAsync(HttpContext context, ChatCompletionRequestBody body, string requestId)
    {
        try
        {
            HashSet<string>? excludedNodes = null;
            RouterStreamSession? lastFailedSession = null;

            while (true)
            {
                var auth = await pipeline.AuthorizeAndAcquireLeaseAsync(context, body.Metadata.Model, requestId, excludedNodes, context.RequestAborted);
                if (auth is null)
                {
                    if (lastFailedSession != null)
                    {
                        await PassThroughErrorAsync(context, lastFailedSession, context.RequestAborted);
                    }
                    return;
                }

                var patchedJson = SseRequestPatcher.InjectStreamOptions(
                    GeminiSchemaPatcher.SanitizeIfGeminiModel(
                        SseRequestPatcher.PatchModel(body.Json, auth.RouterModelId),
                        auth.RouterModelId,
                        body.Metadata.Model ?? ""));
                var lease = auth.Lease;

                var session = await routerProxy.BeginStreamChatCompletionAsync(lease, patchedJson, requestId, auth.ApiKey, context.RequestAborted);

                if (!session.IsSuccess)
                {
                    await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "failed", context.RequestAborted);
                    
                    excludedNodes ??= new HashSet<string>();
                    excludedNodes.Add(lease.NodeId);

                    if (lastFailedSession != null)
                    {
                        await lastFailedSession.DisposeAsync();
                    }
                    lastFailedSession = session;
                    
                    continue; // Try next node
                }

                if (lastFailedSession != null)
                {
                    await lastFailedSession.DisposeAsync();
                }

                // Commit SSE response ?" no error returns possible after this point
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["X-Accel-Buffering"] = "no";

                var startedAt = DateTimeOffset.UtcNow;
                TokenUsage usage;
                string? providerName;
                string? providerAccount;

                await using (session)
                {
                    providerName = session.ProviderName;
                    providerAccount = session.ProviderAccount;
                    usage = await session.CopyToAsync(context.Response.Body, context.RequestAborted);
                }

                var completedAt = DateTimeOffset.UtcNow;
                await controlPlaneProvider.ReleaseRouterNodeAsync(lease.LeaseId, "completed", CancellationToken.None);

                var status = usage.TotalTokens > 0 ? "completed" : "usage_missing";
                RecordUsageFireAndForget(requestId, auth, body.Metadata.Model!, providerName, providerAccount, usage, status, startedAt, completedAt);
                return;
            }
        }
// ... keep catch blocks ...
```

- [ ] **Step 2: Commit**
```bash
git add src/OpenRussiaRouter.ApiGateway/Application/Endpoints/ChatCompletionHandler.cs
git commit -m "feat(apigateway): implement exhaustive retry loop for streaming requests"
```

---

### Task 6: Implement Exclusion Logic in ControlPlane

**Files:**
- Find and Modify the ControlPlane logic that selects nodes. Use search tools if exact file path isn't known, typically: `src/OpenRussiaRouter.ControlPlane/Application/Services/RouterSelector.cs` or `src/OpenRussiaRouter.ControlPlane/Grpc/RouterControlPlaneService.cs`.

- [ ] **Step 1: Find ControlPlane Provider logic**
  Run: `grep -r "SelectRouterNodeAsync" src/OpenRussiaRouter.ControlPlane/`
  Expected: Locate the gRPC service or core selector logic.

- [ ] **Step 2: Implement Exclusion in Selector**
  Modify the list of available nodes to exclude any `node.Id` that is present in the `request.ExcludedNodeIds` before picking a node (usually doing order-by capacity weight or similar).

```csharp
// Example assuming a LINQ query for available nodes
var query = _nodes.Where(n => ...);

if (request.ExcludedNodeIds != null && request.ExcludedNodeIds.Count > 0)
{
    var excludedSet = new HashSet<string>(request.ExcludedNodeIds);
    query = query.Where(n => !excludedSet.Contains(n.Id));
}
```

- [ ] **Step 3: Update ControlPlane gRPC Service**
  Pass the `ExcludedNodeIds` from `SelectRouterNodeRequest` into the selector service.

- [ ] **Step 4: Build ControlPlane to verify**
  Run: `dotnet build src/OpenRussiaRouter.ControlPlane/OpenRussiaRouter.ControlPlane.csproj`
  Expected: Build succeeds.

- [ ] **Step 5: Commit**
```bash
git add src/OpenRussiaRouter.ControlPlane/
git commit -m "feat(controlplane): filter out excluded nodes during selection"
```

---

### Task 7: Final Tests

- [ ] **Step 1: Build entire solution**
  Run: `dotnet build OpenRussiaRouter.sln`
  Expected: Build succeeds

- [ ] **Step 2: Run all tests**
  Run: `dotnet test OpenRussiaRouter.sln`
  Expected: All tests pass. Fix any compilation or failing test issues related to signature changes (e.g., moq setups in ApiGateway tests).
