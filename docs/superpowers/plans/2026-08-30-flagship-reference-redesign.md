# Flagship reference redesign implementation plan

**Goal:** Adapt the flagship carousel cards to the user-provided image and CSS: dark glass surface, jade accents, ID chip and paired current/reference prices.

**Architecture:** Keep `IndexModel`, links and carousel JavaScript unchanged. Replace the three-column comparison with a semantic two-column table inside each card. Prices use existing invariant formatting and localized hover labels. Scope all supplied color variables to the showcase rather than changing the entire site.

**Tech Stack:** Razor Pages, CSS, RESX, xUnit, existing carousel regression tests.

## Constraints

- Preserve flagship selection, discount calculation, canonical localized links, autoplay, hover pause, inertia and reduced-motion behavior.
- Card width follows the supplied `clamp(280px, 28vw, 330px)`; mobile cards stay inside the viewport. Use a 400px minimum height, 24px padding, 20px gap and soft borders/shadows.
- Current and reference price share a cell. Long prices wrap as whole values when possible; do not truncate monetary amounts.
- OpenRouter is no longer a visible column heading; keep its identity in the reference-price tooltip.
- Remove the bottom arrow; keep the whole card a link.
- No database changes, dependency additions, commits or deployment. Preserve unrelated `docs/audits/`.

## Steps

- [x] Update `tests/OpenRussiaRouter.WebApp.Tests/SeoHeadTests.cs` to require a table, localized column/row headers, three current/reference pairs in order, source tooltips and no bottom arrow. Run the focused test before implementation and confirm it fails.
- [x] Update `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml` markup and CSS. Apply the supplied glass background, jade `#34d399`, muted reference `#9ca3af`, red strike `#f87171`, subtle table dividers, ID chip and balanced title/discount sizing.
- [x] Add `Home_PriceOperation` and `Home_PriceColumn` to both `src/OpenRussiaRouter.WebApp/Resources/Pages/Index.{ru,en}.resx`; keep existing source labels for tooltips.
- [x] Update the showcase presentation contract in `src/OpenRussiaRouter.WebApp/CLAUDE.md`.
- [x] Verify RU/EN desktop and narrow mobile layouts in an isolated preview using the actual card template and CSS; check long names and prices.
- [x] Run `rtk dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-restore -nr:false -m:1`.
- [x] Run `rtk dotnet test tests/OpenRussiaRouter.E2ETests --no-restore -nr:false -m:1 --filter FullyQualifiedName~FlagshipCarouselFocusTests --environment CI=true --environment ORR_E2E_USER_EMAIL=offline-fixture --environment ORR_E2E_USER_PASSWORD=unused`.
- [x] Run `rtk dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1` and `rtk git diff --check`.

## Results

- Rendering assertions failed on the previous markup, then passed for RU and EN.
- 560 WebApp tests and 25 isolated Chromium carousel regressions passed without skips or warnings.
- Solution build: 19 projects, zero errors and warnings; diff whitespace check clean.
- Inspected a browser preview made from the production card template/CSS and sample data, not a live Billing-backed page. At 320px, all three sample cards had equal heights, no name/discount overlaps or clipped table cells, including six-decimal prices and an unbroken long model name. The temporary server and script were removed after verification.
- Slopwatch is not installed; changed tests were reviewed manually for suppressions and skipped checks.
