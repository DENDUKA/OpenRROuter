using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Domain.Entities;
using OpenRussiaRouter.BillingService.Domain.Enums;
using OpenRussiaRouter.BillingService.Infrastructure.Workers;
using OpenRussiaRouter.BillingService.Infrastructure.YandexMetrika;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public sealed class YandexMetrikaOfflineConversionWorkerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessBatchAsync_MissingClientId_LeavesEventPending()
    {
        var conversion = CreateConversion();
        var repository = CreateRepository(conversion);
        var identityStore = new Mock<IYandexMetrikaIdentityStore>();
        identityStore.Setup(x => x.GetClientIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var client = new RecordingMetrikaClient();
        var worker = CreateWorker(repository.Object, identityStore.Object, client);

        await worker.ProcessBatchAsync(CancellationToken.None);

        repository.Verify(x => x.UpdateYandexMetrikaOfflineConversionAsync(
            conversion.Id,
            YandexMetrikaOfflineConversionStatus.Pending,
            Now.AddHours(1),
            null,
            null,
            "client_id_not_available",
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
        client.UploadCalls.Should().Be(0);
        client.FindCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatchAsync_ExpiredWithoutClientId_MarksEventExpired()
    {
        var conversion = CreateConversion() with { OccurredAtUtc = Now.AddDays(-20) };
        var repository = CreateRepository(conversion);
        var identityStore = new Mock<IYandexMetrikaIdentityStore>();
        var client = new RecordingMetrikaClient();
        var worker = CreateWorker(repository.Object, identityStore.Object, client);

        await worker.ProcessBatchAsync(CancellationToken.None);

        identityStore.Verify(
            x => x.GetClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(x => x.UpdateYandexMetrikaOfflineConversionAsync(
            conversion.Id,
            YandexMetrikaOfflineConversionStatus.Expired,
            Now,
            null,
            null,
            "client_id_expired",
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_ResolvedIdentity_UploadsAndMarksProcessed()
    {
        var conversion = CreateConversion();
        var repository = CreateRepository(conversion);
        repository.Setup(x => x.SetYandexMetrikaClientIdAsync(
                conversion.Id,
                "123456789",
                Now,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("123456789");
        var identityStore = new Mock<IYandexMetrikaIdentityStore>();
        identityStore.Setup(x => x.GetClientIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("123456789");
        var client = new RecordingMetrikaClient
        {
            UploadResult = new YandexMetrikaUploading(42, 1, 1, conversion.Comment, "PROCESSED")
        };
        var worker = CreateWorker(repository.Object, identityStore.Object, client);

        await worker.ProcessBatchAsync(CancellationToken.None);

        repository.Verify(x => x.UpdateYandexMetrikaOfflineConversionAsync(
            conversion.Id,
            YandexMetrikaOfflineConversionStatus.Processed,
            Now,
            42,
            "PROCESSED",
            null,
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_SnapshottedClientId_DoesNotReadIdentityAgain()
    {
        var conversion = CreateConversion() with { ResolvedClientId = "111" };
        var repository = CreateRepository(conversion);
        var identityStore = new Mock<IYandexMetrikaIdentityStore>();
        var client = new RecordingMetrikaClient
        {
            UploadResult = new YandexMetrikaUploading(7, 1, 1, conversion.Comment, "PROCESSED")
        };
        var worker = CreateWorker(repository.Object, identityStore.Object, client);

        await worker.ProcessBatchAsync(CancellationToken.None);

        identityStore.Verify(
            x => x.GetClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.UploadCalls.Should().Be(1);
        client.LastClientId.Should().Be("111");
    }

    [Fact]
    public async Task ProcessBatchAsync_Reconciling_FindsExistingUploadBeforeSending()
    {
        var conversion = CreateConversion() with
        {
            ResolvedClientId = "111",
            Status = YandexMetrikaOfflineConversionStatus.Reconciling
        };
        var repository = CreateRepository(conversion);
        var identityStore = new Mock<IYandexMetrikaIdentityStore>();
        var client = new RecordingMetrikaClient
        {
            FindResult = new YandexMetrikaUploading(9, 1, 1, conversion.Comment, "PROCESSED")
        };
        var worker = CreateWorker(repository.Object, identityStore.Object, client);

        await worker.ProcessBatchAsync(CancellationToken.None);

        client.UploadCalls.Should().Be(0);
        client.FindCalls.Should().Be(1);
        repository.Verify(x => x.UpdateYandexMetrikaOfflineConversionAsync(
            conversion.Id,
            YandexMetrikaOfflineConversionStatus.Processed,
            Now,
            9,
            "PROCESSED",
            null,
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_LinkageFailure_MarksEventFailed()
    {
        var conversion = CreateConversion() with { ResolvedClientId = "111" };
        var repository = CreateRepository(conversion);
        var identityStore = new Mock<IYandexMetrikaIdentityStore>();
        var client = new RecordingMetrikaClient
        {
            UploadResult = new YandexMetrikaUploading(11, 1, 1, conversion.Comment, "LINKAGE_FAILURE")
        };
        var worker = CreateWorker(repository.Object, identityStore.Object, client);

        await worker.ProcessBatchAsync(CancellationToken.None);

        repository.Verify(x => x.UpdateYandexMetrikaOfflineConversionAsync(
            conversion.Id,
            YandexMetrikaOfflineConversionStatus.Failed,
            Now,
            11,
            "LINKAGE_FAILURE",
            "linkage_failure",
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_TooManyRequests_UsesRetryAfter()
    {
        var conversion = CreateConversion() with { ResolvedClientId = "111" };
        var repository = CreateRepository(conversion);
        var identityStore = new Mock<IYandexMetrikaIdentityStore>();
        var client = new RecordingMetrikaClient
        {
            UploadException = new YandexMetrikaApiException(
                HttpStatusCode.TooManyRequests,
                "rate limited",
                TimeSpan.FromMinutes(7))
        };
        var worker = CreateWorker(repository.Object, identityStore.Object, client);

        await worker.ProcessBatchAsync(CancellationToken.None);

        repository.Verify(x => x.UpdateYandexMetrikaOfflineConversionAsync(
            conversion.Id,
            YandexMetrikaOfflineConversionStatus.Pending,
            Now.AddMinutes(7),
            null,
            null,
            "http_429",
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_BadRequest_MarksEventFailed()
    {
        var conversion = CreateConversion() with { ResolvedClientId = "111" };
        var repository = CreateRepository(conversion);
        var identityStore = new Mock<IYandexMetrikaIdentityStore>();
        var client = new RecordingMetrikaClient
        {
            UploadException = new YandexMetrikaApiException(HttpStatusCode.BadRequest, "bad csv")
        };
        var worker = CreateWorker(repository.Object, identityStore.Object, client);

        await worker.ProcessBatchAsync(CancellationToken.None);

        repository.Verify(x => x.UpdateYandexMetrikaOfflineConversionAsync(
            conversion.Id,
            YandexMetrikaOfflineConversionStatus.Failed,
            Now,
            null,
            null,
            "http_400",
            Now,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_SendsOAuthCsvAndDeterministicComment()
    {
        var conversion = CreateConversion() with
        {
            Price = 1500.5m,
            Currency = "RUB"
        };
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new DelegateHandler(async (request, ct) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"uploading\":{\"id\":5,\"source_quantity\":1,\"line_quantity\":1,\"comment\":\"" +
                    conversion.Comment +
                    "\",\"status\":\"UPLOADED\"}}",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var options = Options.Create(CreateOptions());
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.Value.BaseUrl)
        };
        var client = new YandexMetrikaClient(httpClient, options);

        var uploading = await client.UploadAsync(conversion, "123456789", CancellationToken.None);

        uploading.Id.Should().Be(5);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Authorization!.Scheme.Should().Be("OAuth");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("secret-token");
        capturedRequest.RequestUri!.Query.Should().Contain(Uri.EscapeDataString(conversion.Comment));
        capturedBody.Should().Contain("ClientId,Target,DateTime,Price,Currency");
        capturedBody.Should().Contain("123456789,first_api_request,1788350400,1500.5,RUB");
    }

    private static Mock<IBillingRepository> CreateRepository(
        YandexMetrikaOfflineConversion conversion)
    {
        var repository = new Mock<IBillingRepository>();
        repository.Setup(x => x.AcquireYandexMetrikaOfflineConversionsAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                Now,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([conversion]);
        return repository;
    }

    private static YandexMetrikaOfflineConversionWorker CreateWorker(
        IBillingRepository repository,
        IYandexMetrikaIdentityStore identityStore,
        IYandexMetrikaClient client) =>
        new(
            repository,
            identityStore,
            client,
            Options.Create(CreateOptions()),
            new FixedTimeProvider(Now),
            NullLogger<YandexMetrikaOfflineConversionWorker>.Instance);

    private static YandexMetrikaOfflineConversionOptions CreateOptions() =>
        new()
        {
            Enabled = true,
            CounterId = 111582266,
            OAuthToken = "secret-token",
            BaseUrl = "https://api-metrika.yandex.net/",
            WorkerInterval = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(2),
            MissingIdentityRetryInterval = TimeSpan.FromHours(1),
            UploadStatusPollInterval = TimeSpan.FromMinutes(5),
            BaseRetryDelay = TimeSpan.FromMinutes(1),
            MaxRetryDelay = TimeSpan.FromHours(1),
            ConfigurationRetryInterval = TimeSpan.FromMinutes(15),
            ExpirationWindow = TimeSpan.FromDays(20),
            HttpTimeout = TimeSpan.FromSeconds(30),
            BatchSize = 20
        };

    private static YandexMetrikaOfflineConversion CreateConversion() =>
        new(
            "conversion-1",
            "first_api_request:user-1",
            "user-1",
            "first_api_request",
            Now,
            null,
            null,
            null,
            YandexMetrikaOfflineConversionStatus.Pending,
            "worker",
            Now.AddMinutes(2),
            0,
            Now,
            null,
            null,
            "orr:conversion-1",
            null,
            Now,
            Now);

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class RecordingMetrikaClient : IYandexMetrikaClient
    {
        public YandexMetrikaUploading? UploadResult { get; init; }
        public Exception? UploadException { get; init; }
        public YandexMetrikaUploading? FindResult { get; init; }
        public int UploadCalls { get; private set; }
        public int FindCalls { get; private set; }
        public string? LastClientId { get; private set; }

        public Task<YandexMetrikaUploading> UploadAsync(
            YandexMetrikaOfflineConversion conversion,
            string clientId,
            CancellationToken ct)
        {
            UploadCalls++;
            LastClientId = clientId;
            if (UploadException is not null)
            {
                return Task.FromException<YandexMetrikaUploading>(UploadException);
            }

            return Task.FromResult(
                UploadResult ?? throw new InvalidOperationException("UploadResult was not configured."));
        }

        public Task<YandexMetrikaUploading?> FindByCommentAsync(
            string comment,
            CancellationToken ct)
        {
            FindCalls++;
            return Task.FromResult(FindResult);
        }

        public Task<YandexMetrikaUploading> GetUploadingAsync(
            long uploadId,
            CancellationToken ct) =>
            Task.FromResult(
                UploadResult ?? throw new InvalidOperationException("UploadResult was not configured."));
    }
}
