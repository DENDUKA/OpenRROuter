# Yandex Pay Sandbox Design

## Goal

Add Yandex Pay sandbox as an alternative top-up method while preserving Rollypay as the primary payment method. The sandbox method is available only to authenticated administrators and its successful `CAPTURED` payments credit the account through the existing BillingService flow.

## Scope

- Sandbox only, merchant id `b82d0fc1-48e0-4713-8ba5-3dfb9a4e04b3`.
- Card-only checkout; no Split, subscriptions, refunds UI, or production Yandex Pay API.
- Web SDK button on `/workspace/credits` only for the `Admin` role.
- Merchant API order creation and reconciliation via the sandbox API.
- Dedicated `POST /v1/webhook` receiving ES256 JWT notifications and validating sandbox JWKs.

## Architecture

`YandexPayPaymentProvider` implements the existing `IPaymentProvider` seam and fixes settlement currency to RUB. `PaymentCheckoutService` continues to create the Billing payment first, convert display amount through Billing FX quotes, create the Yandex order using `payment_id` as `orderId`, and attach the provider reference.

The browser SDK requests a same-origin admin-protected checkout endpoint. It returns the Yandex `paymentUrl` to `YaPay.createSession`. The redirect URL never credits balance. A verified webhook or reconciliation query gets the authoritative order total from Yandex Pay and passes its normalized event to `PaymentWebhookProcessor`, which is the only WebApp path that invokes Billing confirmation.

## Security

- The SDK button is a UX restriction; the checkout endpoint independently requires authentication and `Admin` role.
- Webhook payload is read with the existing 64 KiB limit and treated as a compact JWT, not JSON.
- Signature verification requires `alg=ES256`, valid lifetime claims, a JWK selected by `kid`, and a payload `merchantId` equal to configured merchant id.
- JWKs are cached for a bounded duration and refreshed once when the `kid` is absent.
- The webhook endpoint trusts no source IP range.
- CSP permits Yandex Pay only for scripts, frames, images, and connections. Existing `frame-ancestors 'none'` stays in place.

## Configuration

`Payments:YandexPay` includes `Enabled`, `Sandbox`, `MerchantId`, redirect URLs, order TTL, and JWK cache duration. It is disabled by default. The production compose configuration reads only environment variables. Sandbox authorization uses the merchant id as the API key according to the Yandex Pay documentation.

## State Mapping

- `CAPTURED` fetches the Yandex order and becomes `paid` with its amount and currency.
- `FAILED` becomes `canceled`.
- `REFUNDED` becomes `refunded`.
- `PENDING`, `AUTHORIZED`, `CONFIRMED`, operation updates, and unknown events are successful no-ops.
- A stable event id derived from order id, event type, status, and event time makes redelivery idempotent in Billing.

## Verification

Tests cover merchant requests, JWK/JWT validation, webhook state handling and deduplication, role protection of checkout, absence of the SDK for non-admin users, and all existing payment tests. The solution is built and tested serially with the repository-required flags.
