namespace OpenRussiaRouter.E2ETests;

public sealed class FlagshipCarouselTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public FlagshipCarouselTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Carousel_AutoplaysPausesAndSuppressesClickAfterDragAsync()
    {
        if (!TestConfiguration.IsConfigured)
        {
            return;
        }

        await using var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(BuildHomeUrl());

        var carousel = page.Locator("[data-flagship-carousel]");
        Assert.Equal(1, await carousel.CountAsync());

        var before = await GetScrollLeftAsync(carousel);
        await page.WaitForTimeoutAsync(500);
        var moving = await GetScrollLeftAsync(carousel);
        Assert.True(moving > before);

        await carousel.EvaluateAsync("element => element.style.paddingTop = '32px'");
        var carouselBox = await carousel.BoundingBoxAsync();
        Assert.NotNull(carouselBox);
        await page.Mouse.MoveAsync(carouselBox!.X + 4, carouselBox.Y + 4);
        var paddingAt = await GetScrollLeftAsync(carousel);
        await page.WaitForTimeoutAsync(300);
        var movedOverPadding = await GetScrollLeftAsync(carousel);
        Assert.True(movedOverPadding > paddingAt);

        var originalCycle = carousel.Locator("[data-flagship-cycle]:not([data-flagship-clone])");
        await originalCycle.HoverAsync();
        var pausedAt = await GetScrollLeftAsync(carousel);
        await page.WaitForTimeoutAsync(300);
        var stillPaused = await GetScrollLeftAsync(carousel);
        Assert.InRange(Math.Abs(stillPaused - pausedAt), 0, 0.5);

        await page.Mouse.MoveAsync(carouselBox.X + 4, carouselBox.Y + 4);
        var resumedAt = await GetScrollLeftAsync(carousel);
        await page.WaitForTimeoutAsync(1_000);
        var resumed = await GetScrollLeftAsync(carousel);
        Assert.True(resumed > resumedAt);

        var original = OriginalCard(carousel);
        Assert.False(string.IsNullOrWhiteSpace(await original.GetAttributeAsync("href")));
        var visualClones = carousel.Locator("[data-flagship-clone][aria-hidden='true'] a[tabindex='-1']");
        var cloneCount = await visualClones.CountAsync();
        Assert.True(cloneCount > 0);

        await carousel.EvaluateAsync("element => window.FlagshipCarousel.initialize(element)");
        Assert.Equal(cloneCount, await visualClones.CountAsync());

        var box = await original.BoundingBoxAsync();
        Assert.NotNull(box);
        var initialUrl = page.Url;
        await page.Mouse.MoveAsync(box!.X + 40, box.Y + 40);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 100, box.Y + 40);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.Equal(initialUrl, page.Url);

        var paddingY = carouselBox.Y + 4;
        await page.Mouse.MoveAsync(carouselBox.X + carouselBox.Width * 0.65f, paddingY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(carouselBox.X + carouselBox.Width * 0.35f, paddingY);
        await page.Mouse.UpAsync();
        var inertiaAtRelease = await GetScrollLeftAsync(carousel);
        await page.WaitForTimeoutAsync(100);
        var inertiaAfterFirstSample = await GetScrollLeftAsync(carousel);
        await page.WaitForTimeoutAsync(100);
        var inertiaAfterSecondSample = await GetScrollLeftAsync(carousel);
        Assert.True(Math.Abs(inertiaAfterFirstSample - inertiaAtRelease) > 0.5);
        Assert.True(Math.Abs(inertiaAfterSecondSample - inertiaAfterFirstSample) > 0.5);
    }

    [Fact]
    public async Task Carousel_ReducedMotionDisablesAutoplayAndKeepsLinksClickableAsync()
    {
        if (!TestConfiguration.IsConfigured)
        {
            return;
        }

        await using var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(BuildHomeUrl());

        var carousel = page.Locator("[data-flagship-carousel]");
        Assert.Equal(1, await carousel.CountAsync());

        var before = await GetScrollLeftAsync(carousel);
        await page.WaitForTimeoutAsync(500);
        var after = await GetScrollLeftAsync(carousel);
        Assert.InRange(Math.Abs(after - before), 0, 0.5);

        var original = OriginalCard(carousel);
        var href = await original.GetAttributeAsync("href");
        Assert.False(string.IsNullOrWhiteSpace(href));

        await original.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains(href!, StringComparison.Ordinal));
    }

    private static string BuildHomeUrl() => $"{TestConfiguration.BaseUrl.TrimEnd('/')}/";

    private static ILocator OriginalCard(ILocator carousel) =>
        carousel.Locator("[data-flagship-cycle]:not([data-flagship-clone]) > .flagship-card:not([data-flagship-clone])").First;

    private static Task<double> GetScrollLeftAsync(ILocator carousel) =>
        carousel.EvaluateAsync<double>("element => element.scrollLeft");
}
