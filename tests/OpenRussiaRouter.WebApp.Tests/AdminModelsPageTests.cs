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

    private sealed class FakeBillingProvider : BillingProvider
    {
        private readonly List<ModelInfo> _models;

        public FakeBillingProvider(IEnumerable<ModelInfo> models) : base(client: default!)
        {
            _models = models.ToList();
        }

        public override Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelInfo>>(_models);

        public override Task<IReadOnlyList<ProviderInfo>> ListModelProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderInfo>>([]);

        public override Task UpsertModelAsync(ModelInfo model, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

