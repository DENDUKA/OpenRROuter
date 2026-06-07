# Billing — Real-Money Payments: Implementation Plan

_Status: ready for implementation_
_Created: 2026-06-06_
_Scope: OpenRussiaRouter (.NET 8 microservices)_

## Context

`BillingService` уже реализует полноценный **внутренний** биллинг: append-only ledger
(`billing.ledger_entries`), списания за usage, баланс, каталог цен моделей, аномалии —
всё по gRPC, баланс в **USD**. Пополнение сегодня **только виртуальное**
(`LedgerEntryType.VirtualTopUp`, админ через `/admin/users`).

Этот план добавляет **реальный self-serve платёж** (пользователь пополняет баланс сам),
построенный как **провайдер-агностичная инфраструктура** с **мок-провайдером** в роли кассы.
Конкретная платёжная система пока не выбрана — она подключается одним адаптером позже,
ядро при этом не меняется.

## Locked decisions

1. **Внутренняя валюта — всегда USD.** `BuildingBlocks.Money.Currency` остаётся `Usd`-only.
   Ledger, баланс, цены, вся математика BillingService не трогаются.
2. **Провайдер — за интерфейсом `IPaymentProvider`.** Сейчас единственная реализация —
   `MockPaymentProvider`. Реальная касса = +1 класс + регистрация в DI; ядро/домен/webhook-
   плумбинг/UI не меняются. Справочник для будущего реального адаптера (СБП-агрегатор) —
   `OpenRussiaRouter/docs/rollypay-dotnet.md` (ложится ровно в этот интерфейс).
3. **Валюта отображения — слой WebApp.** Поддерживаем `RUB` (по умолчанию), `USD`, `EUR`.
   Конверсия USD↔display через `IExchangeRateProvider`.
4. **Источник курсов — ЦБ РФ, фетч раз в день** (+ на старте), за тем же `IExchangeRateProvider`.
   Last-known-good + конфиг-fallback. Курс **замораживается на момент создания платежа**.
5. **`payments` хранит и то, что платит юзер (`amount`+`currency`), и замороженный
   `usd_amount`+`fx_rate`.** Confirm зачисляет ровно `usd_amount` — без FX-дрейфа, идемпотентно.

## Target flow

```
                   ┌─ provider-agnostic ядро (НЕ меняется при смене кассы) ─┐
[WebApp UI] ─►  IPaymentProvider.CreateAsync ─►  BillingService.CreatePayment(gRPC)
  "Пополнить"          │                              └─ payments(status=created), ledger пока нет
  сумма + валюта       │                                 usd_amount + fx_rate заморожены
                       ▼
                 pay_url (у мока — на свою fake-checkout страницу)
                       │  (оплата)
   webhook ◄───────────┘
[WebApp] POST /webhook/{provider}
   │ 1. IPaymentProvider.Verify(raw, headers)   ◄─ единственное, что специфично провайдеру
   │ 2. IPaymentProvider.Parse(raw) → {providerPaymentId, status}
   ▼
BillingService.ConfirmPaymentByProvider(gRPC)
   │ идемпотентно: payments.provider_payment_id UNIQUE + ledger idempotency_key UNIQUE
   ▼ ledger += ExternalTopUp (usd_amount)  →  баланс
```

Разделение ответственности обусловлено тем, что **у WebApp нет своей БД** (только gRPC-клиенты),
а ledger+Postgres живут в **BillingService**:

- **BillingService** — владелец денег: таблица `payments`, новый `LedgerEntryType.ExternalTopUp`,
  gRPC-методы платежей. Провайдер-агностичен (хранит `provider` как строку).
- **WebApp** — публичный HTTP-фронт: `IPaymentProvider` + `MockPaymentProvider`, webhook-эндпоинт,
  валюта отображения + ЦБ-рефрешер, UI пополнения.
- **AuthService** — хранит предпочтение валюты на профиле пользователя.

---

## Phase 1 — Contracts (proto)

**Файл:** `src/OpenRussiaRouter.Contracts/Protos/billing.proto`

