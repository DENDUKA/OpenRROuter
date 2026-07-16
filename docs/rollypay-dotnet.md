# Rollypay → .NET интеграция (мануал для Claude)

**Model**: Claude Opus 4.7 Extra High Thinking

**Аудитория**: будущая сессия Claude, которая подключает Rollypay в новый .NET-проект.
**Источник истины**: https://docs.rollypay.io (но он маленький и не закрывает грабли — этот документ их фиксирует).
**Self-contained**: весь код, который надо написать, лежит в §3 этого документа. Никаких внешних файлов читать не нужно — копируй блоки прямо отсюда, меняй namespace.

---

## 1. Что Rollypay делает и чего не делает

**Делает**: принимает $ через СБП (QR), автоматически конвертирует в USDT и зачисляет на ваш Rollypay-баланс. Подтверждение оплаты приходит вебхуком за секунды.

**Не делает**:
- Не принимает крипту от пользователей (USDT — только валюта расчётов с вами).
- Нет официального .NET SDK (есть только Python, Node/PHP по запросу). Пишем тонкий `HttpClient` руками.

> **Обновлено 2026-07-16**: sandbox **есть** — тестовый режим на той же кассе (`"test": true`), см. §2.1.
> Ранее этот документ утверждал обратное; это было неверно и заставляло гонять смоук реальными деньгами.

**Когда выбирать Rollypay**: нужно автоматическое СБП для физлица/мерчанта, готовы платить ~3% комиссии Rollypay за избавление от ручного админ-подтверждения.

**Когда НЕ выбирать**: для приёма крипты (USDT/USDC/TON) — там нужен прямой on-chain трекинг (TronGrid/Etherscan/Toncenter) или BTCPay Server. Rollypay не покрывает.

---

## 2. Контракт API (краткая выжимка)

### Создание платежа

```http
POST https://rollypay.io/api/v1/payments
Headers:
  X-API-Key: <значение из UI кассы, вкладка «Интеграция»>     ; постоянный, ключ кассы
  X-Nonce: <uuid>                                              ; per-request, anti-replay 10 мин
  Content-Type: application/json
Body:
{
  "amount": "1500.00",             // строка, не number
  "payment_currency": "RUB",       // других пока нет
  "payment_method": "sbp",         // или "card", "crypto" если включены в кассе
  "order_id": "your-internal-id",  // ваш id, по нему ищете платёж в webhook
  "description": "Заказ #12345",
  "success_redirect_url": "https://t.me/your_bot/",
  "fail_redirect_url": "https://t.me/your_bot/",
  "metadata": { "any": "key-value pairs returned in callbacks" }
}

Response 200:
{
  "payment_id": "pay_a1b2c3d4-…",  // их id, идёт в webhook
  "order_id": "your-internal-id",
  "status": "created",
  "pay_url": "https://pay.rollypay.io/pay/tok_…",
  "expires_at": "2026-05-19T12:30:00Z",
  ...
}
```

**Документация врёт про префикс `rpk_live_…` у API Key** — это просто пример из docs. Реальный ключ может быть любого формата (видели base64-подобные строки без префикса). Не валидируйте по префиксу.

### Получить платёж

```http
GET https://rollypay.io/api/v1/payments/{payment_id}
Headers: X-API-Key + X-Nonce
```

### 2.1. Тестовый режим (sandbox)

**Отдельного sandbox-хоста и отдельных ключей нет** — тестовый платёж создаётся на **той же кассе**, тем же `X-API-Key`. Включается одним из двух эквивалентных способов:

- поле `"test": true` в теле `POST /api/v1/payments`;
- заголовок `X-Test-Mode: true`.

Что отличается от боевого платежа:

| | Тестовый платёж |
| --- | --- |
| Способ оплаты | Всегда заглушка СБП, независимо от `payment_method` |
| Деньги / баланс | Не двигаются, баланс не меняется |
| Лимиты кассы | Не проверяются и не расходуются |
| Кэшбек RollyCoin | Не начисляется |
| Отчёты и метрики | Тестовые платежи исключены |
| Возвраты | Недоступны |

Оплата симулируется вручную: вместо QR на pay-странице показывается баннер с кнопками, под капотом — `POST /public/payments/{token}/simulate` с `"outcome": "paid" | "failed"`. Статусы идут по обычному пути `created → processing → paid/canceled`.

**Вебхуки приходят** — с `"test": true` в теле и заголовком `X-Test-Mode: true`. **Подпись считается точно так же**, как для боевых, поэтому верификатор один и тот же, никаких развилок в коде.

