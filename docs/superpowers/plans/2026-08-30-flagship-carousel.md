# Flagship Carousel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the static flagship-model grid with a slow, seamless, manually scrollable carousel and show the platform price before the crossed-out OpenRouter price.

**Architecture:** Razor renders one semantic set of model links inside a horizontal viewport. A dependency-free controller in `wwwroot/js/flagship-carousel.js` creates non-semantic visual copies, advances `scrollLeft` with `requestAnimationFrame`, normalizes the cycle, and coordinates hover, drag, touch, visibility, resize, and reduced-motion state. Existing server-side model selection and canonical URLs remain unchanged.

**Tech Stack:** ASP.NET Core Razor Pages, vanilla JavaScript, CSS, xUnit/FluentAssertions, optional Microsoft Playwright E2E.

## Global Constraints

- Preserve all existing uncommitted changes in `Index.cshtml`, resources, page models, and tests.
- Do not change Billing contracts, flagship selection, sorting, or canonical model URL construction.
- Carousel speed is approximately 20 px/s and movement is continuous from right to left.
- Pause immediately while the pointer hovers over the carousel and while the user drags or touches it.
- Manual interaction uses mouse drag, trackpad horizontal scroll, and touch swipe; add no arrows, dots, or custom keyboard commands.
- A click without dragging opens the model link; a drag suppresses the accidental click that follows it.
- Original model links remain semantic. Visual clones use `aria-hidden="true"` and their links use `tabindex="-1"`.
- With `prefers-reduced-motion: reduce`, do not start autoplay; manual scrolling and links still work.
- Async C# methods and tests added by this plan end with `Async`; never use `GetAwaiter()`.
- Prefix every verification command with `rtk` and run .NET commands with `-nr:false -m:1`.

---

## File Map

- Modify `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml`: carousel markup, reversed price columns, viewport/track/card CSS, deferred script reference.
- Create `src/OpenRussiaRouter.WebApp/wwwroot/js/flagship-carousel.js`: isolated carousel controller and automatic initialization.
- Modify `tests/OpenRussiaRouter.WebApp.Tests/SeoHeadTests.cs`: rendered HTML contract for price order and carousel structure.
- Create `tests/OpenRussiaRouter.E2ETests/FlagshipCarouselTests.cs`: browser behavior checks when the E2E environment is configured.
- Modify `src/OpenRussiaRouter.WebApp/CLAUDE.md`: document the carousel interaction and clone accessibility contract.

### Task 1: Render the carousel and reverse price columns

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml:125-198,399-598,676-705`
- Modify: `tests/OpenRussiaRouter.WebApp.Tests/SeoHeadTests.cs:109-131`

**Interfaces:**
- Consumes: `IndexModel.FlagshipModels`, `FlagshipModelCard.Path`, existing localized price labels.
- Produces: `[data-flagship-carousel]` viewport containing `[data-flagship-track]` and original `.flagship-card` links for the JavaScript controller.

- [ ] **Step 1: Strengthen the rendered-HTML test so it fails on the current grid**

Add assertions to `Get_Home_RendersLocalizedFlagshipPriceComparisonAsync`:

```csharp
var oursIndex = body.IndexOf($"<span>{ourPriceLabel}</span>", StringComparison.Ordinal);
var openRouterIndex = body.IndexOf("<span>OpenRouter</span>", StringComparison.Ordinal);

oursIndex.Should().BeGreaterThan(-1);
openRouterIndex.Should().BeGreaterThan(oursIndex);
body.Should().Contain("data-flagship-carousel");
body.Should().Contain("data-flagship-track");
body.Should().NotContain("data-flagship-previous");
body.Should().NotContain("data-flagship-next");
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
rtk dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-restore -nr:false -m:1 --filter "FullyQualifiedName~Get_Home_RendersLocalizedFlagshipPriceComparisonAsync"
```

Expected: FAIL because the current HTML renders `OpenRouter` before `Our price` and has no carousel data attributes.

- [ ] **Step 3: Replace the static grid wrapper and reverse each price pair**

Use this structure in `Index.cshtml` while preserving the complete existing card body:

```cshtml
<div class="flagship-carousel" data-flagship-carousel>
    <div class="flagship-track" data-flagship-track>
        <div class="flagship-cycle" data-flagship-cycle>
            @foreach (var flagship in Model.FlagshipModels)
            {
            <a class="flagship-card" href="@flagship.Path"
               aria-label="@Localizer["Home_FlagshipCardAria", flagship.DisplayName]">
                @* Existing header and model copy stay unchanged. *@
                <div class="flagship-price-comparison">
                    <div class="flagship-price-legend" aria-hidden="true">
                        <span></span>
                        <span>@Localizer["Home_PriceOurs"]</span>
                        <span>@Localizer["Home_PriceOpenRouter"]</span>
                    </div>
                    <div class="flagship-price-row">
                        <span class="flagship-price-kind">@Localizer["Home_PriceInput"]</span>
                        <strong>@IndexModel.FormatUsdPrice(flagship.InputPrice)</strong>
                        <del>@IndexModel.FormatUsdPrice(flagship.OpenRouterInputPrice)</del>
                    </div>
                    <div class="flagship-price-row">
                        <span class="flagship-price-kind">@Localizer["Home_PriceOutput"]</span>
                        <strong>@IndexModel.FormatUsdPrice(flagship.OutputPrice)</strong>
                        <del>@IndexModel.FormatUsdPrice(flagship.OpenRouterOutputPrice)</del>
                    </div>
                    <div class="flagship-price-row">
                        <span class="flagship-price-kind">@Localizer["Home_PriceCache"]</span>
                        <strong>@IndexModel.FormatUsdPrice(flagship.CachePrice)</strong>
                        <del>@IndexModel.FormatUsdPrice(flagship.OpenRouterCachePrice)</del>
                    </div>
                </div>
                @* Existing footer stays unchanged. *@
            </a>
            }
        </div>
    </div>