Добавить в `service BillingService`:
```proto
rpc CreatePayment (CreatePaymentRequest) returns (CreatePaymentResponse);
rpc ConfirmPaymentByProvider (ConfirmPaymentByProviderRequest) returns (ConfirmPaymentByProviderResponse);
rpc CancelPaymentByProvider (CancelPaymentByProviderRequest) returns (CancelPaymentByProviderResponse);
rpc GetPayment (GetPaymentRequest) returns (PaymentRecord);
rpc ListPayments (ListPaymentsRequest) returns (ListPaymentsResponse);
```

Сообщения:
- `CreatePaymentRequest { user_id, provider, amount, currency, usd_amount, fx_rate, provider_payment_id (опц.) }`
- `CreatePaymentResponse { payment_id, error_code }`
- `PaymentRecord { id, user_id, provider, provider_payment_id, amount, currency, usd_amount, fx_rate, status, created_at_utc, confirmed_at_utc }`
- `ConfirmPaymentByProviderRequest { provider, provider_payment_id }`
- `ConfirmPaymentByProviderResponse { confirmed, ledger_entry_id, balance_usd, error_code }`
- `CancelPaymentByProviderRequest { provider, provider_payment_id, reason }`
- `CancelPaymentByProviderResponse { canceled, error_code }`
- `GetPaymentRequest { payment_id }`
- `ListPaymentsRequest { user_id, page_size }` / `ListPaymentsResponse { repeated PaymentRecord records }`

> Денежные поля — строками (`decimal.ToString(InvariantCulture)`), как во всём proto.
> `.csproj` не меняется — proto подключён `GrpcServices="Both"`, клиент в WebApp генерится автоматически.

---

## Phase 2 — BillingService: платёжный домен (provider-agnostic)

| Файл | Действие |
|---|---|
| `db/migrations/billing/013_payments.sql` | **NEW.** Таблица `billing.payments` |
| `Domain/Enums/PaymentStatus.cs` | **NEW.** `Created, Paid, Canceled, Expired` |
| `Domain/Enums/LedgerEntryType.cs` | `+ ExternalTopUp = 3` |
| `Domain/Entities/Payment.cs` | **NEW.** record (см. ниже) |
| `Infrastructure/Persistence/PaymentEntity.cs` | **NEW.** Dapper-проекция |
| `Infrastructure/Persistence/BillingPersistenceMapper.cs` | `+ ToDomain/ToEntity(Payment)` |
| `Application/Services/IBillingRepository.cs` | `+` методы платежей (см. ниже) |
| `Infrastructure/Repository/BillingRepository.cs` | Dapper-реализация; **confirm в одной транзакции**; правка `ListTopUpsAsync` |
| `Infrastructure/Repository/InMemoryBillingRepository.cs` | Зеркальная in-memory реализация |
| `Application/Services/BillingApplicationService.cs` | `CreatePaymentAsync`, `ConfirmPaymentByProviderAsync`, `CancelPaymentByProviderAsync` |
| `Infrastructure/Grpc/GrpcBillingService.cs` | 5 override-методов |
| `src/OpenRussiaRouter.BuildingBlocks/Errors/ErrorCodes.cs` | `+ PaymentNotFound, PaymentAlreadyProcessed, UnsupportedCurrency, PaymentProviderUnavailable` |

**`Payment` (domain record):**
```csharp
public sealed record Payment(
    string Id, string UserId, string Provider, string? ProviderPaymentId,
    MoneyAmount UsdAmount,        // замороженный кредит, всегда USD
    decimal Amount, string Currency, decimal FxRate,  // что платит юзер
    PaymentStatus Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? ConfirmedAtUtc);
```