В списке `GET /api/v1/payments` тестовые платежи по умолчанию **скрыты**: `?environment=sandbox` — только тестовые, `?environment=all` — все, без параметра — только боевые.

> ⚠️ **Главная ловушка**: тестовый платёж не стоит ничего, но ваш биллинг зачислит по нему **реальный баланс** — вебхук-то настоящий и подписан валидно. Тестовый режим обязан быть выключен в проде и защищён startup-гвардом (в ORR — `PaymentStartupGuard`).

### В UI кассы три значения

| Что выдают         | Куда идёт в коде                                                                                          |
| ------------------ | --------------------------------------------------------------------------------------------------------- |
| **API Key**        | `X-API-Key` header при создании платежа                                                                   |
| **Signing Secret** | HMAC ключ для проверки входящих webhook'ов (никогда не уходит наружу)                                     |
| **Terminal ID**    | **в коде не используется** — API Key уже привязан к кассе на их стороне. Сохрани в заметках для саппорта. |

### Статусы

```
created → processing → paid       (счастливый путь)
                    ↘ expired     (TTL вышел)
                    ↘ canceled    (пользователь нажал «отмена» или мерчант отменил)
paid → chargeback                 (банк-эмитент откатил)
paid → refunded                   (мерчант сделал возврат)
```

### Webhook

- URL задаётся в UI кассы Rollypay (вкладка «Настройки» → «Маршруты»), либо через `PUT /api/v1/terminals/{terminalID}`.
- Headers: `X-Signature` (hex HMAC-SHA256), `X-Timestamp` (unix seconds).
- Алгоритм подписи: `HMAC-SHA256(signing_secret, X-Timestamp + "." + raw_body)`. **Тело сырое, не парсенное JSON**.
- Идемпотентность: по `(payment_id, event_type)`. Поле idempotency-key они НЕ дают — храните `payment_id` в своей БД и проверяйте на дубль.
- Ретраи: до 8 раз экспоненциально (30 с → 64 мин). Нужно вернуть 2xx за 10 с.
- События: `payment.created`, `payment.paid`, `payment.canceled`, `payment.chargeback`, `payment.refunded`, `payout.completed`, `refund_request.completed`.

### Ошибки

- `400` — bad request: не ретраить, фиксить тело
- `401` — invalid key или **переиспользованный nonce** (легко выстрелить, если nonce генерится из таймера/seed — используйте `Guid.NewGuid()`)
- `429` — rate-limited: backoff
- `5xx` / `503` — backoff
- Тело ошибки: `{ "error": "human-readable description" }`

---

## 3. Файлы для создания (полный код, копируй и адаптируй namespace)

Все примеры используют `namespace YourApp.Services.Rollypay` — замени на свой. Конфиг-класс `PaymentSettings` упоминается ниже — если у проекта своего нет, сделай standalone `RollypaySettings` и инжекть его напрямую через `IOptionsMonitor<RollypaySettings>`.

### 3.1. `RollypaySettings.cs` (конфиг)

```csharp
namespace YourApp.Configuration;

public class RollypaySettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://rollypay.io";
    public string ApiKey { get; set; } = string.Empty;        // из UI кассы → Интеграция → API Key
    public string SigningSecret { get; set; } = string.Empty; // из UI кассы → Интеграция → Signing Secret
    public int PaymentTtlMinutes { get; set; } = 30;          // синхронизировать с TTL чека в UI кассы
    public bool TestMode { get; set; }                        // "test": true — та же касса, без реальных денег (§2.1)
}
```

Если в проекте уже есть `PaymentSettings`, лучше встроить как nested:
```csharp
public class PaymentSettings
{
    // ... your existing fields ...
    public RollypaySettings Rollypay { get; set; } = new();
}
```
Тогда в коде ниже все обращения `_settings.CurrentValue.Rollypay.…` сохраняются, а DI инжектит `IOptionsMonitor<PaymentSettings>`.

### 3.2. `IRollypayClient.cs`

```csharp
namespace YourApp.Services.Rollypay;

public interface IRollypayClient
{
    Task<RollypayPayment> CreatePaymentAsync(CreateRollypayPaymentRequest request, CancellationToken ct = default);
    Task<RollypayPayment> GetPaymentAsync(string paymentId, CancellationToken ct = default);
}
```

### 3.3. `RollypayDtos.cs`

