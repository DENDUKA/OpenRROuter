using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Pages.Admin;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Collector;
using OpenRussiaRouter.WebApp.Services.ControlPlane;
using OpenRussiaRouter.WebApp.Services.Enrichment;

namespace OpenRussiaRouter.WebApp.Tests;

public class AdminModelsPageTests
{
    [Fact]
    public async Task OnPostSaveAsync_WhenModelIsValid_RedirectsToPageWithoutError()
    {
        var billing = new FakeBillingProvider([new ModelInfo("test-model", "Test Model", "test-model", "v1", 1, 2, 0, true)]);
        var controlPlane = new FakeControlPlaneProvider();
        var collector = new FakeCollectorProvider();
        var openRouter = new FakeOpenRouterCatalogClient();

        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        var modelMetadataProvider = new EmptyModelMetadataProvider();
        var viewData = new ViewDataDictionary(modelMetadataProvider, modelState);

        var pageModel = new ModelsModel(
            billing,
            controlPlane,
            collector,
            openRouter,
            NullLogger<ModelsModel>.Instance)
        {
            PageContext = new PageContext(actionContext)
            {
                ViewData = viewData
            },
            Form = new ModelsModel.ModelFormInput
            {
                Id = "test-model",
                DisplayName = "Test Model",
                InputPrice = 1,
                OutputPrice = 2
            },
            TempData = new TempDataScope(httpContext).Create()
        };

        var result = await pageModel.OnPostSaveAsync(isNew: false);

        result.Should().BeOfType<RedirectToPageResult>();
        var redirect = (RedirectToPageResult)result;
        redirect.PageName.Should().BeNull();
        redirect.RouteValues.Should().BeNull();
    }

    [Fact]
    public async Task OnPostSaveAsync_FlagshipWithoutCompleteOpenRouterPrices_DoesNotSaveAsync()
    {
        var billing = new FakeBillingProvider([]);
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        var pageModel = new ModelsModel(
            billing,
            new FakeControlPlaneProvider(),
            new FakeCollectorProvider(),
            new FakeOpenRouterCatalogClient(),
            NullLogger<ModelsModel>.Instance)
        {
            PageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            },
            Form = new ModelsModel.ModelFormInput
            {
                Id = "gpt-flagship",
                DisplayName = "GPT Flagship",
                InputPrice = 1,
                OutputPrice = 2,
                IsFlagship = true,
                OpenRouterInputPrice = 3,
                OpenRouterOutputPrice = 4
            },
            TempData = new TempDataScope(httpContext).Create()
        };

        var result = await pageModel.OnPostSaveAsync(isNew: true);

        result.Should().BeOfType<PageResult>();
        billing.UpsertCalls.Should().Be(0);
        pageModel.ModelState.Should().ContainKey("Form.OpenRouterCachePrice");
    }

    [Fact]
    public async Task OnGetAsync_SortsModelsByFlagshipFirstThenPriority()
    {
        var models = new List<ModelInfo>
        {
            new("non-flagship-high-prio", "B", "b", "v1", 1, 2, 0, true, Priority: 100, IsFlagship: false),
            new("flagship-low-prio", "A", "a", "v1", 1, 2, 0, true, Priority: 10, IsFlagship: true),
            new("flagship-high-prio", "C", "c", "v1", 1, 2, 0, true, Priority: 50, IsFlagship: true),
            new("non-flagship-low-prio", "D", "d", "v1", 1, 2, 0, true, Priority: 5, IsFlagship: false),
        };

        var billing = new FakeBillingProvider(models);
        var httpContext = new DefaultHttpContext();
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        var pageModel = new ModelsModel(
            billing,
            new FakeControlPlaneProvider(),
            new FakeCollectorProvider(),
            new FakeOpenRouterCatalogClient(),
            NullLogger<ModelsModel>.Instance)
        {
            PageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
            },
            TempData = new TempDataScope(httpContext).Create()
        };

        await pageModel.OnGetAsync();

        pageModel.Models.Select(m => m.Id).Should().Equal(
            "flagship-high-prio",
            "flagship-low-prio",
            "non-flagship-high-prio",
            "non-flagship-low-prio"
        );
    }

    private sealed class FakeBillingProvider : BillingProvider
    {
        private readonly List<ModelInfo> _models;

        public FakeBillingProvider(IEnumerable<ModelInfo> models) : base(client: default!)
        {
            _models = models.ToList();
        }

        public int UpsertCalls { get; private set; }

        public override Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelInfo>>(_models);

        public override Task<IReadOnlyList<ProviderInfo>> ListModelProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderInfo>>([]);

        public override Task UpsertModelAsync(ModelInfo model, CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeControlPlaneProvider : ControlPlaneProvider
    {
        public FakeControlPlaneProvider() : base(client: default!) { }
    }

    private sealed class FakeCollectorProvider : CollectorProvider
    {
        public FakeCollectorProvider() : base(client: default!, logger: NullLogger<CollectorProvider>.Instance) { }

        public override Task<NodeModelCatalogResult> ListNodeModelCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeModelCatalogResult([], [], [], null));
    }

    private sealed class FakeOpenRouterCatalogClient : IOpenRouterCatalogClient
    {
        public Task<IReadOnlyList<OpenRouterModelEntry>> LoadCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OpenRouterModelEntry>>([]);

        public OpenRouterMatchResult? FindMatch(IReadOnlyList<OpenRouterModelEntry> catalog, string modelId) => null;
    }
}