**`IBillingRepository` (+):**
```csharp
Task<Payment> CreatePaymentAsync(Payment payment, CancellationToken ct = default);
Task<Payment?> GetPaymentAsync(string id, CancellationToken ct = default);
Task<Payment?> GetPaymentByProviderRefAsync(string provider, string providerPaymentId, CancellationToken ct = default);
Task<(IReadOnlyCollection<Payment> Items, bool HasMore)> ListPaymentsAsync(string userId, int limit, CancellationToken ct = default);
// confirm атомарно: update payments -> Paid + insert ledger(ExternalTopUp)
Task<LedgerEntry> ConfirmPaymentAtomicAsync(Payment payment, LedgerEntry ledgerEntry, CancellationToken ct = default);
Task MarkPaymentCanceledAsync(string paymentId, string reason, DateTimeOffset nowUtc, CancellationToken ct = default);
```

**`013_payments.sql`:**
```sql
create table if not exists billing.payments (
    id varchar(64) primary key,
    user_id varchar(64) not null,
    provider varchar(32) not null,
    provider_payment_id varchar(128) null,
    amount numeric(18,2) not null,            -- что платит юзер
    currency varchar(8) not null,             -- RUB/USD/EUR
    usd_amount numeric(18,6) not null,        -- замороженный кредит
    fx_rate numeric(18,8) not null,
    status varchar(16) not null,
    created_at_utc timestamptz not null,
    confirmed_at_utc timestamptz null
);
create unique index if not exists ux_billing_payments_provider_ref
    on billing.payments (provider, provider_payment_id)
    where provider_payment_id is not null;     -- нативная защита от двойного confirm
create index if not exists ix_billing_payments_user_created
    on billing.payments (user_id, created_at_utc desc);
```

**Confirm-логика (`BillingApplicationService.ConfirmPaymentByProviderAsync`):**
- lookup по `(provider, provider_payment_id)`;
- если `Status == Paid` → **no-op**, вернуть текущий баланс (идемпотентность);
- иначе `ConfirmPaymentAtomicAsync`: в одной транзакции `status=Paid, confirmed_at=now`
  + ledger-запись `Type=ExternalTopUp, Amount=UsdAmount, IdempotencyKey="payment:{id}", Reason="Top-up {amount} {currency}"`.
- Глобальный UNIQUE на `ledger_entries.idempotency_key` — вторая защита от двойного зачисления.

**Правка истории пополнений** — `BillingRepository.ListTopUpsAsync` (сейчас `where ... type = 'VirtualTopUp'`):
```sql
where user_id = @UserId and type in ('VirtualTopUp','ExternalTopUp')
```
(аналогично в `InMemoryBillingRepository`). Так реальные пополнения попадают в Top-Up History.

> Регистрация repo не меняется — `AddBillingInfrastructure` уже резолвит
> `BillingRepository`/`InMemoryBillingRepository`, новые методы попадают в существующие классы.

---

## Phase 3 — WebApp: абстракция провайдера + мок + webhook

| Файл | Действие |
|---|---|
| `Services/Payments/IPaymentProvider.cs` | **NEW.** интерфейс-seam |
| `Services/Payments/PaymentDtos.cs` | **NEW.** `PaymentRequest`, `PaymentCreated`, `PaymentWebhookEvent`, `enum WebhookVerification` |
| `Services/Payments/MockPaymentProvider.cs` | **NEW.** мок-касса |
| `Services/Payments/PaymentProviderRegistry.cs` | **NEW.** резолв по `Name` |
| `Services/Payments/PaymentWebhookProcessor.cs` | **NEW.** switch по статусу → BillingProvider gRPC |
| `Services/Payments/PaymentSettings.cs` | **NEW.** конфиг |
| `Api/PaymentWebhookEndpoint.cs` | **NEW.** minimal API `MapPost("/webhook/{provider}")` |
| `Services/Billing/BillingProvider.cs` | `+ CreatePaymentAsync, ConfirmPaymentByProviderAsync, CancelPaymentByProviderAsync, ListPaymentsAsync` |

**`IPaymentProvider`:**
```csharp
public interface IPaymentProvider
{
    string Name { get; }                                              // "mock", позже "rollypay"...
    Task<PaymentCreated> CreateAsync(PaymentRequest req, CancellationToken ct);   // → ProviderPaymentId + PayUrl
    WebhookVerification Verify(string rawBody, IHeaderDictionary headers);        // подпись/HMAC
    PaymentWebhookEvent Parse(string rawBody);                                    // → ProviderPaymentId + Status
}
```

