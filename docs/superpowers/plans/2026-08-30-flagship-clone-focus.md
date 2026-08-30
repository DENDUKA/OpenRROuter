# Flagship clone focus regression implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Execution stays in the current task; that optional skill is not installed.

**Goal:** Prevent focus inside aria-hidden carousel clones while preserving native model links and existing motion.

**Architecture:** Delegate clone interaction handling at the viewport. Resolve the corresponding original card by a stable ordinal copied during cloning (href may repeat for catalog fallbacks), prevent pointer-induced focus on the clone and focus the original with preventScroll. Redirect programmatic clone focus through the same resolver.

**Tech Stack:** Vanilla JavaScript, xUnit, Microsoft.Playwright, Razor Pages.

## Global constraints

- Preserve aria-hidden/tabindex clone semantics, hrefs, normal/modified clicks and drag suppression.
- Do not change autoplay, hover, inertia, locale routing, CSS or backend data.
- Use rtk and --no-restore -nr:false -m:1 for .NET checks.
- No implementation commit or deployment.

### Task 1: Regression and targeted fix

**Files:**
- Create: `tests/OpenRussiaRouter.E2ETests/FlagshipCarouselFocusTests.cs`
- Modify: `src/OpenRussiaRouter.WebApp/wwwroot/js/flagship-carousel.js`
- Modify: `src/OpenRussiaRouter.WebApp/CLAUDE.md`

**Interfaces:** Consume the existing data-flagship-carousel/track/cycle/clone DOM contract and production JS asset. Produce no new public API.

- [x] Add isolated Playwright fixtures for supplemental card clones and whole-cycle clones, in both locales. Assert after pointerdown and direct focus that `document.activeElement.closest('[aria-hidden="true"]') === null`, and that focus belongs to the original model link without changing scrollLeft.
- [x] Verify the new test fails on the original JS, using the existing opt-in browser runner with test-only credentials; the fixture never logs in or accesses backend services. All four pointer cases failed on hidden focus.
- [x] Resolve a clone's `.flagship-card` by its copied `data-flagship-original-index`. On pointerdown/mousedown prevent default focus and call `original.focus({ preventScroll: true })`. Add a focusin fallback with the same mapping. Keep native click handling unchanged.
- [x] Verify ordinary click/touch and modified click navigation, drag suppression, original-card focus, idempotent initialization and rebuilt clones. Read Chromium accessibility diagnostics and reject aria-hidden/focus warnings. All 17 browser cases passed, including duplicate href mapping.
- [x] Update the WebApp carousel invariant: tabindex=-1 alone does not prevent pointer/programmatic focus; cloned links transfer focus to the semantic original without scrolling.
- [x] Run focused browser tests, WebApp tests, solution build, node syntax and git diff checks. Inspect changes for disabled tests, warning suppression and swallowed errors (Slopwatch is not installed). WebApp: 520 passed; solution: 19 projects, zero errors/warnings; syntax/diff checks clean.

### Follow-up: autoplay does not move without hover

- [x] Reproduce actual displacement with the production JS, Chromium scrollLeft and Playwright's controlled animation clock at device scale factors 1, 1.25 and 2. No hover, no reduced-motion: `RunForAsync(1000)` must move approximately 20 CSS pixels. Before the fix all three cases moved 0 pixels and failed; afterward they pass.
- [x] If subpixel writes are rounded away, keep the unwritten fractional delta between frames for both autoplay and inertia. Clear it on rebuild and interaction cancellation; do not overwrite native manual scroll with an old absolute position.
- [x] Verify real hover pauses, leaving resumes after 800 ms, focus alone does not pause, external scroll/loop wrapping retain the correct position, and reduced-motion still disables automatic movement. All 25 isolated browser cases passed, including real RAF and trusted touch input.
- [x] Run all isolated browser regressions with the existing offline test parameters, WebApp tests and the solution build. Document the subpixel invariant. Browser: 25 passed (not skipped); WebApp: 520 passed; solution: 19 projects with zero errors/warnings. Node syntax and diff checks passed.