</div>
```

Replace `.flagship-grid` rules with a horizontally scrollable viewport and fixed-width flex track:

```css
.flagship-carousel {
    position: relative;
    overflow-x: auto;
    overflow-y: hidden;
    margin-inline: calc(50% - 50vw);
    padding-inline: max(24px, calc((100vw - 1180px) / 2));
    cursor: grab;
    scrollbar-width: none;
    overscroll-behavior-inline: contain;
    touch-action: pan-y;
    mask-image: linear-gradient(90deg, transparent, #000 42px, #000 calc(100% - 42px), transparent);
    -webkit-mask-image: linear-gradient(90deg, transparent, #000 42px, #000 calc(100% - 42px), transparent);
}
.flagship-carousel::-webkit-scrollbar { display: none; }
.flagship-carousel.is-dragging { cursor: grabbing; user-select: none; }
.flagship-track {
    display: flex;
    width: max-content;
    gap: 14px;
}
.flagship-cycle {
    display: flex;
    gap: 14px;
}
.flagship-card {
    width: clamp(280px, 28vw, 330px);
    flex: 0 0 clamp(280px, 28vw, 330px);
}
```

Retain the current card hover/focus styling. Add:

```cshtml
<script src="~/js/flagship-carousel.js" asp-append-version="true" defer></script>
```

- [ ] **Step 4: Run the focused test and verify it passes**

Run the command from Step 2. Expected: both RU and EN theory cases PASS.

- [ ] **Step 5: Commit the markup slice**

```powershell
rtk git add src/OpenRussiaRouter.WebApp/Pages/Index.cshtml tests/OpenRussiaRouter.WebApp.Tests/SeoHeadTests.cs
rtk git commit -m "feat: render flagship carousel track"
```

Before committing, stage only these files and inspect `rtk git diff --cached` because both currently contain user changes.

### Task 2: Implement seamless autoplay and manual drag

**Files:**
- Create: `src/OpenRussiaRouter.WebApp/wwwroot/js/flagship-carousel.js`
- Create: `tests/OpenRussiaRouter.E2ETests/FlagshipCarouselTests.cs`

**Interfaces:**
- Consumes: `[data-flagship-carousel]` and its direct `[data-flagship-track]` child.
- Produces: `window.FlagshipCarousel.initialize(viewport)` for idempotent startup; adds visual clone groups marked `[data-flagship-clone]`, `aria-hidden="true"`, and `tabindex="-1"` links.

- [ ] **Step 1: Add the browser behavior test**

Create `FlagshipCarouselTests.cs` with an opt-in test following the existing `TestConfiguration` convention:

```csharp
namespace OpenRussiaRouter.E2ETests;

public sealed class FlagshipCarouselTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public FlagshipCarouselTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Carousel_AutoplaysPausesAndKeepsCardLinksClickableAsync()
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
        await page.GotoAsync($"{TestConfiguration.BaseUrl}/ru");

        var carousel = page.Locator("[data-flagship-carousel]");
        if (await carousel.CountAsync() == 0)
        {
            return;
        }

        var before = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        await page.WaitForTimeoutAsync(500);
        var moving = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        Assert.True(moving > before);

        await carousel.HoverAsync();
        var pausedAt = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        await page.WaitForTimeoutAsync(300);
        var stillPaused = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        Assert.InRange(Math.Abs(stillPaused - pausedAt), 0, 0.5);

        await page.Mouse.MoveAsync(0, 0);
        var resumedAt = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        await page.WaitForTimeoutAsync(300);
        var resumed = await carousel.EvaluateAsync<double>("element => element.scrollLeft");
        Assert.True(resumed > resumedAt);

        var original = carousel.Locator("[data-flagship-cycle]:not([data-flagship-clone]) > .flagship-card:not([data-flagship-clone])").First;
        var href = await original.GetAttributeAsync("href");
        Assert.False(string.IsNullOrWhiteSpace(href));
        Assert.True(await carousel.Locator("[data-flagship-clone][aria-hidden='true'] a[tabindex='-1']").CountAsync() > 0);

        var box = await original.BoundingBoxAsync();
        Assert.NotNull(box);
        var initialUrl = page.Url;
        await page.Mouse.MoveAsync(box!.X + 40, box.Y + 40);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 100, box.Y + 40);
        await page.Mouse.UpAsync();
        Assert.Equal(initialUrl, page.Url);
    }
}
```

Add a second test using `ReducedMotion.Reduce`; after 500 ms its `scrollLeft` must remain within
0.5 px of the initial value while a direct click on an original `.flagship-card` must navigate to
the card's localized `href`.

- [ ] **Step 2: Run the E2E test to establish its pre-implementation state**

Run:

```powershell
rtk dotnet test tests/OpenRussiaRouter.E2ETests --no-restore -nr:false -m:1 --filter "FullyQualifiedName~FlagshipCarouselTests"
```

Expected when E2E is configured: FAIL because the script and clones do not exist. When configuration is absent, the test returns without browser work, matching the existing project convention.

- [ ] **Step 3: Implement the dependency-free controller**

Create `flagship-carousel.js` with these concrete responsibilities:

```javascript
(() => {
    const SPEED_PX_PER_SECOND = 20;
    const DRAG_THRESHOLD_PX = 6;

    function initialize(viewport) {
        if (!viewport || viewport.dataset.flagshipInitialized === "true") return;
        const track = viewport.querySelector("[data-flagship-track]");
        const mainCycle = track?.querySelector("[data-flagship-cycle]");
        const originals = mainCycle ? Array.from(mainCycle.children) : [];
        if (!track || !mainCycle || originals.length === 0) return;

        viewport.dataset.flagshipInitialized = "true";
        let cycleWidth = 0;
        let frameId = 0;
        let previousTime = 0;
        let hovered = false;
        let pointerActive = false;
        let dragged = false;
        let startX = 0;
        let startScrollLeft = 0;
        const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

        const rebuild = () => {
            track.querySelectorAll(":scope > [data-flagship-clone]").forEach(element => element.remove());
            mainCycle.querySelectorAll(":scope > [data-flagship-clone]").forEach(element => element.remove());
            while (mainCycle.scrollWidth < viewport.clientWidth + 330) {
                for (const original of originals) {
                    const supplemental = original.cloneNode(true);
                    supplemental.dataset.flagshipClone = "";
                    supplemental.setAttribute("aria-hidden", "true");
                    supplemental.tabIndex = -1;
                    mainCycle.append(supplemental);
                }
            }
            const before = mainCycle.cloneNode(true);
            const after = mainCycle.cloneNode(true);
            for (const clone of [before, after]) {
                clone.dataset.flagshipClone = "";
                clone.setAttribute("aria-hidden", "true");
                clone.querySelectorAll("a").forEach(link => link.tabIndex = -1);
            }
            track.replaceChildren(before, mainCycle, after);
            const trackGap = Number.parseFloat(getComputedStyle(track).columnGap) || 0;
            cycleWidth = mainCycle.getBoundingClientRect().width + trackGap;
            viewport.scrollLeft = cycleWidth;
        };

        const normalize = () => {
            if (cycleWidth <= 0) return;
            if (viewport.scrollLeft >= cycleWidth * 2) viewport.scrollLeft -= cycleWidth;
            if (viewport.scrollLeft <= 0) viewport.scrollLeft += cycleWidth;
        };

        const tick = timestamp => {
            if (previousTime === 0) previousTime = timestamp;
            const elapsed = Math.min(timestamp - previousTime, 50);
            previousTime = timestamp;
            if (!hovered && !pointerActive && !document.hidden && !reducedMotion.matches) {
                viewport.scrollLeft += SPEED_PX_PER_SECOND * elapsed / 1000;
                normalize();
            }
            frameId = window.requestAnimationFrame(tick);
        };

        viewport.addEventListener("pointerenter", () => { hovered = true; });
        viewport.addEventListener("pointerleave", () => { hovered = false; });
        viewport.addEventListener("pointerdown", event => {
            pointerActive = true;
            dragged = false;
            startX = event.clientX;
            startScrollLeft = viewport.scrollLeft;
            viewport.classList.add("is-dragging");
            viewport.setPointerCapture(event.pointerId);
        });
        viewport.addEventListener("pointermove", event => {
            if (!pointerActive) return;
            const delta = event.clientX - startX;
            if (Math.abs(delta) >= DRAG_THRESHOLD_PX) dragged = true;
            if (dragged) {
                event.preventDefault();
                viewport.scrollLeft = startScrollLeft - delta;
                normalize();
            }
        });
        const finishPointer = event => {
            pointerActive = false;
            viewport.classList.remove("is-dragging");
            if (viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
        };
        viewport.addEventListener("pointerup", finishPointer);
        viewport.addEventListener("pointercancel", finishPointer);
        viewport.addEventListener("click", event => {
            if (!dragged) return;
            event.preventDefault();
            event.stopPropagation();
            dragged = false;
        }, true);
        viewport.addEventListener("scroll", normalize, { passive: true });

        const resizeObserver = new ResizeObserver(rebuild);
        resizeObserver.observe(viewport);
        rebuild();
        const startAnimation = () => {
            if (frameId !== 0) return;
            previousTime = 0;
            frameId = window.requestAnimationFrame(tick);
        };
        const stopAnimation = () => {
            window.cancelAnimationFrame(frameId);
            frameId = 0;
        };
        startAnimation();
        window.addEventListener("pagehide", stopAnimation);
        window.addEventListener("pageshow", startAnimation);
    }

    window.FlagshipCarousel = { initialize };
    document.querySelectorAll("[data-flagship-carousel]").forEach(initialize);
})();
```

The server-rendered `[data-flagship-cycle]` is the authoritative cycle. Supplemental cards inside it and the two buffer cycles are explicitly non-semantic.

- [ ] **Step 4: Run the WebApp suite and, when configured, the E2E test**

```powershell
rtk dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-restore -nr:false -m:1
rtk dotnet test tests/OpenRussiaRouter.E2ETests --no-restore -nr:false -m:1 --filter "FullyQualifiedName~FlagshipCarouselTests"
```

Expected: WebApp tests PASS with zero warnings; configured E2E verifies motion, hover pause, clones, and the original model link.

- [ ] **Step 5: Commit the behavior slice**

```powershell
rtk git add src/OpenRussiaRouter.WebApp/wwwroot/js/flagship-carousel.js src/OpenRussiaRouter.WebApp/Pages/Index.cshtml tests/OpenRussiaRouter.E2ETests/FlagshipCarouselTests.cs
rtk git commit -m "feat: animate flagship carousel"
```

Inspect `rtk git diff --cached` first so unrelated user changes in `Index.cshtml` are not staged accidentally.

### Task 3: Document and verify the complete feature

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/CLAUDE.md`

**Interfaces:**
- Consumes: the final carousel markup and `window.FlagshipCarousel.initialize` behavior.
- Produces: the maintained WebApp invariant describing autoplay, pause, clones, reduced motion, and price order.

- [ ] **Step 1: Update service documentation**

Extend the existing “Флагманская витрина на главной” section with:

```markdown
Карточки выводятся одной бесконечной горизонтальной лентой: автодвижение около 20 px/с
приостанавливается на hover и во время pointer/touch-взаимодействия. Стрелок, точек и специальных
keyboard-команд нет; доступны drag, touch swipe и trackpad. Семантическим остаётся один набор
ссылок, визуальные циклические копии скрыты через `aria-hidden` и исключены из tab-порядка.
`prefers-reduced-motion` отключает autoplay. В сравнении сначала идёт зелёная цена платформы,
затем зачёркнутая цена OpenRouter.
```

- [ ] **Step 2: Run formatting and shortcut checks**

```powershell
rtk git diff --check
rtk rg -n "GetAwaiter\(|#pragma warning disable|Skip\s*=" src/OpenRussiaRouter.WebApp tests/OpenRussiaRouter.WebApp.Tests tests/OpenRussiaRouter.E2ETests
```

Expected: no new whitespace errors, blocking waits, suppressed warnings, or skipped tests in changed code.

- [ ] **Step 3: Run required validation**

```powershell
rtk dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-restore -nr:false -m:1
rtk dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: all tests pass; 19 projects build with 0 errors and 0 warnings.

- [ ] **Step 4: Commit documentation separately**

```powershell
rtk git add src/OpenRussiaRouter.WebApp/CLAUDE.md
rtk git commit -m "docs: describe flagship carousel behavior"
```

Before committing, verify that no `.superpowers/brainstorm` mockups or unrelated user files are staged.