**`MockPaymentProvider`:**
- `CreateAsync` → `ProviderPaymentId = "mock_{guid}"`, `PayUrl = "/pay/mock/{paymentId}"` (fake-checkout в самом WebApp).
- `Verify` → `Valid` в dev либо проверка `Mock.SharedSecret` из заголовка.
- `Parse` → читает наш собственный JSON, отданный fake-checkout страницей.

**`PaymentWebhookEndpoint` (грабли учтены):**
- hard-cap тела (64 KB) до чтения;
- **читать `Request.Body` как строку ДО любого парсинга** (для будущей HMAC-проверки — нормализация JSON ломает подпись);
- `registry[provider].Verify(...)` → при `!= Valid` вернуть 401;
- `processor.ProcessAsync(rawBody)` → `Results.Ok()`;
- `.RequireRateLimiting("payment-webhook")`.

**`PaymentWebhookProcessor`** — switch по `status`: `paid` → `BillingProvider.ConfirmPaymentByProviderAsync`;
`canceled`/`expired` → `CancelPaymentByProviderAsync`; `created` → no-op. Идемпотентность делегируется BillingService.

---

## Phase 4 — WebApp: валюта отображения + курсы ЦБ РФ

| Файл | Действие |
|---|---|
| `Services/Currency/DisplayCurrency.cs` | **NEW.** `enum { Rub, Usd, Eur }` + helpers (символ/код, дефолт=Rub) |
| `Services/Currency/IExchangeRateProvider.cs` | **NEW.** seam: `decimal UsdTo(DisplayCurrency)`, `decimal ToUsd(decimal amount, DisplayCurrency)` |
| `Services/Currency/CbrRateSnapshot.cs` | **NEW.** потокобезопасный держатель (`volatile` ref на immutable record) |
| `Services/Currency/CbrClient.cs` | **NEW.** `HttpClient`-обёртка к ЦБ + парсинг XML |
| `Services/Currency/CbrExchangeRateProvider.cs` | **NEW.** реализует `IExchangeRateProvider`, читает из snapshot |
| `Services/Currency/CbrRateRefresher.cs` | **NEW.** `BackgroundService`: фетч на старте + раз в день |
| `Services/Currency/CurrencyFormatter.cs` | **NEW.** USD→display для показа баланса/цен/истории |
| `Services/Currency/ExchangeRateSettings.cs` | **NEW.** fallback-курсы, `RefreshHourMsk`, опц. `MarkupPercent` |

**Источник — ЦБ РФ:** `https://www.cbr.ru/scripts/XML_daily.asp` (официальный XML, без ключа).
ЦБ отдаёт **рубли за единицу валюты**:
- `USD→RUB` = `Value(R01235)` (`Nominal=1`);
- `USD→EUR` = кросс `Value(R01235) / Value(R01239)`;
- `USD→USD` = 1.

Парсинг-грабли: десятичный разделитель — **запятая**; есть `<Nominal>` (делить на него); читать с явной культурой (ru-RU / ручная замена `,`→`.`).

**`CbrRateRefresher` (логика):**
1. **Фетч на старте** — сразу, до первого пользовательского запроса.
2. **Дальше раз в день** (`PeriodicTimer`; цикл около 12:00 МСК — ЦБ обновляет курсы утром, в выходные не меняет).
3. **Retry с backoff** при сбое (3 попытки), потом ждём следующего цикла.

**Устойчивость:**
- ошибка фетча → держим **last-known-good** в памяти, не обнуляем;
- холодный старт + ЦБ недоступен → **fallback на статические курсы из `appsettings`**;
- snapshot в памяти теряется при рестарте → фетч-на-старте + конфиг-сид это закрывают
  (персист в БД/файл — overkill, не делаем).

