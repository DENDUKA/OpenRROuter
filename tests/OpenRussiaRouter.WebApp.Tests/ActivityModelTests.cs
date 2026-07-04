using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using OpenRussiaRouter.Contracts.Auth;
using OpenRussiaRouter.WebApp.Pages.Workspace;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using System.Security.Claims;

namespace OpenRussiaRouter.WebApp.Tests;

public class ActivityModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset SevenDaysAgo = Now.AddDays(-7);

    [Fact]
    public async Task OnGetAsync_DetailsTab_DeletedKeysNotInApiKeys()
    {
        var activeKey = new ApiKeySummary("k1", "Active Key", "sk-abc", Now, Now, null, false);
        var disabledKey = new ApiKeySummary("k2", "Disabled Key", "sk-def", Now, Now, Now, false);
        var deletedKey = new ApiKeySummary("k3", "Deleted Key", "sk-ghi", Now, Now, null, true);

        var auth = new ActivityFakeAuth([activeKey, disabledKey, deletedKey]);
        var billing = new ActivityFakeBilling(
            activity: [],
            summary: new ActivitySummaryInfo(0, 0, 0, 0, 0m, [], []));

        var model = CreateModel(billing, auth);

        await model.OnGetAsync();

        Assert.Equal(2, model.ApiKeys.Count);
        Assert.Contains(model.ApiKeys, k => k.Id == "k1");
        Assert.Contains(model.ApiKeys, k => k.Id == "k2");
        Assert.DoesNotContain(model.ApiKeys, k => k.Id == "k3");
    }

    [Fact]
    public async Task OnGetAsync_DetailsTab_AvailableModelsFromSummaryNotCatalog()
    {
        var auth = new ActivityFakeAuth([
            new ApiKeySummary("k1", "Key 1", "sk-1", Now, Now, null, false)
        ]);

        var summaryModels = new[]
        {
            new ActivityModelSummaryInfo("model-a", 10, 100, 50, 5, 0.01m),
            new ActivityModelSummaryInfo("model-b", 5, 200, 100, 10, 0.02m),
        };
        var billing = new ActivityFakeBilling(
            activity: [],
            summary: new ActivitySummaryInfo(15, 300, 150, 15, 0.03m, summaryModels, []));

        var model = CreateModel(billing, auth);

        await model.OnGetAsync();

        Assert.Equal(2, model.AvailableModels.Count);
        Assert.Contains("model-a", model.AvailableModels);
        Assert.Contains("model-b", model.AvailableModels);
    }

    [Fact]
    public async Task OnGetAsync_DetailsTab_ActivityAndSummaryRunInParallel()
    {
        var auth = new ActivityFakeAuth([
            new ApiKeySummary("k1", "Key 1", "sk-1", Now, Now, null, false)
        ]);

        var activityRecords = new[]
        {
            new ActivityByTokenInfo("req-1", "k1", "sk-1", "model-x", "node-1",
                100, 50, 5, 0.005m, "completed", SevenDaysAgo, Now),
        };
        var summaryModels = new[]
        {
            new ActivityModelSummaryInfo("model-x", 1, 100, 50, 5, 0.005m),
        };
        var billing = new ActivityFakeBilling(
            activity: activityRecords,
            summary: new ActivitySummaryInfo(1, 100, 50, 5, 0.005m, summaryModels, []));

        var model = CreateModel(billing, auth);

        await model.OnGetAsync();

        Assert.Single(model.Activity);
        Assert.Equal("model-x", model.Activity[0].ModelId);
        Assert.Single(model.AvailableModels);
        Assert.Equal("model-x", model.AvailableModels[0]);
    }

    [Fact]
    public async Task OnGetAsync_DetailsTab_EmptyActivity_EmptyModels()
    {
        var auth = new ActivityFakeAuth([
            new ApiKeySummary("k1", "Key 1", "sk-1", Now, Now, null, false)
        ]);
        var billing = new ActivityFakeBilling(
            activity: [],
            summary: new ActivitySummaryInfo(0, 0, 0, 0, 0m, [], []));

        var model = CreateModel(billing, auth);

        await model.OnGetAsync();

        Assert.Empty(model.Activity);
        Assert.Empty(model.AvailableModels);
    }

    [Fact]
    public async Task OnGetAsync_DetailsTab_ModelsSortedAlphabetically()
    {
        var auth = new ActivityFakeAuth([
            new ApiKeySummary("k1", "Key 1", "sk-1", Now, Now, null, false)
        ]);
        var summaryModels = new[]
        {
            new ActivityModelSummaryInfo("zebra", 1, 10, 5, 0, 0.001m),
            new ActivityModelSummaryInfo("alpha", 2, 20, 10, 0, 0.002m),
            new ActivityModelSummaryInfo("mid", 3, 30, 15, 0, 0.003m),
        };
        var billing = new ActivityFakeBilling(
            activity: [],
            summary: new ActivitySummaryInfo(6, 60, 30, 0, 0.006m, summaryModels, []));

        var model = CreateModel(billing, auth);

        await model.OnGetAsync();

        Assert.Equal(3, model.AvailableModels.Count);
        Assert.Equal("alpha", model.AvailableModels[0]);
        Assert.Equal("mid", model.AvailableModels[1]);
        Assert.Equal("zebra", model.AvailableModels[2]);
    }

    [Fact]
    public async Task OnGetAsync_OverviewTab_AvailableModelsFromSummary()
    {
        var auth = new ActivityFakeAuth([
            new ApiKeySummary("k1", "Key 1", "sk-1", Now, Now, null, false)
        ]);
        var summaryModels = new[]
        {
            new ActivityModelSummaryInfo("gpt-4", 100, 10000, 5000, 500, 0.5m),
            new ActivityModelSummaryInfo("claude-3", 50, 5000, 2500, 250, 0.25m),
        };
        var billing = new ActivityFakeBilling(
            activity: [],
            summary: new ActivitySummaryInfo(150, 15000, 7500, 750, 0.75m, summaryModels, []));

        var model = CreateModel(billing, auth);
        model.Tab = "overview";

        await model.OnGetAsync();

        Assert.NotNull(model.Summary);
        Assert.Equal(2, model.AvailableModels.Count);
        Assert.Contains("claude-3", model.AvailableModels);
        Assert.Contains("gpt-4", model.AvailableModels);
    }

    [Fact]
    public async Task OnGetAsync_DetailsTab_FiltersPassedToBilling()
    {
        var auth = new ActivityFakeAuth([
            new ApiKeySummary("k1", "Key 1", "sk-1", Now, Now, null, false)
        ]);
        var billing = new ActivityFakeBilling(
            activity: [],
            summary: new ActivitySummaryInfo(0, 0, 0, 0, 0m, [], []));

        var model = CreateModel(billing, auth);
        model.FromDate = "2025-01-01";
        model.ToDate = "2025-01-31";
        model.ModelFilter = "gpt-4";
        model.KeyFilter = "k1";

        await model.OnGetAsync();

        Assert.Equal(["k1"], billing.LastKeyIds);
        Assert.NotNull(billing.LastFromUtc);
        Assert.NotNull(billing.LastToUtc);
        Assert.Equal("gpt-4", billing.LastModelFilter);
        Assert.Equal("k1", billing.LastKeyFilter);
    }

    [Fact]
    public async Task OnGetAsync_DetailsTab_DeletedKeysRequestedWithIncludeDeletedFalse()
    {
        var auth = new ActivityFakeAuth([
            new ApiKeySummary("k1", "Key 1", "sk-1", Now, Now, null, false),
            new ApiKeySummary("k2", "Deleted", "sk-2", Now, Now, null, true),
        ]);
        var billing = new ActivityFakeBilling(
            activity: [],
            summary: new ActivitySummaryInfo(0, 0, 0, 0, 0m, [], []));

        var model = CreateModel(billing, auth);

        await model.OnGetAsync();

        Assert.Single(model.ApiKeys);
        Assert.Equal("k1", model.ApiKeys[0].Id);
    }

    [Fact]
    public async Task OnGetAsync_DetailsTab_NullDateRange_PassesNullToBilling()
    {
        var auth = new ActivityFakeAuth([
            new ApiKeySummary("k1", "Key 1", "sk-1", Now, Now, null, false)
        ]);
        var billing = new ActivityFakeBilling(
            activity: [],
            summary: new ActivitySummaryInfo(0, 0, 0, 0, 0m, [], []));

        var model = CreateModel(billing, auth);

        await model.OnGetAsync();

        Assert.Null(billing.LastFromUtc);
        Assert.Null(billing.LastToUtc);
    }

    private static ActivityModel CreateModel(ActivityFakeBilling billing, ActivityFakeAuth auth)
    {
        return new ActivityModel(billing, auth, NullStringLocalizer<SharedResource>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                        "test"))
                }
            }
        };
    }

    internal sealed class ActivityFakeAuth : AuthProvider
    {
        private readonly IReadOnlyList<ApiKeySummary> _keys;

        public ActivityFakeAuth(IReadOnlyList<ApiKeySummary> keys)
            : base(client: default!) => _keys = keys;

        public override Task<IReadOnlyList<ApiKeySummary>> ListApiKeysAsync(
            string userId, bool includeDeleted = false, CancellationToken cancellationToken = default)
        {
            var result = includeDeleted
                ? _keys
                : _keys.Where(k => !k.IsDeleted).ToList();
            return Task.FromResult<IReadOnlyList<ApiKeySummary>>(result);
        }
    }

    internal sealed class ActivityFakeBilling : BillingProvider
    {
        private readonly IReadOnlyList<ActivityByTokenInfo> _activity;
        private readonly ActivitySummaryInfo _summary;

        public IReadOnlyCollection<string>? LastKeyIds { get; private set; }
        public DateTimeOffset? LastFromUtc { get; private set; }
        public DateTimeOffset? LastToUtc { get; private set; }
        public string? LastModelFilter { get; private set; }
        public string? LastKeyFilter { get; private set; }

        public ActivityFakeBilling(
            IReadOnlyList<ActivityByTokenInfo> activity,
            ActivitySummaryInfo summary)
            : base(client: default!)
        {
            _activity = activity;
            _summary = summary;
        }

        public override Task<(IReadOnlyList<ActivityByTokenInfo> Items, bool HasMore, string? NextCursor)> ListActivityByTokenAsync(
            IReadOnlyCollection<string> apiKeyIds,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            string? modelIdFilter = null,
            string? apiKeyIdFilter = null,
            string? afterCursor = null,
            int pageSize = 0,
            CancellationToken cancellationToken = default)
        {
            LastKeyIds = apiKeyIds;
            LastFromUtc = fromUtc;
            LastToUtc = toUtc;
            LastModelFilter = modelIdFilter;
            LastKeyFilter = apiKeyIdFilter;
            return Task.FromResult((_activity, false, (string?)null));
        }

        public override Task<ActivitySummaryInfo> GetActivitySummaryAsync(
            IReadOnlyCollection<string> apiKeyIds,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            CancellationToken cancellationToken = default)
        {
            LastKeyIds = apiKeyIds;
            LastFromUtc = fromUtc;
            LastToUtc = toUtc;
            return Task.FromResult(_summary);
        }
    }
}