```csharp
using System.Text.Json.Serialization;

namespace YourApp.Services.Rollypay;

public sealed class CreateRollypayPaymentRequest
{
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("payment_currency")]
    public string PaymentCurrency { get; set; } = "RUB";

    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; set; } = "sbp";

    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("success_redirect_url")]
    public string? SuccessRedirectUrl { get; set; }

    [JsonPropertyName("fail_redirect_url")]
    public string? FailRedirectUrl { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    // null → поле не уходит в тело (нужен JsonIgnoreCondition.WhenWritingNull), см. §2.1
    [JsonPropertyName("test")]
    public bool? Test { get; set; }
}

public sealed class RollypayPayment
{
    [JsonPropertyName("payment_id")]
    public string PaymentId { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("pay_url")]
    public string? PayUrl { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("payment_currency")]
    public string? PaymentCurrency { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("paid_at")]
    public DateTime? PaidAt { get; set; }
}
```

### 3.4. `RollypayApiException.cs`

```csharp
using System.Net;

namespace YourApp.Services.Rollypay;

public sealed class RollypayApiException : Exception
{
    public RollypayApiException(HttpStatusCode statusCode, string body, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Body = body;
    }

    public HttpStatusCode StatusCode { get; }
    public string Body { get; }
}

public sealed class RollypayUnavailableException : Exception
{
    public RollypayUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
```

Разделение: `RollypayApiException` для **детерминированных** 4xx (не ретраить — фиксить запрос), `RollypayUnavailableException` для **транзиентных** 5xx/429/таймаутов (можно ретраить или показать юзеру «временно недоступно»).

### 3.5. `RollypayClient.cs`

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using YourApp.Configuration;

namespace YourApp.Services.Rollypay;