**Деньги при пополнении:** юзер вводит сумму в `DisplayCurrency` →
`usd = ToUsd(amount, cur)` с учётом опц. `MarkupPercent`, `fxRate` фиксируется → оба уходят
в `CreatePayment` и **замораживаются на платеже**. Дневное обновление курса между `created` и
`paid` на уже созданный платёж не влияет. Минимум — `PaymentSettings.MinTopUpUsd`.

**Опц. `MarkupPercent`** (наценка к курсу ЦБ, напр. 2–3%) — защита от спреда ЦБ↔эквайер и
волатильности; применяется при `display→USD`. Дефолт `0`, значение — бизнес-решение.

---

## Phase 5 — WebApp: UI

| Файл | Действие |
|---|---|
| `Pages/Workspace/Credits.cshtml` + `.cs` | Баланс/история — через `CurrencyFormatter`. Форма «Пополнить»: сумма + валюта (дефолт из профиля) → `OnPostTopUpAsync` → `CreatePayment` → redirect на `PayUrl` |
| `Pages/Pay/Mock.cshtml` + `.cs` | **NEW.** Fake-checkout: сумма + кнопки «Оплатить»/«Отменить» → POST `/pay/mock/{id}/complete` → self-webhook в `/webhook/mock` (прогон всего пути) |
| `Pages/Workspace/PaymentSuccess.cshtml` | **NEW.** «Платёж обрабатывается, баланс обновится в течение минуты» |
| `Pages/Workspace/Settings.cshtml` (+`.cs`) | Селектор валюты по умолчанию → сохранение в профиль (Phase 6). Сейчас страница-заглушка |
| `Pages/Shared/_WorkspaceLayout.cshtml` | (опц.) переключатель валюты в шапке |

---

## Phase 6 — AuthService: предпочтение валюты на профиле

| Файл | Действие |
|---|---|
| `db/migrations/auth/007_user_display_currency.sql` | **NEW.** `alter table auth.users add column if not exists display_currency varchar(8) not null default 'RUB';` |
| `Domain/Entities/UserAccount.cs` | `+ DisplayCurrency` |
| `Infrastructure/Persistence/UserAccountEntity.cs` | `+ display_currency` |
| `Infrastructure/Repository/AuthRepository.cs` + `InMemoryAuthRepository.cs` | select/insert колонки + `UpdateDisplayCurrencyAsync` |
| `Application/Services/IAuthRepository.cs` | `+ UpdateDisplayCurrencyAsync, GetUserProfileAsync` |
| `Protos/auth.proto` | `+ rpc GetUserProfile, rpc UpdateUserSettings(user_id, display_currency)` |
| `Infrastructure/Grpc/GrpcAuthService.cs` | реализация |
| WebApp `Services/Auth/AuthProvider.cs` | обёртки `GetProfile`/`UpdateSettings` |

> Дешёвый fallback (если отложить правку AuthService) — хранить выбор в cookie.
> По вводной «нужна вся инфраструктура» → делаем через профиль.

---

## Phase 7 — DI / конфиг / pipeline

**`WebApp/Program.cs`:**
```csharp
builder.Services.Configure<PaymentSettings>(builder.Configuration.GetSection("Payments"));
builder.Services.Configure<ExchangeRateSettings>(builder.Configuration.GetSection("ExchangeRates"));
builder.Services.AddHttpClient<CbrClient>();
builder.Services.AddSingleton<CbrRateSnapshot>();
builder.Services.AddSingleton<IExchangeRateProvider, CbrExchangeRateProvider>();
builder.Services.AddSingleton<CurrencyFormatter>();
builder.Services.AddHostedService<CbrRateRefresher>();
builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
builder.Services.AddSingleton<PaymentProviderRegistry>();
builder.Services.AddScoped<PaymentWebhookProcessor>();
builder.Services.AddRateLimiter(/* "payment-webhook": fixed window по IP */);
// ...
var app = builder.Build();
app.UseRateLimiter();                  // ДО маппинга вебхука
PaymentWebhookEndpoint.Map(app);
```

