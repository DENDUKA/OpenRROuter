using System.Net;
using OpenRussiaRouter.E2ETests.Pages;
using Xunit.Abstractions;

namespace OpenRussiaRouter.E2ETests;

/// <summary>
/// E2E: admin opens Sync from 9Router, imports two new models, sees them in billing catalog.
///
/// Required: same credentials as other E2E tests (ORR_E2E_USER_EMAIL / ORR_E2E_USER_PASSWORD).
/// Optional: ORR_E2E_STUB_ADDRESS — address the ControlPlane process can reach the stub HTTP server
///           (defaults to "localhost"; set to "host.docker.internal" when running in containers).
///
/// The stub node is registered and deleted within the test. Imported models accumulate in the
/// billing catalog (there is no delete-model UI), so run-specific IDs are used to stay idempotent.
/// </summary>
public sealed class ModelSyncTests(PlaywrightFixture fixture, ITestOutputHelper output) : IClassFixture<PlaywrightFixture>
{
    private const string StubControlToken = "e2e-sync-stub-token";

    [Fact]
    public async Task SyncFromRouter_ImportTwoNewModels_AppearsInBillingCatalog_AndNotNewOnResync()
    {
        if (!TestConfiguration.IsConfigured)
        {
            output.WriteLine("E2E credentials are not configured; set ORR_E2E_USER_EMAIL and ORR_E2E_USER_PASSWORD or e2e.local.json.");
            return;
        }

        var run = Guid.NewGuid().ToString("N")[..8];
        var nodeName = $"E2E Sync {run}";

        // run-specific model IDs keep the test idempotent across repeated runs
        var upstreamText = $"E2E-Text-{run}";
        var upstreamVision = $"E2E-Vision-{run}";
        // publicId includes a namespace prefix so publicId != id (verifies namespaced import)
        var publicText = $"e2e/{upstreamText}";
        var publicVision = $"e2e/{upstreamVision}";

        // ── Stub server ──────────────────────────────────────────────────────
        await using var stub = new RouterNodeStub(
            TestConfiguration.StubAddress,
            StubControlToken,
            upstreamText, publicText,
            upstreamVision, publicVision);

        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        // ── 1. Login ─────────────────────────────────────────────────────────
        var loginPage = new LoginPage(page, TestConfiguration.BaseUrl);
        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(TestConfiguration.UserEmail!, TestConfiguration.UserPassword!);

        // ── 2. Register stub node ────────────────────────────────────────────
        var nodesPage = new NodesPage(page, TestConfiguration.BaseUrl);
        await nodesPage.NavigateAsync();
        await nodesPage.AddNodeAsync(nodeName, stub.NodeUrl, controlToken: StubControlToken);

        try
        {
            // ── 3. Wait for ControlPlane health-check to mark node healthy ──
            //    Stub responds {"status":"healthy"} on GET /healthz
            await nodesPage.WaitForHealthyAsync(nodeName, timeoutSeconds: 120);

            var modelsPage = new ModelsAdminPage(page, TestConfiguration.BaseUrl);

            // ── 4. Open /admin/models, click "Sync from 9Router" ─────────────
            await modelsPage.NavigateAsync();
            await modelsPage.ClickSyncFromRouterLinkAsync();

            // ── 5. Wait for "New On Nodes" table ────────────────────────────
            await modelsPage.WaitForSyncTableAsync();

            // ── 6. Select both new models ────────────────────────────────────
            await modelsPage.SelectRowByUpstreamIdAsync(upstreamText);
            await modelsPage.SelectRowByUpstreamIdAsync(upstreamVision);

            // ── 7. Fill per-row prices ───────────────────────────────────────
            await modelsPage.SetRowPricesAsync(upstreamText, inputPrice: 10m, outputPrice: 20m, cachePrice: 5m);
            await modelsPage.SetRowPricesAsync(upstreamVision, inputPrice: 15m, outputPrice: 30m, cachePrice: 8m);

            // ── 8. Per-row Enabled (defaults to true; assert explicit) ───────
            await modelsPage.SetRowEnabledAsync(upstreamText, true);
            await modelsPage.SetRowEnabledAsync(upstreamVision, true);

            // ── 9. Submit ────────────────────────────────────────────────────
            await modelsPage.SubmitImportAsync();

            // ── 10. Wait for redirect to /admin/models ───────────────────────
            await page.WaitForURLAsync(
                url => url.Contains("/admin/models") && !url.Contains("/sync"),
                new() { Timeout = 15_000 });

            // ── 11. Success message ──────────────────────────────────────────
            Assert.True(
                await modelsPage.HasSuccessMessageAsync("Imported 2 models."),
                "Expected success message 'Imported 2 models.'");

            // ── 12. Both models visible in billing table ─────────────────────
            var modelIds = await modelsPage.GetModelIdsAsync();
            Assert.Contains(publicText, modelIds, StringComparer.Ordinal);
            Assert.Contains(publicVision, modelIds, StringComparer.Ordinal);

            // ═══ Phase 2: re-sync must not show already-imported models ═══

            // ── 13. Navigate to sync page again ──────────────────────────────
            await modelsPage.NavigateToSyncAsync();

            // ── 14. Imported models must NOT appear as "New On Nodes" ────────
            Assert.False(
                await modelsPage.HasNewOnNodesRowAsync(upstreamText),
                $"{upstreamText} must not appear as new after import.");
            Assert.False(
                await modelsPage.HasNewOnNodesRowAsync(upstreamVision),
                $"{upstreamVision} must not appear as new after import.");
        }
        finally
        {
            // Best-effort: remove stub node so subsequent runs stay clean
            try
            {
                await nodesPage.NavigateAsync();
                if (await nodesPage.NodeExistsAsync(nodeName))
                    await nodesPage.DeleteNodeAsync(nodeName);
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // In-process stub: minimal 9Router node for E2E test isolation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimal in-process HTTP server that impersonates a 9Router node.
    /// Implements:
    ///   GET /healthz                                         → {"status":"healthy"}
    ///   GET /api/integrations/providers/with-models          → one active e2e connection with two models embedded
    ///   GET /v1/models                                       → same two model IDs (fallback)
    ///   POST /api/integrations/keys                          → stub key-create response
    ///
    /// Listens on a dynamic port on localhost (or "+" when StubAddress is a remote host).
    /// NodeUrl is the URL to register with ControlPlane and is reachable from it.
    /// </summary>
    private sealed class RouterNodeStub : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serveTask;
        private readonly string _controlToken;

        private readonly string _providersWithModelsJson;
        private readonly string _v1ModelsJson;

        public string NodeUrl { get; }

        public RouterNodeStub(
            string stubAddress,
            string controlToken,
            string upstreamText, string publicText,
            string upstreamVision, string publicVision)
        {
            _controlToken = controlToken;

            var port = FindFreePort();

            // Determine the HttpListener prefix.
            // For localhost/127.0.0.1 a simple "localhost" prefix works without elevated rights.
            // For remote addresses (containers) we need to bind on all interfaces via "+".
            var isLoopback = stubAddress is "localhost" or "127.0.0.1";
            var listenHost = isLoopback ? "localhost" : "+";
            var prefix = $"http://{listenHost}:{port}/";

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            NodeUrl = $"http://{stubAddress}:{port}/";

            // Combined endpoint: providers + their models in one response
            _providersWithModelsJson = $$"""
                {
                  "connections": [
                    {
                      "id": "e2e-stub-conn",
                      "provider": "e2e",
                      "authType": "apikey",
                      "name": "E2E Stub Provider",
                      "isActive": true,
                      "testStatus": "active",
                      "models": [
                        {
                          "id": "{{upstreamText}}",
                          "publicId": "{{publicText}}",
                          "name": "E2E Text Model",
                          "type": "text",
                          "enabled": true,
                          "sources": ["e2e"]
                        },
                        {
                          "id": "{{upstreamVision}}",
                          "publicId": "{{publicVision}}",
                          "name": "E2E Vision Model",
                          "type": "vision",
                          "enabled": true,
                          "sources": ["e2e"]
                        }
                      ]
                    }
                  ]
                }
                """;

            _v1ModelsJson = $$"""
                {
                  "data": [
                    { "id": "{{upstreamText}}" },
                    { "id": "{{upstreamVision}}" }
                  ]
                }
                """;

            _serveTask = ServeAsync(_cts.Token);
        }

        private static int FindFreePort()
        {
            using var tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((System.Net.IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }

        private async Task ServeAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch when (ct.IsCancellationRequested || !_listener.IsListening)
                {
                    break;
                }

                // fire-and-forget; errors are swallowed per request
                _ = Task.Run(() => HandleAsync(ctx, ct), CancellationToken.None);
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            try
            {
                var method = ctx.Request.HttpMethod;
                var path = ctx.Request.Url?.AbsolutePath ?? "/";

                string? json = null;

                if (method == "GET" && path == "/healthz")
                {
                    json = """{"status":"healthy"}""";
                }
                else if (method == "GET" && path == "/api/integrations/providers/with-models")
                {
                    if (!Authorize(ctx))
                        return;

                    json = _providersWithModelsJson;
                }
                else if (method == "GET" && path == "/v1/models")
                {
                    json = _v1ModelsJson;
                }
                else if (method == "POST" && path == "/api/integrations/keys")
                {
                    if (!Authorize(ctx))
                        return;

                    json = """{"created":true,"id":"stub-key-1"}""";
                }

                if (json is null)
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }

        private bool Authorize(HttpListenerContext ctx)
        {
            var expected = "Bearer " + _controlToken;
            if (string.Equals(ctx.Request.Headers["Authorization"], expected, StringComparison.Ordinal))
                return true;

            ctx.Response.StatusCode = 401;
            ctx.Response.Close();
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            try
            {
                await _serveTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch
            {
                // ignore timeout / cancellation on shutdown
            }

            _cts.Dispose();
        }
    }
}
