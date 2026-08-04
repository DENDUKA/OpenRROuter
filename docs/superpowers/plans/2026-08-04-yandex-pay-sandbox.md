# Yandex Pay Sandbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an administrator-only, card-only Yandex Pay sandbox top-up method that credits balances through the existing payment pipeline.

**Architecture:** The Yandex adapter uses the existing `IPaymentProvider` abstraction with RUB settlement. Dedicated endpoints create SDK checkouts and validate Yandex ES256 webhook JWTs; actual confirmation continues through `PaymentWebhookProcessor` and BillingService.

**Tech Stack:** ASP.NET Core 8 Razor Pages, HttpClientFactory, Microsoft IdentityModel JWT/JWK validation, xUnit, FluentAssertions.

## Global Constraints

- Rollypay remains the active primary provider and must not regress.
- Sandbox merchant id is `b82d0fc1-48e0-4713-8ba5-3dfb9a4e04b3`; no API secret is committed.
- Yandex Pay supports only `CARD`; no Split is sent or displayed.
- The Yandex Pay button and its checkout endpoint require the `Admin` role.
- A sandbox `CAPTURED` event credits BillingService for full-cycle testing.
- All production payment confirmation stays in BillingService.
- Build and test with `-nr:false -m:1`; warnings are errors.

---

### Task 1: Add Yandex Pay Adapter and JWT Validation

**Files:**
- Create: `src/OpenRussiaRouter.WebApp/Services/Payments/YandexPay/YandexPayDtos.cs`
- Create: `src/OpenRussiaRouter.WebApp/Services/Payments/YandexPay/YandexPayClient.cs`
- Create: `src/OpenRussiaRouter.WebApp/Services/Payments/YandexPay/YandexPayJwkProvider.cs`
- Create: `src/OpenRussiaRouter.WebApp/Services/Payments/YandexPay/YandexPayWebhookVerifier.cs`
- Create: `src/OpenRussiaRouter.WebApp/Services/Payments/YandexPay/YandexPayPaymentProvider.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Services/Payments/PaymentSettings.cs`
- Modify: `Directory.Packages.props`
- Modify: `src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/YandexPayPaymentProviderTests.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/YandexPayWebhookVerifierTests.cs`

- [ ] Implement sandbox Merchant API creation/query and JWK retrieval.
- [ ] Implement ES256/JWK JWT validation with merchant id check and bounded cache refresh.
- [ ] Implement `IPaymentProvider` mapping Yandex order status to canonical payment status.
- [ ] Add focused unit tests for requests, mappings, valid JWT, expired JWT, bad signature, and wrong merchant id.

### Task 2: Add Server Endpoints

**Files:**
- Create: `src/OpenRussiaRouter.WebApp/Api/YandexPayCheckoutEndpoint.cs`
- Create: `src/OpenRussiaRouter.WebApp/Api/YandexPayWebhookEndpoint.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/YandexPayCheckoutEndpointTests.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/YandexPayWebhookEndpointTests.cs`

- [ ] Add an antiforgery-protected, authenticated `Admin` checkout endpoint that calls `PaymentCheckoutService.CreateAsync` with `providerName: "yandexpay"`.
- [ ] Add `POST /v1/webhook`, JWT verify-before-parse, canonical event mapping, and Yandex response bodies.
- [ ] Register named HTTP clients, provider, JWK cache, and endpoints.
- [ ] Test admin access, webhook confirmation, failure handling, and duplicate delivery.

### Task 3: Add Admin SDK UI and Deployment Configuration

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Program.cs`
- Modify: `src/OpenRussiaRouter.WebApp/appsettings.json`
- Modify: `deploy/compose/.env.example`
- Modify: `deploy/compose/compose.prod.yml`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/LegalConsentTests.cs`

- [ ] Render the official SDK and test button only for enabled sandbox configuration and the `Admin` role.
- [ ] Reuse top-up amount, currency, legal consent, and same-origin checkout endpoint from the existing form.
- [ ] Add narrowly scoped CSP directives for `https://pay.yandex.ru`.
- [ ] Add disabled-by-default sandbox settings and compose environment variables.
- [ ] Test that non-admin rendering and calls cannot access Yandex Pay.

### Task 4: Document and Verify

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/CLAUDE.md`
- Modify: `CLAUDE.md`

- [ ] Document provider routing, `/v1/webhook`, JWT/JWK trust, and admin-only sandbox policy.
- [ ] Run `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`.
- [ ] Run `dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1`.
