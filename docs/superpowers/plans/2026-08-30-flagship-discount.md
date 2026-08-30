# Flagship discount implementation plan

**Goal:** Show one large green rounded discount to the right of each model name, red struck-through OpenRouter prices, and no flagship badge on cards.

**Architecture:** A computed nullable percentage on `IndexModel.FlagshipModelCard` uses the existing manual reference prices. Razor renders it with localized “до” / “up to” and a calculation tooltip. No Billing contracts, persisted data or carousel behavior change.

**Tech stack:** C#, Razor Pages, CSS, RESX, xUnit and FluentAssertions.

## Constraints and calculation

- Compare input, output and cache separately: `(reference - ours) / reference * 100`.
- Display the maximum positive saving with “up to”, not an assumed workload average.
- Ignore zero/negative references and negative platform prices. Equal or higher platform prices do not count as savings.
- Round to a whole number with `MidpointRounding.AwayFromZero`; hide a result rounded to zero.
- Keep the whole card a link, and preserve the existing manual-price disclaimer and carousel behavior.
- Preserve unrelated dirty changes. Do not commit or deploy.

## Execution

- [x] Extend `tests/OpenRussiaRouter.WebApp.Tests/SeoHeadTests.cs` with RU/EN assertions for one `−67%`, its qualifier, absence of the old badge, and danger-colored struck prices. Run the focused rendering test and verify failure.
- [x] Add data-driven tests to `tests/OpenRussiaRouter.WebApp.Tests/IndexPageTests.cs` for each winning token category, midpoint rounding, free usage, equal/higher prices, zero references and tiny savings.
- [x] Implement `public int? MaxDiscountPercent` on the existing card record in `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml.cs`.
- [x] Modify `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml`: a two-column name/discount heading, wrapping model name, non-wrapping green 34px percentage, red `<del>` prices using `var(--color-danger)` and `currentColor`; remove the old badge and its styles.
- [x] Replace `Home_FlagshipBadge` with `Home_DiscountUpTo` and `Home_DiscountTooltip` in both `src/OpenRussiaRouter.WebApp/Resources/Pages/Index.{ru,en}.resx` files.
- [x] Update `src/OpenRussiaRouter.WebApp/CLAUDE.md` with the calculation and presentation rules.
- [x] Run `rtk dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-restore -nr:false -m:1` and `rtk dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`. Both must pass.
- [x] Review responsive CSS and the diff for unwanted changes and test suppressions.

## Verification

- New rendering assertions failed before implementation in both locales and passed afterward.
- 533 WebApp tests passed, no warnings.
- Solution build: 19 projects, no errors or warnings.
- Responsive CSS reserves an auto-width discount column and allows long model names to wrap in the remaining space; no fixed card height or new focusable elements.
- Live visual verification unavailable: `http://localhost:51574/` refused the browser connection. No screenshot-based layout claim is made.
