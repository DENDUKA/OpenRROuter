using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.BuildingBlocks.Analytics;
using OpenRussiaRouter.WebApp.Services.Analytics;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class YandexMetrikaIdentityMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AuthenticatedConsentedRequest_PersistsIdentityOncePerHour()
    {
        var store = new RecordingIdentityStore();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var nextCalls = 0;
        var middleware = CreateMiddleware(store, cache, () => nextCalls++);

        await middleware.InvokeAsync(CreateContext(authenticated: true, consent: "1", clientId: "123456789"));
        await middleware.InvokeAsync(CreateContext(authenticated: true, consent: "1", clientId: "123456789"));

        store.Upserts.Should().ContainSingle()
            .Which.Should().Be(("user-1", "123456789"));
        nextCalls.Should().Be(2);
    }

    [Theory]
    [InlineData(false, "1", "123")]
    [InlineData(true, "0", "123")]
    [InlineData(true, null, "123")]
    [InlineData(true, "1", null)]
    [InlineData(true, "1", "abc")]
    [InlineData(true, "1", "123456789012345678901234567890123")]
    public async Task InvokeAsync_IneligibleRequest_DoesNotPersistIdentity(
        bool authenticated,
        string? consent,
        string? clientId)
    {
        var store = new RecordingIdentityStore();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var nextCalls = 0;
        var middleware = CreateMiddleware(store, cache, () => nextCalls++);

        await middleware.InvokeAsync(CreateContext(authenticated, consent, clientId));

        store.Upserts.Should().BeEmpty();
        nextCalls.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_StoreFailure_ContinuesPipeline()
    {
        var store = new RecordingIdentityStore { ThrowOnUpsert = true };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var nextCalls = 0;
        var middleware = CreateMiddleware(store, cache, () => nextCalls++);

        var action = () => middleware.InvokeAsync(
            CreateContext(authenticated: true, consent: "1", clientId: "123"));

        await action.Should().NotThrowAsync();
        nextCalls.Should().Be(1);
    }

    private static YandexMetrikaIdentityMiddleware CreateMiddleware(
        IYandexMetrikaIdentityStore store,
        IMemoryCache cache,
        Action onNext) =>
        new(
            _ =>
            {
                onNext();
                return Task.CompletedTask;
            },
            store,
            cache,
            TimeProvider.System,
            NullLogger<YandexMetrikaIdentityMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(
        bool authenticated,
        string? consent,
        string? clientId)
    {
        var context = new DefaultHttpContext();
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                authenticationType: "test"));
        }

        var cookies = new List<string>();
        if (consent is not null)
        {
            cookies.Add($"orr_consent={consent}");
        }
        if (clientId is not null)
        {
            cookies.Add($"_ym_uid={clientId}");
        }
        if (cookies.Count > 0)
        {
            context.Request.Headers.Cookie = string.Join("; ", cookies);
        }

        return context;
    }

    private sealed class RecordingIdentityStore : IYandexMetrikaIdentityStore
    {
        public List<(string UserId, string ClientId)> Upserts { get; } = [];

        public bool ThrowOnUpsert { get; init; }

        public Task UpsertAsync(
            string userId,
            string clientId,
            DateTimeOffset observedAtUtc,
            CancellationToken ct = default)
        {
            if (ThrowOnUpsert)
            {
                throw new InvalidOperationException("analytics unavailable");
            }

            Upserts.Add((userId, clientId));
            return Task.CompletedTask;
        }

        public Task<string?> GetClientIdAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> FindRelatedUserIdsAsync(string clientId, string? excludeUserId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