public sealed class RollypayClient : IRollypayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<RollypaySettings> _settings;

    public RollypayClient(HttpClient httpClient, IOptionsMonitor<RollypaySettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<RollypayPayment> CreatePaymentAsync(CreateRollypayPaymentRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        ApplyAuthHeaders(httpRequest);

        return await SendAsync<RollypayPayment>(httpRequest, ct)
            ?? throw new RollypayApiException(HttpStatusCode.OK, string.Empty, "Empty response body from Rollypay create payment");
    }

    public async Task<RollypayPayment> GetPaymentAsync(string paymentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
            throw new ArgumentException("paymentId must not be empty", nameof(paymentId));

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/payments/{Uri.EscapeDataString(paymentId)}");
        ApplyAuthHeaders(httpRequest);

        return await SendAsync<RollypayPayment>(httpRequest, ct)
            ?? throw new RollypayApiException(HttpStatusCode.OK, string.Empty, "Empty response body from Rollypay get payment");
    }

    private void ApplyAuthHeaders(HttpRequestMessage request)
    {
        var settings = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Rollypay API key is not configured");

        request.Headers.TryAddWithoutValidation("X-API-Key", settings.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Nonce", Guid.NewGuid().ToString("D"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken ct) where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex)
        {
            throw new RollypayUnavailableException("Rollypay request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new RollypayUnavailableException("Rollypay request failed.", ex);
        }

        using var _ = response;
        var body = await response.Content.ReadAsStringAsync(ct);

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new RollypayUnavailableException(
                $"Rollypay returned {(int)response.StatusCode}: {Truncate(body, 200)}");

        if (!response.IsSuccessStatusCode)
            throw new RollypayApiException(response.StatusCode, body,
                $"Rollypay rejected request: {(int)response.StatusCode} {response.ReasonPhrase}");

        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new RollypayApiException(response.StatusCode, body, "Failed to parse Rollypay response", ex);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
```

**Замечание про `IOptionsMonitor<RollypaySettings>`**: если используешь nested-вариант (§3.1 второй фрагмент), замени на `IOptionsMonitor<PaymentSettings>` и `_settings.CurrentValue.Rollypay` вместо `_settings.CurrentValue` — иначе оставь как есть.

### 3.6. `RollypayWebhookVerifier.cs`

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using YourApp.Configuration;

namespace YourApp.Services.Rollypay;

public sealed class RollypayWebhookVerifier
{
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly IOptionsMonitor<RollypaySettings> _settings;
    private readonly TimeProvider _timeProvider;

    public RollypayWebhookVerifier(IOptionsMonitor<RollypaySettings> settings, TimeProvider? timeProvider = null)
    {
        _settings = settings;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public VerificationResult Verify(string rawBody, string? timestampHeader, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(signatureHeader))
            return VerificationResult.MissingHeaders;

        if (!long.TryParse(timestampHeader, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            return VerificationResult.BadTimestamp;

        var ts = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var now = _timeProvider.GetUtcNow();
        if ((now - ts).Duration() > MaxClockSkew)
            return VerificationResult.ExpiredTimestamp;

        var secret = _settings.CurrentValue.SigningSecret;
        if (string.IsNullOrWhiteSpace(secret))
            return VerificationResult.SecretNotConfigured;

        var payload = $"{timestampHeader}.{rawBody}";
        var expected = ComputeHexHmac(secret, payload);

        if (!TryParseHex(signatureHeader.Trim(), out var providedBytes))
            return VerificationResult.BadSignature;

        var expectedBytes = HexToBytes(expected);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes)
            ? VerificationResult.Valid
            : VerificationResult.BadSignature;
    }

    public static string ComputeHexHmac(string secret, string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryParseHex(string hex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (hex.Length == 0 || hex.Length % 2 != 0) return false;
        try { bytes = HexToBytes(hex); return true; }
        catch (FormatException) { return false; }
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        return Convert.FromHexString(hex);
    }
}

public enum VerificationResult
{
    Valid,
    MissingHeaders,
    BadTimestamp,
    ExpiredTimestamp,
    BadSignature,
    SecretNotConfigured
}
```

### 3.7. `RollypayWebhookEvent.cs`

```csharp
using System.Text.Json.Serialization;

namespace YourApp.Services.Rollypay;

public sealed class RollypayWebhookEvent
{
    // true у вебхуков тестовых платежей (дублирует заголовок X-Test-Mode). Подпись — как у боевых.
    [JsonPropertyName("test")]
    public bool Test { get; set; }

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("payment_id")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}
```

### 3.8. `RollypayWebhookProcessor.cs` ← **ЕДИНСТВЕННЫЙ файл, который меняется доменно**

Шаблон от VPN-бота. Адаптируй под платёжный сервис целевого проекта (вместо `IPaymentService` — твой эквивалент: `IBillingService`, `IInvoiceService`, и т.п.). Что **не менять**: switch-case структура, обработка `payment.created` как no-op, идемпотентная проверка по `payment_id`.

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YourApp.Services;   // ваш IPaymentService (или эквивалент)

namespace YourApp.Services.Rollypay;

public sealed class RollypayWebhookProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IPaymentService _paymentService;          // ЗАМЕНИ на свой сервис
    private readonly IAdminNotifier _adminNotifier;            // ЗАМЕНИ на свой канал нотификаций (Telegram / email / Slack)
    private readonly ILogger<RollypayWebhookProcessor> _logger;

    public RollypayWebhookProcessor(
        IPaymentService paymentService,
        IAdminNotifier adminNotifier,
        ILogger<RollypayWebhookProcessor> logger)
    {
        _paymentService = paymentService;
        _adminNotifier = adminNotifier;
        _logger = logger;
    }

    public async Task<ProcessResult> ProcessAsync(string rawBody, CancellationToken ct)
    {
        RollypayWebhookEvent? evt;
        try { evt = JsonSerializer.Deserialize<RollypayWebhookEvent>(rawBody, JsonOptions); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Rollypay webhook payload is not valid JSON");
            return ProcessResult.BadPayload;
        }

        if (evt is null || string.IsNullOrWhiteSpace(evt.EventType))
            return ProcessResult.BadPayload;

        if (string.IsNullOrWhiteSpace(evt.PaymentId))
        {
            _logger.LogWarning("Rollypay webhook {EventType} missing payment_id", evt.EventType);
            return ProcessResult.BadPayload;
        }

        _logger.LogInformation("Rollypay webhook received: {EventType} payment_id={PaymentId}",
            evt.EventType, evt.PaymentId);

        switch (evt.EventType)
        {
            case "payment.paid":
                // ДОМЕННАЯ ЛОГИКА: продлить подписку / зачислить баланс / выдать токены
                // Должна быть ИДЕМПОТЕНТНОЙ: ретраи Rollypay прилетят повторно
                await _paymentService.ConfirmByProviderAsync(evt.PaymentId!, ct);
                return ProcessResult.Handled;

            case "payment.canceled":
                await _paymentService.CancelByProviderAsync(evt.PaymentId!, "rollypay_canceled", ct);
                return ProcessResult.Handled;

            case "payment.created":
                // No-op: Payment row уже создана на стороне клиента при вызове POST /payments
                return ProcessResult.Handled;

            case "payment.chargeback":
            case "payment.refunded":
                // Подписку автоматически НЕ откатываем — слишком опасно для случая ошибочного chargeback.
                // Нотифицируем админа и оставляем решение за ним.
                await _adminNotifier.NotifyAsync(
                    $"⚠️ Rollypay {evt.EventType}: provider_id={evt.PaymentId} order_id={evt.OrderId} amount={evt.Amount} {evt.Currency}. " +
                    "Проверьте вручную.", ct);
                return ProcessResult.Handled;

            case "payout.completed":
            case "refund_request.completed":
                _logger.LogInformation("Rollypay account event ignored: {EventType}", evt.EventType);
                return ProcessResult.Handled;

            default:
                _logger.LogWarning("Rollypay webhook unknown event_type: {EventType}", evt.EventType);
                return ProcessResult.UnknownEvent;
        }
    }
}

public enum ProcessResult { Handled, BadPayload, UnknownEvent }
```

**Что должны делать `ConfirmByProviderAsync` и `CancelByProviderAsync` в целевом проекте:**

- `ConfirmByProviderAsync(providerPaymentId, ct)`: найти в БД свою Payment-запись по колонке `ProviderPaymentId` (которую сохраняли при создании платежа). Если статус уже Confirmed — вернуть false (no-op, идемпотентность). Иначе: перевести в Confirmed, записать `providerPaymentId` как tx-reference, выдать юзеру что положено (подписка/баланс/доступ), уведомить.
- `CancelByProviderAsync(providerPaymentId, reason, ct)`: найти Payment, если в Pending — перевести в Rejected с причиной. Иначе no-op.

Если в проекте уже нет Payment-таблицы — заведи минимальную: `(id, provider, provider_payment_id UNIQUE, order_id, amount, currency, status, created_at, confirmed_at)`. `provider_payment_id` под `UNIQUE` индекс — нативная защита от двойного подтверждения на уровне БД.

### 3.9. `RollypayWebhookEndpoint.cs`

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using YourApp.Services.Rollypay;

namespace YourApp.Api;

public static class RollypayWebhookEndpoint
{
    private const int MaxBodyBytes = 64 * 1024;

    public static void MapRollypayWebhook(WebApplication app)
    {
        app.MapPost("/webhook/rollypay", async (
                HttpContext ctx,
                RollypayWebhookVerifier verifier,
                RollypayWebhookProcessor processor,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("RollypayWebhookEndpoint");

                if (ctx.Request.ContentLength is long length && length > MaxBodyBytes)
                {
                    logger.LogWarning("Rollypay webhook body too large: {Length} bytes", length);
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                string rawBody;
                using (var reader = new StreamReader(ctx.Request.Body, leaveOpen: true))
                    rawBody = await reader.ReadToEndAsync(ct);

                if (rawBody.Length > MaxBodyBytes)
                {
                    logger.LogWarning("Rollypay webhook body exceeded {Max} chars", MaxBodyBytes);
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                var timestamp = ctx.Request.Headers["X-Timestamp"].ToString();
                var signature = ctx.Request.Headers["X-Signature"].ToString();

                var verification = verifier.Verify(rawBody, timestamp, signature);
                if (verification != VerificationResult.Valid)
                {
                    logger.LogWarning("Rollypay webhook verification failed: {Reason}", verification);
                    return Results.Unauthorized();
                }

                var result = await processor.ProcessAsync(rawBody, ct);
                return result switch
                {
                    ProcessResult.Handled => Results.Ok(),
                    ProcessResult.BadPayload => Results.BadRequest(),
                    ProcessResult.UnknownEvent => Results.Ok(),
                    _ => Results.Ok()
                };
            })
            .RequireRateLimiting("rollypay-webhook");
    }
}
```

### 3.10. Тест на верификатор (HMAC golden — самый важный из тестов)

Если этот тест зелёный, значит подпись считается правильно и реальные вебхуки будут верифицироваться. Без него рискуешь deploy'ить сломанный HMAC и узнать об этом только из логов прода.

```csharp
using Microsoft.Extensions.Options;
using Xunit;
using YourApp.Configuration;
using YourApp.Services.Rollypay;

namespace YourApp.Tests;

public class RollypayWebhookVerifierTests
{
    private const string Secret = "test-signing-secret";

    [Fact]
    public void Verify_ValidSignature_ReturnsValid()
    {
        var verifier = CreateVerifier(out var fakeTime);
        var ts = fakeTime.GetUtcNow().ToUnixTimeSeconds().ToString();
        const string body = """{"event_type":"payment.paid","payment_id":"pay_1"}""";
        var sig = RollypayWebhookVerifier.ComputeHexHmac(Secret, $"{ts}.{body}");

        Assert.Equal(VerificationResult.Valid, verifier.Verify(body, ts, sig));
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsBadSignature()
    {
        var verifier = CreateVerifier(out var fakeTime);
        var ts = fakeTime.GetUtcNow().ToUnixTimeSeconds().ToString();
        const string body = """{"event_type":"payment.paid","payment_id":"pay_1"}""";
        const string tampered = """{"event_type":"payment.paid","payment_id":"pay_2"}""";
        var sig = RollypayWebhookVerifier.ComputeHexHmac(Secret, $"{ts}.{body}");

        Assert.Equal(VerificationResult.BadSignature, verifier.Verify(tampered, ts, sig));
    }

    [Fact]
    public void Verify_ExpiredTimestamp_Rejected()
    {
        var verifier = CreateVerifier(out var fakeTime);
        var oldTs = fakeTime.GetUtcNow().AddMinutes(-10).ToUnixTimeSeconds().ToString();
        const string body = """{}""";
        var sig = RollypayWebhookVerifier.ComputeHexHmac(Secret, $"{oldTs}.{body}");

        Assert.Equal(VerificationResult.ExpiredTimestamp, verifier.Verify(body, oldTs, sig));
    }

    private static RollypayWebhookVerifier CreateVerifier(out FakeTimeProvider fakeTime)
    {
        var options = Options.Create(new RollypaySettings { SigningSecret = Secret });
        var monitor = new StaticOptionsMonitor<RollypaySettings>(options.Value);
        fakeTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_750_000_000));
        return new RollypayWebhookVerifier(monitor, fakeTime);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<T, string?> _) => null!;
    }
}
```

Дополнительные test cases которые стоит написать (по аналогии):

- `MissingHeaders` для null/пустых `X-Timestamp` или `X-Signature`
- `BadTimestamp` для нечислового `X-Timestamp`
- `BadSignature` для не-hex `X-Signature`
- `SecretNotConfigured` если `RollypaySettings.SigningSecret == ""`

Для `RollypayClient` тесты — через `HttpMessageHandler`-стуб: проверь, что `X-API-Key` ставится, что `X-Nonce` per-request UUID (два вызова — два разных), что 4xx бросает `RollypayApiException` (статус + тело сохранены), 5xx/429 бросает `RollypayUnavailableException`, отсутствующий `ApiKey` → `InvalidOperationException`.

Для `RollypayWebhookProcessor` — Moq на `IPaymentService`: `payment.paid` → `ConfirmByProviderAsync` вызван 1 раз; повторный вызов с тем же payload → метод вызывается дважды (он сам идемпотентный — это уже на уровне сервиса), но физических побочных эффектов нет; `payment.chargeback` → не дёргает confirm, дёргает админ-нотификатор.

---

## 4. Wiring и infra (то, что забывается между сессиями)

### DI (Program.cs)

```csharp
// Конфиг
builder.Services.Configure<RollypaySettings>(builder.Configuration.GetSection("Rollypay"));

// HttpClient
builder.Services.AddHttpClient<IRollypayClient, RollypayClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptionsMonitor<RollypaySettings>>().CurrentValue;
    var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://rollypay.io" : settings.BaseUrl;
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Webhook
builder.Services.AddSingleton<RollypayWebhookVerifier>();
builder.Services.AddScoped<RollypayWebhookProcessor>();

// Rate-limit для webhook (защита от мусора с внешнего интернета):
builder.Services.AddRateLimiter(opt =>
{
    opt.AddPolicy("rollypay-webhook", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0
        });
    });
});

// ...

var app = builder.Build();
app.UseRateLimiter();
RollypayWebhookEndpoint.MapRollypayWebhook(app);
```

### Reverse-proxy (Caddy)

```caddyfile
your-domain.com {
    handle /webhook/* {
        reverse_proxy app:5000
    }
}
```

Если nginx — стандартный `proxy_pass` без вмешательства в body / заголовки. Не нормализуй JSON, не убирай заголовки `X-*` через `proxy_hide_header`.

---

## 5. Грабли (порядок убывания вероятности выстрелить в ногу)

1. **Парсить тело до HMAC-проверки**. Любой JSON-парсер нормализует пробелы/порядок ключей → подпись не совпадёт. Читать `Request.Body` как **строку**, потом парсить дубль для бизнес-логики.

2. **`X-Nonce` не уникален per-request**. Anti-replay 10-минутное окно. Если генерите через `DateTime.UtcNow.Ticks` или из seed-pool — поймаете 401 при бурстах. Только `Guid.NewGuid().ToString("D")`.

3. **Тестовый режим = боевая касса с флагом**. Sandbox есть (§2.1), но он не изолирован ключами: те же `api_key`/`signing_secret`, разница только в `"test": true`. Отсюда два симметричных прокола — забыть флаг в dev (спишутся реальные деньги) и забыть выключить его в проде (юзеры получат баланс бесплатно). Флаг должен приходить из конфига, а не из кода, и проверяться на старте.

4. **Status lifecycle забывается**. После `paid` могут прийти `chargeback`/`refunded`. Заранее решите: автоматически снимать подписку или только нотификация админу. Шаблон в §3.8 — нотификация (безопасный дефолт).

5. **`payment.created` тоже прилетит вебхуком**. Если на этот же `payment_id` ваша сторона уже создала локальную запись (через ответ `POST /payments`), на `created`-событие — **no-op**, не плодите дубли. Реагируйте только на `paid` и `canceled`. В шаблоне §3.8 уже учтено.

6. **Идемпотентность paid-события**. Они шлют до 8 ретраев. Ваш `ConfirmByProviderAsync` должен быть идемпотентным: первый вызов → продлили подписку, повторные → no-op. Не выстрелит, если делаете lookup по `ProviderPaymentId` (UNIQUE index) и проверяете `Status == Confirmed` перед действием.

7. **Секреты в правильном месте**. Зависит от проекта:
   - Если у проекта есть «AppSettings в БД как source of truth» — туда же положить `ApiKey`/`SigningSecret`. Не дублировать в user-secrets/appsettings.json, иначе при апдейте из админки забудете обновить файл и получите неконсистентное состояние.
   - Если проект стандартный — user-secrets для dev, env-vars / KeyVault для prod.

8. **TTL чека**. У Rollypay по умолчанию 30 мин на оплату. В вашей таблице `Payment.ExpiresAt` должен быть `>=` этому значению, иначе локально вы expire'нете до того, как они отправят `payment.paid` от опоздавшего юзера. Безопасный множитель — 2× (TTL вашей записи 60 мин, у Rollypay 30 мин).

9. **Юридика**. Подключение агрегатора **обязано** быть отражено в оферте + политике конфиденциальности (передача суммы/order_id/description/user_id третьему лицу). Если у вас есть механизм `LegalVersion` с принудительным re-acceptом — bump'ните версию, иначе действующие юзеры технически акцептовали редакцию без упоминания агрегатора.

   Шаблон правок (вставляй в свою оферту, мелкие правки терминов под свой бренд):

   **В раздел «Термины»:**
   > N. Платежный агрегатор — юридическое лицо или сервис, через которого <Ваш сервис> принимает оплату от Пользователя и которому Пользователь предоставляет платежные реквизиты при оплате.

   **В раздел «Порядок оплаты»:**
   > N. При выборе оплаты через платежного агрегатора Пользователь переходит на платежную страницу агрегатора и совершает оплату на условиях, опубликованных агрегатором. Подтверждение оплаты поступает в <Ваш сервис> автоматически на основании уведомления агрегатора.
   > N+1. Оператор не является стороной правоотношений между Пользователем и платежным агрегатором в части процессинга платежа, не имеет доступа к платежным реквизитам Пользователя и не отвечает за решения агрегатора об отклонении, заморозке или возврате платежа.

   **В раздел «Возвраты»:**
   > N. Если оплата была произведена через платежного агрегатора, фактический срок зачисления возврата определяется правилами агрегатора и банка-эмитента Пользователя и может превышать срок принятия Оператором решения о возврате.

   **В политику конфиденциальности, раздел «Какие данные обрабатываются»:**
   > N. При оплате через платежного агрегатора <Ваш сервис> передает агрегатору минимально необходимый набор сведений: сумму, валюту, идентификатор платежной заявки, описание платежа и технический идентификатор учетной записи Пользователя. Платежные реквизиты (карта/счёт), передаваемые Пользователем агрегатору на его странице, <Ваш сервис> не получает и не хранит.

10. **Лимит размера тела вебхука**. Поставьте hard cap (например 64 KB) до чтения, иначе тривиальный DOS-вектор (`Content-Length: 10GB`). В §3.9 уже учтено.

11. **`success_redirect_url` для веб-приложения**. Если это не Telegram-бот, сделайте отдельный success-эндпоинт, который показывает «спасибо, доступ активируется через минуту», иначе юзер увидит сырую страницу до прихода webhook'а и пожалуется в саппорт. Для Telegram-бота `https://t.me/your_bot/` — нормально.

12. **`amount` — строка, не number**. JSON-сериализатор по умолчанию закодирует `decimal` как число. Их API хочет `"1500.00"` строкой. Если шлёте `decimal` через `JsonSerializer.Serialize` — приведите вручную через `amount.ToString("F2", CultureInfo.InvariantCulture)`.

13. **Webhook домен под Cloudflare-фронтом**. Если перед вашим бэкендом стоит Cloudflare, проверьте что Rollypay не отрезается WAF (их IP — не из общеизвестных пулов). Если есть — добавьте allowlist по path `/webhook/rollypay`.

14. **Документация Rollypay врёт про префикс `rpk_live_…`** — это пример, не валидируй формат ключа. Реальный ключ может быть произвольной строкой.

---

## 6. Чеклист подключения нового проекта

- [ ] Касса в Rollypay создана, в UI выписаны: `API Key`, `Signing Secret`, `Terminal ID` (последний — себе в заметки).
- [ ] Реверс-прокси: добавлен `/webhook/*` (или ваш путь) → app:port.
- [ ] В кассе указан `callback_url = https://your-domain/webhook/rollypay` (вкладка «Настройки»), `success_redirect_url` + `fail_redirect_url`.
- [ ] Созданы 8 файлов из §3 (3.1–3.9), namespace заменён на проектный.
- [ ] `RollypayWebhookProcessor` (§3.8) переписан под целевой `IPaymentService` (или эквивалент). Создан/расширен Payment-сервис с методами `ConfirmByProviderAsync` + `CancelByProviderAsync`. В Payment-таблице есть колонка `ProviderPaymentId` (string, UNIQUE) и желательно `ProviderPayUrl` (string).
- [ ] DI wiring §4 в `Program.cs`. `app.UseRateLimiter()` стоит до `MapRollypayWebhook`.
- [ ] Минимум тест §3.10 (HMAC golden) скопирован и зелёный.
- [ ] Юридические документы дополнены шаблоном из граблей §5 #9. `LegalVersion` bump'нут (если есть механизм re-accept).
- [ ] Конфиг проложен: где живут `ApiKey` + `SigningSecret`, как админ переключает `Enabled` (kill switch без рестарта).
- [ ] `TestMode` проброшен в конфиг и **запрещён в Production** startup-гвардом (§2.1: тестовый платёж бесплатный, но баланс зачислится настоящий).
- [ ] Прогон end-to-end в тестовом режиме (§7), затем один боевой платёж минимальной суммой с реального телефона.

---

## 7. End-to-end smoke test

Прогоняется **в тестовом режиме, без реальных денег** (§2.1) — реальная оплата нужна только как финальная проверка перед публичным запуском.

1. Включить провайдера и тестовый режим через конфиг (в ORR — `Payments:ActiveProvider=rollypay` + `Payments:Rollypay:TestMode=true`).
2. Создать платёж через ваш UI (бот / веб).
3. В логах: `POST /api/v1/payments` → 200 OK → `pay_url` сохранён, в теле ушло `"test":true`.
4. Открыть `pay_url` — вместо QR будет баннер тестового режима с кнопкой симуляции. Нажать «оплачено».
5. В логах: `Rollypay webhook received: payment.paid payment_id=pay_…` → верификатор вернул `Valid` → processor вызвал `ConfirmByProviderAsync` → статус Payment стал `Confirmed` → юзер получил нотификацию.
6. Re-fire того же события (если Rollypay UI позволяет) → второй раз `ConfirmByProviderAsync` возвращает false (или просто без побочных эффектов) — повторного продления нет.
7. Прогнать `outcome: "failed"` — платёж должен уйти в `canceled`, баланс не тронут.
8. Создать ещё один платёж, не оплачивать, дождаться истечения TTL → должен прийти `payment.canceled` или статус через `GET /api/v1/payments/{id}` становится `expired`.
9. **Выключить `TestMode`** и повторить шаги 2–5 один раз реальной минимальной суммой с реального телефона: тестовый режим подменяет способ оплаты заглушкой, поэтому саму СБП-цепочку он не проверяет.

---

## 8. Стоит ли вообще подключать Rollypay

Решение зависит от ниши:

- **СБП от физлица/мерчанта без эквайринга** — Rollypay сильно проще, чем заводить юрлицо и подключать эквайринг Сбера/Тинькофф. Комиссия ~3% — приемлемая плата за «работает из коробки».
- **Есть уже свой эквайринг или ЮКасса** — Rollypay не нужен, дешевле напрямую.
- **Только крипта** — Rollypay вообще не про вас.
- **Параллельно крипта + СБП** — Rollypay для СБП, прямой on-chain трекинг для крипты. Не пытайтесь склеить — это два разных мира с разными moving parts.

### Где живёт эталонная имплементация (если есть доступ)

Этот мануал родился из интеграции в VPN-боте (`d:\Dev\Projects\VPN\VpnBot\src\VpnBot.Core\Services\Rollypay\`). Если запускаешь сессию в VPN-репо и хочешь глянуть как оно реально работает в проде — пути там есть. Если работаешь в другом проекте — игнорируй эту секцию, всё нужное есть в §3 этого документа.