**`appsettings.json` (WebApp):**
```json
"Payments": {
  "ActiveProvider": "mock",
  "MinTopUpUsd": "1.00",
  "Mock": { "SharedSecret": "dev" }
},
"ExchangeRates": {
  "Source": "cbr",
  "RefreshHourMsk": 12,
  "MarkupPercent": "0",
  "Fallback": { "RubPerUsd": "95.0", "RubPerEur": "103.0" }
}
```

> BillingService DI/Startup не меняется (новые методы в существующем repo + GrpcBillingService).

---

## Phase 8 — Тесты

**BillingService.Tests** (PG + in-memory, паттерн уже есть):
- create → confirm → баланс вырос на `usd_amount`;
- **повторный confirm = no-op** (идемпотентность по `provider_payment_id` и по ledger idempotency_key);
- `ExternalTopUp` виден в `ListTopUps`;
- cancel из `Created` → `Canceled`; из `Paid` → no-op.

**WebApp** (если есть тест-проект; иначе добавить):
- `MockPaymentProvider` create → paid → balance (полный e2e через self-webhook);
- идемпотентность webhook (двойная доставка);
- парсер ЦБ XML: фикстура реального ответа → корректные `RubPerUsd`, кросс `USD→EUR`,
  запятая-десятичные, `Nominal`;
- рефрешер: ЦБ недоступен на старте → fallback; успешный фетч → snapshot обновился;
  ошибка после успеха → держится last-known-good;
- конвертация с `MarkupPercent`, округление RUB↔USD.

**HMAC golden-тест** — каркас под будущий реальный `Verify` (из `rollypay-dotnet.md` §3.10),
чтобы реальный адаптер сразу проверялся.

---

## Implementation order

1. **Phase 1 → 2** (proto + платёжный домен BillingService) + тесты домена. Самостоятельный, проверяемый кусок.
2. **Phase 3 + 4** (WebApp: абстракция, мок, webhook, валюта, ЦБ-рефрешер) → **Phase 5** (UI) → e2e через мок.
3. **Phase 6** (AuthService профиль) → подключить селектор в Settings.
4. **Phase 7** конфиг параллельно; **Phase 8** по ходу каждого слоя.

## Точки расширения (всё за интерфейсами, ядро в USD неизменно)

- **сменить/добавить кассу** → новый класс `Xxx PaymentProvider : IPaymentProvider` + регистрация.
  Мануал `OpenRussiaRouter/docs/rollypay-dotnet.md` ложится ровно сюда.
- **подключить другой источник курсов** → новая реализация `IExchangeRateProvider`.
- **добавить валюту отображения** → расширить `DisplayCurrency` + кросс-курс в провайдере.

## Open items (дефолты взяты, не блокеры)

- Минимум пополнения — `1 USD` (в конфиге).
- `MarkupPercent` — дефолт `0`, итоговое значение за владельцем.
- В k8s/compose реальный webhook потребует публичного HTTPS-роута на WebApp;
  мок работает внутри кластера без внешнего входа.

## File inventory (≈30 файлов)

- **Contracts:** `billing.proto` (+ `auth.proto` в Phase 6).
- **BuildingBlocks:** `ErrorCodes.cs`.
- **BillingService (~10):** миграция `013`, `PaymentStatus`, `LedgerEntryType`, `Payment`,
  `PaymentEntity`, `BillingPersistenceMapper`, `IBillingRepository`, `BillingRepository`,
  `InMemoryBillingRepository`, `BillingApplicationService`, `GrpcBillingService`.
- **WebApp (~16):** `Services/Payments/*` (6), `Api/PaymentWebhookEndpoint`, `Services/Billing/BillingProvider`,
  `Services/Currency/*` (8), `Pages/Workspace/Credits*`, `Pages/Pay/Mock*`, `Pages/Workspace/PaymentSuccess`,
  `Pages/Workspace/Settings*`, `Program.cs`, `appsettings.json`.
- **AuthService (~7):** миграция `007`, `UserAccount`, `UserAccountEntity`, `AuthRepository`,
  `InMemoryAuthRepository`, `IAuthRepository`, `GrpcAuthService`.
- **Tests:** BillingService payment tests, WebApp mock/webhook/FX tests, HMAC golden каркас.
