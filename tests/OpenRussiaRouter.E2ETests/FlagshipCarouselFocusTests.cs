using System.Collections.Concurrent;

namespace OpenRussiaRouter.E2ETests;

/// <summary>Browser regressions using the production carousel script and an isolated, offline page.</summary>
public sealed class FlagshipCarouselFocusTests(PlaywrightFixture fixture)
    : IClassFixture<PlaywrightFixture>
{
    private const string OriginalSelector =
        "[data-flagship-cycle]:not([data-flagship-clone]) > .flagship-card:not([data-flagship-clone])";

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(2)]
    public async Task Carousel_WithoutHoverMovesAtConfiguredSpeedAsync(float deviceScaleFactor)
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync(reducedMotion: ReducedMotion.NoPreference, deviceScaleFactor: deviceScaleFactor);
        var page = await CreatePageAsync(context, "ru", controlledClock: true);
        Assert.False(await page.EvaluateAsync<bool>("() => !!document.querySelector('[data-flagship-cycle]:hover')"));
        var carousel = page.Locator("[data-flagship-carousel]");
        var before = await carousel.EvaluateAsync<double>("element => element.scrollLeft");

        await page.Clock.RunForAsync(1_000);

        var after = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        Assert.InRange(after - before, 18, 21);
    }

    [Fact]
    public async Task Carousel_HoverPausesAndLeavingResumesEvenWithFocusedCardAsync()
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync(reducedMotion: ReducedMotion.NoPreference);
        var page = await CreatePageAsync(context, "ru", controlledClock: true);
        await page.Clock.RunForAsync(1_000);
        var original = page.Locator(OriginalSelector);
        var box = await original.BoundingBoxAsync();
        Assert.NotNull(box);
        await page.Mouse.MoveAsync(box!.X + 100, box.Y + 40);
        var pausedAt = await GetScrollLeftAsync(page);
        await page.Clock.RunForAsync(1_000);
        Assert.Equal(pausedAt, await GetScrollLeftAsync(page));

        await original.EvaluateAsync("element => element.focus({ preventScroll: true })");
        await page.Mouse.MoveAsync(900, 400);
        await page.Clock.RunForAsync(700);
        Assert.Equal(pausedAt, await GetScrollLeftAsync(page));
        await page.Clock.RunForAsync(1_100);
        Assert.InRange(await GetScrollLeftAsync(page) - pausedAt, 18, 21);
        await AssertOriginalFocusAsync(page);
    }

    [Fact]
    public async Task Carousel_ExternalScrollAndWheelDelayDoNotResetPositionAndLoopContinuesAsync()
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync(reducedMotion: ReducedMotion.NoPreference);
        var page = await CreatePageAsync(context, "en", controlledClock: true);
        await page.Clock.RunForAsync(500);
        var carousel = page.Locator("[data-flagship-carousel]");
        await carousel.EvaluateAsync("""
            element => {
                element.dispatchEvent(new WheelEvent('wheel', { deltaX: 80 }));
                element.scrollLeft += 80;
            }
            """);
        var manuallyScrolledTo = await GetScrollLeftAsync(page);
        await page.Clock.RunForAsync(700);
        Assert.Equal(manuallyScrolledTo, await GetScrollLeftAsync(page));
        await page.Clock.RunForAsync(1_100);
        Assert.InRange(await GetScrollLeftAsync(page) - manuallyScrolledTo, 18, 21);

        var cycleWidth = await carousel.EvaluateAsync<double>("""
            element => {
                const track = element.querySelector('[data-flagship-track]');
                const cycle = track.querySelector('[data-flagship-cycle]:not([data-flagship-clone])');
                const width = cycle.getBoundingClientRect().width + parseFloat(getComputedStyle(track).columnGap);
                element.scrollLeft = 2 * width - 5;
                return width;
            }
            """);
        await page.Clock.RunForAsync(1_000);
        Assert.InRange(await GetScrollLeftAsync(page) - cycleWidth, 14, 16);
    }

    [Fact]
    public async Task Carousel_TouchDragRetainsInertiaThenResumesSlowAutoplayAsync()
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync(hasTouch: true, reducedMotion: ReducedMotion.NoPreference);
        var page = await CreatePageAsync(context, "ru", controlledClock: true);
        var cdp = await context.NewCDPSessionAsync(page);
        await DispatchTouchAsync(cdp, "touchStart", 300);
        await page.Clock.RunForAsync(16);
        await DispatchTouchAsync(cdp, "touchMove", 280);
        await page.Clock.RunForAsync(16);
        await DispatchTouchAsync(cdp, "touchMove", 260);
        await DispatchTouchAsync(cdp, "touchEnd", null);
        var releasedAt = await GetScrollLeftAsync(page);
        await page.Clock.RunForAsync(100);
        var firstSample = await GetScrollLeftAsync(page);
        await page.Clock.RunForAsync(100);
        var secondSample = await GetScrollLeftAsync(page);
        Assert.True(firstSample - releasedAt > 5);
        Assert.InRange(secondSample - firstSample, 5, firstSample - releasedAt);

        await page.Clock.RunForAsync(1_000);
        var stoppedAt = await GetScrollLeftAsync(page);
        await page.Clock.RunForAsync(200);
        Assert.Equal(stoppedAt, await GetScrollLeftAsync(page));
        await page.Clock.RunForAsync(1_400);
        Assert.InRange(await GetScrollLeftAsync(page) - stoppedAt, 15, 25);
        Assert.Equal("https://carousel.test/ru", page.Url);
    }

    [Fact]
    public async Task Carousel_ReducedMotionDisablesAutomaticMovementAsync()
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync();
        var page = await CreatePageAsync(context, "ru", controlledClock: true);
        var before = await GetScrollLeftAsync(page);
        await page.Clock.RunForAsync(5_000);
        Assert.Equal(before, await GetScrollLeftAsync(page));
    }

    [Fact]
    public async Task Carousel_RealAnimationFramesMoveWithoutHoverAsync()
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync(reducedMotion: ReducedMotion.NoPreference);
        var page = await CreatePageAsync(context, "ru");
        await page.Mouse.MoveAsync(900, 400);
        var distance = await page.EvaluateAsync<double>("""
            () => new Promise(resolve => {
                const viewport = document.querySelector('[data-flagship-carousel]');
                const start = viewport.scrollLeft;
                const startedAt = performance.now();
                function sample(now) {
                    if (now - startedAt >= 2000) resolve(viewport.scrollLeft - start);
                    else requestAnimationFrame(sample);
                }
                requestAnimationFrame(sample);
            })
            """);
        Assert.InRange(distance, 15, 45);
    }

    private static Task<double> GetScrollLeftAsync(IPage page) =>
        page.Locator("[data-flagship-carousel]").EvaluateAsync<double>("element => element.scrollLeft");

    private static Task DispatchTouchAsync(ICDPSession cdp, string type, int? x) =>
        cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = x.HasValue ? new object[] { new { x = x.Value, y = 40 } } : []
        });

    [Theory]
    [InlineData(false, "ru")]
    [InlineData(true, "ru")]
    [InlineData(false, "en")]
    [InlineData(true, "en")]
    public async Task Clone_PointerFocusUsesOriginalWithoutScrollingAndClickNavigatesAsync(bool wholeCycle, string locale)
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync();
        var page = await CreatePageAsync(context, locale);
        var (diagnostics, cdp) = await ObserveAccessibilityAsync(page);
        var clone = await PositionCloneAsync(page, wholeCycle);
        var carousel = page.Locator("[data-flagship-carousel]");
        var scrollBefore = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        var box = await clone.BoundingBoxAsync();
        Assert.NotNull(box);

        // A real pointerdown catches the native focus default, not just synthetic click handlers.
        await page.Mouse.MoveAsync(box!.X + 40, box.Y + 40);
        await page.Mouse.DownAsync();

        await AssertOriginalFocusAsync(page);
        Assert.InRange(Math.Abs(await carousel.EvaluateAsync<double>("element => element.scrollLeft") - scrollBefore), 0, 0.5);
        await AssertNoAccessibilityWarningsAsync(diagnostics, cdp);
        await page.Mouse.UpAsync();
        await page.WaitForURLAsync($"https://carousel.test/{locale}/models/test/flagship");
        await AssertNoAccessibilityWarningsAsync(diagnostics, cdp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clone_ProgrammaticFocusRedirectsAfterRebuildAsync(bool wholeCycle)
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync();
        var page = await CreatePageAsync(context, "ru");
        var (diagnostics, cdp) = await ObserveAccessibilityAsync(page);
        var clone = await PositionCloneAsync(page, wholeCycle);
        await clone.FocusAsync();
        await AssertOriginalFocusAsync(page);
        await AssertNoAccessibilityWarningsAsync(diagnostics, cdp);

        var cloneCount = await page.Locator("[data-flagship-clone]").CountAsync();
        await page.Locator("[data-flagship-carousel]").EvaluateAsync("element => window.FlagshipCarousel.initialize(element)");
        Assert.Equal(cloneCount, await page.Locator("[data-flagship-clone]").CountAsync());
        await page.Locator("[data-flagship-carousel]").EvaluateAsync("element => element.style.width = '620px'");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        clone = await PositionCloneAsync(page, wholeCycle);
        await clone.FocusAsync();
        await AssertOriginalFocusAsync(page);
        await AssertNoAccessibilityWarningsAsync(diagnostics, cdp);
    }

    [Fact]
    public async Task Clone_WithDuplicateHrefFocusesMatchingOriginalAsync()
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync();
        var page = await CreatePageAsync(context, "ru", duplicateHref: true);
        var clone = page.Locator("[data-flagship-cycle]:not([data-flagship-clone]) > .flagship-card[data-flagship-clone]").Nth(1);
        await clone.FocusAsync();

        Assert.True(await page.Locator(OriginalSelector).Nth(1).EvaluateAsync<bool>("element => element === document.activeElement"));
        Assert.False(await page.EvaluateAsync<bool>("() => !!document.activeElement.closest('[aria-hidden=\"true\"]')"));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public async Task OriginalCard_RetainsNativeFocusAndNavigationAsync(string locale)
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync();
        var page = await CreatePageAsync(context, locale);
        var original = page.Locator(OriginalSelector);
        await original.FocusAsync();
        await AssertOriginalFocusAsync(page);
        await original.ClickAsync();
        await page.WaitForURLAsync($"https://carousel.test/{locale}/models/test/flagship");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clone_DragStillScrollsAndSuppressesNavigationAsync(bool wholeCycle)
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync();
        var page = await CreatePageAsync(context, "ru");
        var clone = await PositionCloneAsync(page, wholeCycle);
        var carousel = page.Locator("[data-flagship-carousel]");
        var before = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        var box = await clone.BoundingBoxAsync();
        Assert.NotNull(box);
        await page.Mouse.MoveAsync(box!.X + 100, box.Y + 40);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 160, box.Y + 40);
        await page.Mouse.UpAsync();

        Assert.InRange(before - await carousel.EvaluateAsync<double>("element => element.scrollLeft"), 59, 61);
        Assert.Equal("https://carousel.test/ru", page.Url);
        await AssertOriginalFocusAsync(page);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Clone_ModifiedClickStillOpensModelInNewTabAsync(bool wholeCycle, bool middleClick)
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync();
        var page = await CreatePageAsync(context, "en");
        var clone = await PositionCloneAsync(page, wholeCycle);
        var popup = await context.RunAndWaitForPageAsync(async () => await clone.ClickAsync(new LocatorClickOptions
        {
            Button = middleClick ? MouseButton.Middle : MouseButton.Left,
            Modifiers = middleClick ? [] : [KeyboardModifier.Control]
        }));

        await popup.WaitForURLAsync("https://carousel.test/en/models/test/flagship");
        Assert.Equal("https://carousel.test/en", page.Url);
        await AssertOriginalFocusAsync(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clone_TouchTapStillNavigatesWithoutHiddenFocusAsync(bool wholeCycle)
    {
        if (!TestConfiguration.IsConfigured) return;

        await using var context = await CreateContextAsync(hasTouch: true);
        var page = await CreatePageAsync(context, "ru");
        var (diagnostics, cdp) = await ObserveAccessibilityAsync(page);
        var clone = await PositionCloneAsync(page, wholeCycle);
        await clone.TapAsync();
        await page.WaitForURLAsync("https://carousel.test/ru/models/test/flagship");
        await AssertNoAccessibilityWarningsAsync(diagnostics, cdp);
    }

    private Task<IBrowserContext> CreateContextAsync(bool hasTouch = false,
        ReducedMotion reducedMotion = ReducedMotion.Reduce, float deviceScaleFactor = 1) =>
        fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ReducedMotion = reducedMotion,
            DeviceScaleFactor = deviceScaleFactor,
            HasTouch = hasTouch
        });

    private static async Task<IPage> CreatePageAsync(IBrowserContext context, string locale,
        bool duplicateHref = false, bool controlledClock = false)
    {
        var secondCard = duplicateHref
            ? $"<a class=\"flagship-card\" href=\"/{locale}/models/test/flagship\"><span>Second model</span></a>"
            : string.Empty;
        var html = $$"""
            <!doctype html><html lang="{{locale}}"><head><style>
            body { margin: 0; }
            [data-flagship-carousel] { width: 640px; overflow-x: auto; touch-action: pan-y; }
            [data-flagship-track], [data-flagship-cycle] { display: flex; width: max-content; gap: 24px; }
            .flagship-card { display: block; flex: 0 0 280px; width: 280px; height: 180px; }
            </style></head><body>
            <div data-flagship-carousel><div data-flagship-track><div data-flagship-cycle>
            <a class="flagship-card" href="/{{locale}}/models/test/flagship"><span>Flagship model</span></a>
            {{secondCard}}
            </div></div></div></body></html>
            """;
        await context.RouteAsync("https://carousel.test/**", async route => await route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "text/html",
            Body = new Uri(route.Request.Url).AbsolutePath == $"/{locale}" ? html : "<!doctype html><title>Model detail</title>"
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync($"https://carousel.test/{locale}");
        if (controlledClock)
        {
            await page.Clock.InstallAsync(new ClockInstallOptions());
            await page.Clock.PauseAtAsync(DateTime.UtcNow.AddMinutes(1));
            await page.Mouse.MoveAsync(900, 400);
        }
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "OpenRussiaRouter.WebApp", "wwwroot", "js", "flagship-carousel.js"))
        });
        return page;
    }

    private static async Task<ILocator> PositionCloneAsync(IPage page, bool wholeCycle)
    {
        var clone = page.Locator(wholeCycle
            ? "[data-flagship-track] > [data-flagship-cycle][data-flagship-clone]:last-child > .flagship-card:first-child"
            : "[data-flagship-cycle]:not([data-flagship-clone]) > .flagship-card[data-flagship-clone]").First;
        await clone.EvaluateAsync("""
            element => {
                const viewport = element.closest('[data-flagship-carousel]');
                viewport.scrollLeft += element.getBoundingClientRect().left - viewport.getBoundingClientRect().left - 30;
            }
            """);
        return clone;
    }

    private static async Task AssertOriginalFocusAsync(IPage page)
    {
        Assert.False(await page.EvaluateAsync<bool>("() => !!document.activeElement.closest('[aria-hidden=\"true\"]')"));
        Assert.True(await page.Locator(OriginalSelector).EvaluateAsync<bool>("element => element === document.activeElement"));
    }

    private static async Task<(ConcurrentQueue<string> Diagnostics, ICDPSession Session)> ObserveAccessibilityAsync(IPage page)
    {
        var diagnostics = new ConcurrentQueue<string>();
        var cdp = await page.Context.NewCDPSessionAsync(page);
        cdp.Event("Log.entryAdded").OnEvent += (_, entry) => diagnostics.Enqueue(entry?.ToString() ?? string.Empty);
        await cdp.SendAsync("Log.enable");
        await cdp.SendAsync("Accessibility.enable");
        return (diagnostics, cdp);
    }

    private static async Task AssertNoAccessibilityWarningsAsync(ConcurrentQueue<string> diagnostics, ICDPSession cdp)
    {
        // Flush the accessibility tree while focus is still on the page, not after navigation hides the bug.
        await cdp.SendAsync("Accessibility.getFullAXTree");
        Assert.DoesNotContain(diagnostics, text => text.Contains("Blocked aria-hidden", StringComparison.OrdinalIgnoreCase));
    }
}
