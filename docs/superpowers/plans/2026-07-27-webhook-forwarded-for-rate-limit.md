# T-006 Webhook Forwarded-For Rate-Limit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent unauthenticated payment-webhook callers from creating new rate-limit buckets by supplying forged X-Forwarded-For values.

**Architecture:** Keep Caddy as the sole trusted immediate proxy without coupling application code to a Docker subnet. Configure ASP.NET Core forwarded-header handling to consume exactly one hop and prove through the webhook endpoint that different client-supplied left-side values share the same 60-request fixed-window limiter bucket.

**Tech Stack:** .NET 8, ASP.NET Core Forwarded Headers and Rate Limiting middleware, xUnit, WebApplicationFactory.

---

### Task 1: Capture the forged-header bypass in an endpoint regression test

**Files:**
- Modify: `tests/OpenRussiaRouter.WebApp.Tests/PaymentWebhookEndpointTests.cs:1-44`

- [ ] **Step 1: Add the test imports and a helper that models Caddy's X-Forwarded-For chain**

Add `using System.Net.Http.Headers;` if the compiler requires it, then add this helper inside `PaymentWebhookEndpointTests`:

```csharp
private static HttpRequestMessage CreateForgedForwardedForRequest(int requestNumber)
{
    var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/mock")
    {
        Content = new StringContent("not-json")
    };
    request.Headers.Add("X-Mock-Signature", "wrong");
    request.Headers.TryAddWithoutValidation(
        "X-Forwarded-For",
        $"198.51.100.{requestNumber}, 172.20.0.2");
    return request;
}
```

The first address is client-controlled and changes for every request. The last address is the immediate Caddy hop and remains constant.

- [ ] **Step 2: Add the failing rate-limit integration test**

Add:

```csharp
[Fact]
public async Task Forged_forwarded_for_values_do_not_bypass_webhook_rate_limit()
{
    for (var requestNumber = 1; requestNumber <= 60; requestNumber++)
    {
        using var request = CreateForgedForwardedForRequest(requestNumber);
        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    using var rejectedRequest = CreateForgedForwardedForRequest(61);
    using var rejectedResponse = await _client.SendAsync(rejectedRequest);

    rejectedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
}
```

The invalid signature is intentional: it lets the request pass through the rate limiter without invoking external payment dependencies.

- [ ] **Step 3: Run the focused test before changing middleware configuration**

Run:

```powershell
dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-restore -nr:false -m:1 --filter "FullyQualifiedName~PaymentWebhookEndpointTests.Forged_forwarded_for_values_do_not_bypass_webhook_rate_limit"
```

Expected: on the vulnerable configuration, the 61st request is not throttled because the forged source produces a different limiter partition.

### Task 2: Restrict forwarded-header processing to the immediate proxy

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs:366-371`

- [ ] **Step 1: Set the forwarded-header hop limit**

Add the explicit hop limit to the existing `ForwardedHeadersOptions` initializer:

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    ForwardLimit = 1,
    KnownNetworks = { },
    KnownProxies = { }
});
```

Do not move the middleware, remove the forwarded host/protocol flags, or introduce a static Docker network range.

- [ ] **Step 2: Run the focused regression test**

Run the command from Task 1, Step 3.

Expected: the first 60 requests reach signature validation and receive 401; the 61st receives 429 despite a distinct forged left-side address.

- [ ] **Step 3: Run the WebApp tests and solution build**

Run:

```powershell
dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-restore -nr:false -m:1
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: both commands exit 0 without warnings.

- [ ] **Step 4: Update documentation, task status, and commit**

Amend `src/OpenRussiaRouter.WebApp/CLAUDE.md` to say that the application processes only one immediate forwarded-header hop to protect the webhook rate limiter, while preserving Caddy's scheme and host forwarding. Set T-006 in `.opencode/tasks.md` to `done` with the actual ISO-8601 completion time. Then run:

```powershell
git add -- src/OpenRussiaRouter.WebApp/Program.cs src/OpenRussiaRouter.WebApp/CLAUDE.md tests/OpenRussiaRouter.WebApp.Tests/PaymentWebhookEndpointTests.cs .opencode/tasks.md
git commit -m "fix(webapp): bound forwarded headers for webhooks"
```

