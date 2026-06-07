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
5. **`payments` хранит `provider_amount`+`provider_currency` (что платит юзер) и
   `credited_usd_amount`+`fx_rate`, который вычисляет сам Billing из доверенного FX-quote.**
   Confirm зачисляет ровно `credited_usd_amount` — без FX-дрейфа, идемпотентно.

## Target flow

```
1. [WebApp] "Пополнить" (сумма + валюта отображения)
       │ provider_amount/currency (display→provider), БЕЗ usd
       ▼
   BillingService.CreatePayment ──► Billing САМ считает credited_usd из FX-quote
       │ payments(status=Created, order_id=payment_id, credited_usd+fx заморожены)
       ▼
2. [WebApp] IPaymentProvider.CreateAsync(order_id=payment_id) ─► providerPaymentId + pay_url
       ▼
   BillingService.AttachProviderRef(payment_id, providerPaymentId, pay_url, expires_at)
       ▼
   юзер платит по pay_url (у мока — fake-checkout)
       │ webhook
       ▼
3. [WebApp] POST /webhook/{provider}
       │ IPaymentProvider.Verify(raw) → Parse → {providerPaymentId|order_id, eventType, amount, currency, eventId}
       ▼
   BillingService.ConfirmPaymentByProvider / ApplyPaymentEvent   (s2s-auth)
       │ durable inbox: payment_event (tx1) → atomic{transition + ledger} (tx2)
       │ lookup по provider_payment_id ИЛИ order_id; сверка amount/currency
       ▼ ledger += ExternalTopUp (credited_usd) → баланс
```

Разделение ответственности обусловлено тем, что **у WebApp нет своей БД** (только gRPC-клиенты),
а ledger+Postgres живут в **BillingService**:

- **BillingService** — владелец денег: таблица `payments`, новый `LedgerEntryType.ExternalTopUp`,
  gRPC-методы платежей. Провайдер-агностичен (хранит `provider` как строку).
- **WebApp** — публичный HTTP-фронт: `IPaymentProvider` + `MockPaymentProvider`, webhook-эндпоинт,
  валюта отображения + ЦБ-рефрешер, UI пополнения.
- **AuthService** — хранит предпочтение валюты на профиле пользователя.

---

## Hardening from review (2026-06-07) — invariants & rationale (Phases 1–8 updated to match)

Принято по итогам двух раундов ревью. Это инварианты и обоснование; **Phase 1–3, SQL и Phase 7
ниже переписаны под них** — отдельного «supersedes» больше нет, фазы и этот раздел согласованы.

### Integrity invariants (денежная граница доверия)
- **Billing сам считает и владеет `credited_usd_amount`** — НИКОГДА не принимает сумму кредита от
  вызывающей стороны. Caller передаёт только `provider_amount` + `provider_currency` (сколько платит
  юзер); Billing берёт курс из **доверенного FX-quote** и вычисляет USD сам. Это закрывает атаку
  «provider_amount=1 RUB, credited=100000 USD».
- **Доверенный FX-quote — на стороне Billing (выбрано: Billing сам поллит ЦБ).** `FxQuoteService` в Billing
  держит свой `CbrClient` + дневной refresh + fallback (как у WebApp, но **авторитетный** для денег).
  Зафиксировано: округление `credited_usd` — 6 знаков, half-up; **макс. свежесть `fx_at`** — 48 ч, иначе
  `CreatePayment` → `FxQuoteUnavailable`; поддерживаемые `provider_currency` — RUB/USD/EUR; `fx_at` пишется
  на платёж и замораживается. WebApp-овский ЦБ-рефрешер — **только для отображения** (опц. позже свести
  display на `GetFxQuote`, чтобы убрать дрейф preview↔credit).
- **ВСЕ ledger-мутирующие gRPC требуют service-to-service auth — не только новые payment-методы.**
  Сейчас Billing gRPC без авторизации (`Startup.cs` `AddGrpc()` без интерсептора). Закрыть s2s-токеном/mTLS:
  существующий **`CreateVirtualTopUp`** (тоже бесплатно мутирует ledger!), **`RecordUsage`**, payment-методы
  (`CreatePayment/AttachProviderRef/Confirm/ApplyEvent`) и будущие correction/reversal. Желательно
  per-caller authorization: `RecordUsage` — только ApiGateway, `CreateVirtualTopUp` — только admin-путь.
- Billing — единственный, кто мутирует ledger.
- `MockPaymentProvider` регистрируется **только в Development** (guard по `IHostEnvironment`) и
  **fail-fast в Production**, если активный провайдер = mock или реальный не сконфигурирован.

### Три различные валюты на платеже (не смешивать)
| поле | смысл |
|---|---|
| `display_amount` + `display_currency` | что юзер видит/выбрал (RUB/USD/EUR) |
| `provider_amount` + `provider_currency` | что реально спишет касса (для СБП — RUB; для мока = display) |
| `credited_usd_amount` | что зачислится в ledger (USD), **заморожено** |

Плюс `fx_rate`, `fx_source` (`cbr`/`fallback`/`quote`), `fx_at` — **проставляет Billing** из своего
доверенного quote в момент create. Caller (WebApp) передаёт только `provider_amount`+`provider_currency`
и `display_*` для показа; `credited_usd_amount` Billing вычисляет сам и замораживает на платеже.
При confirm Billing сверяет подтверждённые провайдером `amount`/`currency` с сохранёнными
`provider_amount`/`provider_currency`. (Поправка к раннему решению: расчёт кредита перенесён из
WebApp в Billing — требование денежной целостности; display-FX остаётся в WebApp.)

### State machine
`Created→Paid` | `Created→Canceled` | `Created→Expired` | `Paid→Refunded` | `Paid→ChargedBack`.
Все переходы — атомарные условные UPDATE; недопустимые переходы отвергаются, не применяются молча.

### Create flow (порядок развёрнут — нет окна потери платежа)
1. Billing `CreatePayment` → строка `Created` с внутренним `payment_id`, ожидаемыми
   `provider_amount/currency`, `credited_usd`, `fx_*`. Идемпотентно по клиентскому токену.
2. WebApp вызывает провайдера, передавая `order_id = payment_id`.
3. Billing `AttachProviderRef(payment_id, provider_payment_id, pay_url, expires_at)` — атомарно.

Webhook находит локальный платёж по `provider_payment_id` **или** `order_id`
(покрывает «webhook раньше attach»).

### Confirm (валидация в условии перехода → затем зачисление, одна транзакция)
Сумма/валюта проверяются **в самом `WHERE`**, до установки `Paid` — состояние «Paid без ledger» невозможно:
```sql
-- 1) атомарный условный переход c проверкой суммы прямо в условии
update billing.payments
set status='Paid', confirmed_at_utc=@Now, updated_at_utc=@Now
where id=@Id and status='Created'
  and provider_amount=@ConfirmedAmount and provider_currency=@ConfirmedCurrency
returning *;
-- 2) ТОЛЬКО при 1 строке, в той же транзакции:
insert into billing.ledger_entries(... type='ExternalTopUp',
  amount=<credited_usd_amount строки>, idempotency_key='payment:'||@Id ...);
```
Классификация при **0 строк** (повторный SELECT строки):
- уже `Paid` с тем же `provider_payment_id` → **идемпотентный no-op**, вернуть баланс;
- статус `Canceled`/`Expired`/иной → **ошибка перехода** (`PaymentInvalidTransition`), НЕ молчаливый no-op;
- mismatch `amount`/`currency` → НЕ зачислять, записать anomaly + payment_event, `PaymentAmountMismatch`;
- строки нет → `PaymentNotFound`.

Тесты: confirm×confirm, confirm×cancel, confirm-after-expired/canceled (→ ошибка), mismatch amount/currency.

### Refund / chargeback → reversal в ledger (политика разведена явно)
- **refund** (инициирует мерчант, ожидаемо) → **автоматический** reversal: отрицательная ledger-запись
  `LedgerEntryType.Correction`, `idempotency_key="refund:{payment_id}"`, статус → `Refunded`.
- **chargeback** (инициирует банк, спорно) → по умолчанию **только notify-admin**, статус → `ChargedBack`,
  ledger не трогаем; авто-reversal — опционально по конфигу, `idempotency_key="chargeback:{payment_id}"`.

Ключи refund и chargeback **различны** (разные операции). Append-only сохраняется в обоих случаях.

### Append-only аудит + дедуп + recovery: `billing.payment_events`
Хранит **нормализованный `PaymentEventInput`** (`payment_id, order_id, event_type, confirmed_amount,
confirmed_currency, reason, provider_event_id`) + `raw_payload` (только аудит). Нормализованные поля
обязательны: Billing provider-agnostic и `raw_payload` повторно **не парсит** — recovery-worker применяет
событие из нормализованных колонок.
- **Дедуп:** UNIQUE `(provider, provider_event_id)`; **fallback** для касс без него — UNIQUE
  `(payment_id, event_type)` (по УЖЕ разрешённому `payment_id`, а не по nullable `provider_payment_id`,
  иначе ранний webhook с NULL обходит UNIQUE).
- **Durable inbox (две транзакции):** **tx1** — вставка payment_event (`processed=false`, `payment_id`
  разрешён по `provider_payment_id` ИЛИ `order_id`); **tx2** — атомарно `{переход + ledger + processed=true}`.
  **Ответ gRPC формируется по итогу tx2** (`confirmed/ledger_entry_id/balance_usd`); webhook ACK-ается после
  возврата Billing. `processed=false` — это **сеть безопасности на случай краха между tx1 и tx2**, не
  нормальный async-путь. (Прежняя формулировка «ACK после tx1 до tx2» снята как противоречивая.)
- **Recovery-worker:** добирает `processed=false`, повторно применяет идемпотентно, инкрементит `attempts`.
- `raw_payload`: retention (напр. 90 дней) + redaction перед хранением.

### Reconciliation worker — каркас сейчас, логика при выборе провайдера
`PaymentReconciliationWorker` (WebApp) периодически досводит зависшие `Created`. User-scoped `ListPayments`
не годится — нужен **внутренний** `AcquireStalePayments(worker_id, lease_seconds, older_than, limit)`.
`FOR UPDATE SKIP LOCKED` держит лок только до конца транзакции выдачи — поэтому нужен **персистентный lease**:
колонки `lease_owner`/`lease_until_utc` на `payments`; метод атомарно ставит их (SKIP LOCKED) и возвращает
только строки с истёкшим/пустым lease. Цикл: acquire → `provider.GetPaymentAsync` →
`ConfirmPaymentByProvider`/`ApplyPaymentEvent` → снять lease. Опрос зависит от API кассы; у мока — тривиален.
Worker — в Phase 7 DI.

---

## Phase 1 — Contracts (proto)

**Файл:** `src/OpenRussiaRouter.Contracts/Protos/billing.proto`

Добавить в `service BillingService`:
```proto
rpc GetFxQuote (GetFxQuoteRequest) returns (GetFxQuoteResponse);             // доверенный курс зачисления
rpc CreatePayment (CreatePaymentRequest) returns (CreatePaymentResponse);
rpc AttachProviderRef (AttachProviderRefRequest) returns (AttachProviderRefResponse);
rpc ConfirmPaymentByProvider (ConfirmPaymentByProviderRequest) returns (ConfirmPaymentByProviderResponse);  // event_type=paid
rpc ApplyPaymentEvent (ApplyPaymentEventRequest) returns (ApplyPaymentEventResponse);  // canceled/expired/refunded/chargeback (с аудитом)
rpc GetPayment (GetPaymentRequest) returns (PaymentRecord);
rpc ListPayments (ListPaymentsRequest) returns (ListPaymentsResponse);                 // user-scoped (UI)
rpc AcquireStalePayments (AcquireStalePaymentsRequest) returns (AcquireStalePaymentsResponse);  // internal: lease+SKIP LOCKED для reconciliation
```

Сообщения (**caller НЕ передаёт credited_usd — его считает Billing**):
- `GetFxQuoteRequest { provider_currency }` / `GetFxQuoteResponse { rate, usd_per_unit, fx_source, fx_at, quote_id, expires_at }`
- `CreatePaymentRequest { user_id, provider, provider_amount, provider_currency, display_amount, display_currency, client_token }` — **без** usd_amount/fx_rate; `client_token` для идемпотентности create
- `CreatePaymentResponse { payment_id, order_id, credited_usd_amount, fx_rate, error_code }`
- `AttachProviderRefRequest { payment_id, provider_payment_id, pay_url, expires_at_utc }` / `...Response { ok, error_code }`
- `PaymentRecord { id, user_id, provider, provider_payment_id, order_id, display_amount, display_currency, provider_amount, provider_currency, credited_usd_amount, fx_rate, fx_source, fx_at_utc, status, cancel_reason, created_at_utc, expires_at_utc, confirmed_at_utc, canceled_at_utc }`
- `ConfirmPaymentByProviderRequest { provider, provider_payment_id, order_id, confirmed_amount, confirmed_currency, provider_event_id, raw_payload, signature_ok }`
- `ConfirmPaymentByProviderResponse { confirmed, ledger_entry_id, balance_usd, error_code }`
- `ApplyPaymentEventRequest { provider, provider_payment_id, order_id, event_type (canceled|expired|refunded|chargeback), reason, confirmed_amount, confirmed_currency, provider_event_id, raw_payload, signature_ok }` / `...Response { applied, ledger_entry_id, error_code }` — все provider-события **кроме `paid`** идут сюда (с аудитом); `expired`→`Expired`, `canceled`→`Canceled`
- `AcquireStalePaymentsRequest { worker_id, lease_seconds, older_than_utc, limit }` / `AcquireStalePaymentsResponse { repeated PaymentRecord records }` — атомарно ставит `lease_owner=worker_id, lease_until=now+lease` на `Created` (SKIP LOCKED), берёт только истёкший/пустой lease
- `GetPaymentRequest { payment_id }`
- `ListPaymentsRequest { user_id, page_size }` / `ListPaymentsResponse { repeated PaymentRecord records }`

> Денежные поля — строками (`decimal.ToString(InvariantCulture)`).
> **Lookup в Confirm/Cancel/ApplyEvent — по `provider_payment_id` ИЛИ `order_id`** (webhook раньше attach).
> `.csproj` не меняется — proto подключён `GrpcServices="Both"`. Мутирующие методы — под s2s-auth (см. Hardening).

---

## Phase 2 — BillingService: платёжный домен (provider-agnostic)

| Файл | Действие |
|---|---|
| `db/migrations/billing/013_payments.sql` | **NEW.** Таблицы `billing.payments` + `billing.payment_events` |
| `Domain/Enums/PaymentStatus.cs` | **NEW.** `Created, Paid, Canceled, Expired, Refunded, ChargedBack` |
| `Domain/Entities/PaymentEvent.cs` | **NEW.** запись события провайдера |
| `Application/Services/FxQuoteService.cs` | **NEW.** доверенный курс зачисления (Billing-side FX) |
| `Domain/Enums/LedgerEntryType.cs` | `+ ExternalTopUp = 3` |
| `Domain/Entities/Payment.cs` | **NEW.** record (см. ниже) |
| `Infrastructure/Persistence/PaymentEntity.cs` | **NEW.** Dapper-проекция |
| `Infrastructure/Persistence/BillingPersistenceMapper.cs` | `+ ToDomain/ToEntity(Payment)` |
| `Application/Services/IBillingRepository.cs` | `+` методы платежей (см. ниже) |
| `Infrastructure/Repository/BillingRepository.cs` | Dapper-реализация; **confirm в одной транзакции**; правка `ListTopUpsAsync` |
| `Infrastructure/Repository/InMemoryBillingRepository.cs` | Зеркальная in-memory реализация |
| `Application/Services/BillingApplicationService.cs` | `GetFxQuoteAsync`, `CreatePaymentAsync` (считает credited_usd), `AttachProviderRefAsync`, `ConfirmPaymentByProviderAsync`, `ApplyPaymentEventAsync`, `AcquireStalePaymentsAsync` |
| `Infrastructure/Grpc/GrpcBillingService.cs` | 8 override-методов (+ s2s-auth на мутирующих) |
| `Infrastructure/Workers/PaymentEventRecoveryWorker.cs` | **NEW.** дообработка `processed=false` |
| `src/OpenRussiaRouter.BuildingBlocks/Errors/ErrorCodes.cs` | `+ PaymentNotFound, PaymentAlreadyProcessed, PaymentAmountMismatch, PaymentInvalidTransition, UnsupportedCurrency, FxQuoteUnavailable, PaymentProviderUnavailable` |

**`Payment` (domain record):**
```csharp
public sealed record Payment(
    string Id, string UserId, string Provider, string? ProviderPaymentId, string OrderId,
    string ClientToken,                                       // идемпотентность create
    decimal DisplayAmount, string DisplayCurrency,            // для показа
    decimal ProviderAmount, string ProviderCurrency,          // что реально платит юзер
    MoneyAmount CreditedUsd, decimal FxRate, string FxSource, DateTimeOffset FxAtUtc, // считает Billing, заморожено
    PaymentStatus Status, string? CancelReason, string? LeaseOwner, DateTimeOffset? LeaseUntilUtc,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc, DateTimeOffset? ConfirmedAtUtc, DateTimeOffset? CanceledAtUtc);
```
**`PaymentStatus`:** `Created, Paid, Canceled, Expired, Refunded, ChargedBack`.

**`IBillingRepository` (+):**
```csharp
Task<Payment> CreatePaymentAsync(Payment payment, CancellationToken ct = default);              // status=Created
Task<bool> AttachProviderRefAsync(string paymentId, string providerPaymentId, string? payUrl, DateTimeOffset? expiresAt, CancellationToken ct = default);
Task<Payment?> GetPaymentAsync(string id, CancellationToken ct = default);
Task<Payment?> FindPaymentAsync(string provider, string? providerPaymentId, string? orderId, CancellationToken ct = default); // по ref ИЛИ order_id
Task<(IReadOnlyCollection<Payment> Items, bool HasMore)> ListPaymentsAsync(string userId, int limit, CancellationToken ct = default);
// Атомарно: payment_event + условный переход Created->Paid (amount/currency в WHERE) + ledger ExternalTopUp.
Task<ConfirmOutcome> ConfirmPaymentAtomicAsync(PaymentEventInput evt, DateTimeOffset nowUtc, CancellationToken ct = default);
// Атомарно: payment_event + переход (canceled/expired/refunded/chargeback) + (для refund / опц. chargeback) reversal-ledger Correction.
Task<ApplyOutcome> ApplyPaymentEventAtomicAsync(PaymentEventInput evt, DateTimeOffset nowUtc, CancellationToken ct = default);
Task<IReadOnlyCollection<Payment>> AcquireStalePaymentsAsync(string workerId, TimeSpan lease, DateTimeOffset olderThanUtc, int limit, CancellationToken ct = default); // SKIP LOCKED + lease_owner/lease_until
Task<IReadOnlyCollection<PaymentEvent>> ListUnprocessedEventsAsync(int limit, CancellationToken ct = default); // recovery-worker
```

**`013_payments.sql`:**
```sql
create table if not exists billing.payments (
    id varchar(64) primary key,
    user_id varchar(64) not null,
    client_token varchar(64) not null,              -- идемпотентность create
    provider varchar(32) not null,
    provider_payment_id varchar(128) null,
    order_id varchar(64) not null,                  -- = id, отдаётся провайдеру
    display_amount numeric(18,2) not null,
    display_currency varchar(8) not null,
    provider_amount numeric(18,2) not null,         -- что реально платит юзер
    provider_currency varchar(8) not null,
    credited_usd_amount numeric(18,6) not null,     -- считает Billing, заморожено
    fx_rate numeric(18,8) not null,
    fx_source varchar(16) not null,                 -- cbr/fallback
    fx_at_utc timestamptz not null,                 -- время курса (заморожено)
    status varchar(16) not null,
    cancel_reason varchar(256) null,
    pay_url varchar(512) null,
    lease_owner varchar(64) null,                   -- reconciliation lease
    lease_until_utc timestamptz null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    expires_at_utc timestamptz null,
    confirmed_at_utc timestamptz null,
    canceled_at_utc timestamptz null,
    constraint ck_payments_amounts check (display_amount > 0 and provider_amount > 0
        and credited_usd_amount > 0 and fx_rate > 0),
    constraint ck_payments_status check (status in
        ('Created','Paid','Canceled','Expired','Refunded','ChargedBack'))
);
create unique index if not exists ux_billing_payments_provider_ref
    on billing.payments (provider, provider_payment_id) where provider_payment_id is not null;
create unique index if not exists ux_billing_payments_client_token
    on billing.payments (user_id, client_token);    -- идемпотентность CreatePayment
create index if not exists ix_billing_payments_user_created
    on billing.payments (user_id, created_at_utc desc);
create index if not exists ix_billing_payments_reconcile
    on billing.payments (status, created_at_utc) where status = 'Created';  -- AcquireStalePayments

create table if not exists billing.payment_events (
    id varchar(64) primary key,
    payment_id varchar(64) not null,                -- разрешён при приёме (по provider_payment_id ИЛИ order_id)
    provider varchar(32) not null,
    provider_event_id varchar(128) null,
    provider_payment_id varchar(128) null,
    event_type varchar(32) not null,
    -- нормализованный PaymentEventInput (Billing provider-agnostic, raw_payload повторно НЕ парсит):
    order_id varchar(64) null,
    confirmed_amount numeric(18,2) null,
    confirmed_currency varchar(8) null,
    reason varchar(256) null,
    raw_payload text null,                          -- только аудит (retention + redaction)
    signature_ok boolean not null,
    processed boolean not null default false,
    attempts int not null default 0,
    received_at_utc timestamptz not null,
    processed_at_utc timestamptz null
);
create unique index if not exists ux_payment_events_provider_event
    on billing.payment_events (provider, provider_event_id) where provider_event_id is not null;
-- fallback-дедуп для касс без provider_event_id: по УЖЕ разрешённому payment_id (non-null), не по nullable provider_payment_id:
create unique index if not exists ux_payment_events_fallback
    on billing.payment_events (payment_id, event_type) where provider_event_id is null;
```

**Confirm/Apply — атомарность, классификация переходов и idempotency-ключи ledger** (`payment:{id}` для
зачисления, `refund:{id}` / `chargeback:{id}` для reversal) описаны в авторитетном разделе
**Hardening → Confirm / Refund / payment_events**.

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
| `Services/Billing/BillingProvider.cs` | `+ GetFxQuoteAsync, CreatePaymentAsync, AttachProviderRefAsync, ConfirmPaymentByProviderAsync, ApplyPaymentEventAsync, AcquireStalePaymentsAsync` |

**`IPaymentProvider`:**
```csharp
public interface IPaymentProvider
{
    string Name { get; }                                              // "mock", позже "rollypay"...
    Task<PaymentCreated> CreateAsync(PaymentRequest req, CancellationToken ct);   // order_id=payment_id → ProviderPaymentId + PayUrl
    WebhookVerification Verify(string rawBody, IHeaderDictionary headers);        // подпись/HMAC
    PaymentWebhookEvent Parse(string rawBody);                                    // → providerPaymentId, orderId, eventType, amount, currency, providerEventId
    Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct);  // reconciliation
}
```
`PaymentWebhookProcessor` **в БД сам не пишет**: после `Verify` передаёт распарсенное событие
(+ `raw_payload`, `signature_ok`) в `BillingProvider.ConfirmPaymentByProvider`/`ApplyPaymentEvent`,
где Billing атомарно фиксирует payment_event + переход + ledger.
`PaymentReconciliationWorker` (WebApp) добирает зависшие `Created` через `provider.GetPaymentAsync`.

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

**`PaymentWebhookProcessor`** — switch по `event_type`: `paid` → `ConfirmPaymentByProvider`;
`canceled`/`expired`/`refunded`/`chargeback` → `ApplyPaymentEvent` (с `provider_event_id`+`raw_payload` для аудита);
`created` → no-op. Идемпотентность и аудит — на стороне BillingService (`expired`→`Expired`, не `Canceled`).

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

**Деньги при пополнении (расчёт USD — на стороне Billing):** юзер вводит сумму в `DisplayCurrency`;
WebApp переводит её в `provider_currency` (валюта кассы) для показа/оплаты и шлёт в `CreatePayment`
**только** `provider_amount`+`provider_currency` (+`display_*`). `credited_usd` и `fx_rate` вычисляет и
замораживает **Billing** из своего доверенного FX-quote (см. Hardening → Integrity invariants). ЦБ-курс
WebApp здесь — лишь предпросмотр для юзера, не источник зачисления. Минимум — `PaymentSettings.MinTopUpUsd`.

**Опц. `MarkupPercent`** (наценка/спред к курсу ЦБ, 2–3%) — параметр **Billing-quote** (не WebApp),
применяется при расчёте `credited_usd`. Дефолт `0`, значение — бизнес-решение.

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

> **Поправка по итогам ревью.** Auth-миграций в репо уже **9** (`001…009`) → следующая = **010**
> (не 007). `auth.proto` уже содержит `GetUser`/`UpdateUser` (`UpdateUserRequest` уже несёт
> `display_name`, `max_api_keys`) → **расширяем существующие контракты**, а не вводим параллельные
> `GetUserProfile`/`UpdateUserSettings`.

| Файл | Действие |
|---|---|
| `db/migrations/auth/010_add_user_display_currency.sql` | **NEW.** `alter table auth.users add column if not exists display_currency varchar(8) not null default 'RUB';` |
| `Domain/Entities/UserAccount.cs` | `+ DisplayCurrency` |
| `Infrastructure/Persistence/UserAccountEntity.cs` | `+ display_currency` |
| `Infrastructure/Repository/AuthRepository.cs` + `InMemoryAuthRepository.cs` | select/insert колонки; запись через существующий update-путь |
| `Application/Services/IAuthRepository.cs` | расширить существующий `UpdateUserAsync` (а не новый метод) |
| `Protos/auth.proto` | расширить: `UserInfo + display_currency`, `UpdateUserRequest + optional string display_currency = 4` |
| `Infrastructure/Grpc/GrpcAuthService.cs` | дополнить `GetUser`/`UpdateUser` обработкой `display_currency` |
| WebApp `Services/Auth/AuthProvider.cs` | использовать существующие `GetUser`/`UpdateUser` |

> Дешёвый fallback (если отложить правку AuthService) — хранить выбор в cookie.
> По вводной «нужна вся инфраструктура» → делаем через профиль.

---

## Phase 7 — DI / конфиг / pipeline

**`WebApp/Program.cs`:**
```csharp
builder.Services.Configure<PaymentSettings>(builder.Configuration.GetSection("Payments"));
builder.Services.Configure<ExchangeRateSettings>(builder.Configuration.GetSection("ExchangeRates"));
// ЦБ-FX — ТОЛЬКО для отображения (баланс/цены), НЕ для зачисления:
builder.Services.AddHttpClient<CbrClient>();
builder.Services.AddSingleton<CbrRateSnapshot>();
builder.Services.AddSingleton<IExchangeRateProvider, CbrExchangeRateProvider>();
builder.Services.AddSingleton<CurrencyFormatter>();
builder.Services.AddHostedService<CbrRateRefresher>();
// Платёжные провайдеры: mock — только в Development
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
// реальные провайдеры регистрируются всегда, когда сконфигурированы
builder.Services.AddSingleton<PaymentProviderRegistry>();
builder.Services.AddScoped<PaymentWebhookProcessor>();
builder.Services.AddHostedService<PaymentReconciliationWorker>();   // досведение зависших Created
builder.Services.AddRateLimiter(/* "payment-webhook": fixed window по IP */);

var app = builder.Build();
// Fail-fast: в Production активный провайдер не должен быть mock и обязан существовать
PaymentStartupGuard.Validate(app.Services, app.Environment);
app.UseRateLimiter();                  // ДО маппинга вебхука
PaymentWebhookEndpoint.Map(app);
```

**`appsettings.json` (общий — БЕЗ mock):**
```json
"Payments": { "ActiveProvider": "", "MinTopUpUsd": "1.00" },
"ExchangeRates": {
  "Source": "cbr", "RefreshHourMsk": 12, "MarkupPercent": "0",
  "Fallback": { "RubPerUsd": "95.0", "RubPerEur": "103.0" }
}
```
**`appsettings.Development.json` (мок — только здесь):**
```json
"Payments": { "ActiveProvider": "mock", "Mock": { "SharedSecret": "dev" } }
```
Production: `ActiveProvider` = реальный провайдер; если пусто/`mock` → `PaymentStartupGuard` падает на старте.

> **BillingService DI/Startup МЕНЯЕТСЯ:** + `FxQuoteService` (свой `CbrClient` + дневной refresh + fallback),
> + `PaymentEventRecoveryWorker` (hosted), + **s2s-auth интерсептор на ВСЕХ ledger-мутирующих gRPC** —
> `CreateVirtualTopUp`, `RecordUsage` **и** payment-методы (см. Hardening), не только payment.

---

## Phase 8 — Тесты

**BillingService.Tests** (PG + in-memory, паттерн уже есть):
- create → confirm → баланс вырос на `credited_usd_amount` (его посчитал Billing, не caller);
- **повторный confirm = no-op** (идемпотентность по `provider_payment_id` и по ledger idempotency_key);
- `ExternalTopUp` виден в `ListTopUps`;
- cancel из `Created` → `Canceled`; из `Paid` → **ошибка `PaymentInvalidTransition`** (НЕ no-op);
- `credited_usd` НЕ принимается от caller (Billing считает сам из FX-quote).

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

**Обязательные тесты до реализации (из ревью):**
- параллельные `confirm × confirm` и `confirm × cancel` (атомарность условного перехода);
- несовпадение webhook `amount`/`currency`/`order_id` с ожидаемым → отказ зачисления + anomaly;
- webhook **до** привязки `provider_payment_id` (поиск по `order_id`);
- сбой BillingService после успешного create у провайдера → платёж не теряется (reconciliation/lookup);
- `refund` → авто-reversal (баланс откатывается, `refund:{id}`);
- `chargeback` дефолт **notify-only**: баланс НЕ меняется + админ уведомлён; с конфигом auto-reversal → откат (`chargeback:{id}`);
- `expired`-событие → статус `Expired` (не `Canceled`); каждое provider-событие создаёт `payment_event` (аудит);
- durable inbox: сбой tx2 → `processed=false` → recovery-worker дозавершает идемпотентно;
- reconciliation после пропущенного webhook (`AcquireStalePayments` + `provider.GetPayment`);
- `MockPaymentProvider` **не регистрируется вне Development** + `PaymentStartupGuard` fail-fast в prod.

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

## File inventory (≈40 файлов)

- **Contracts:** `billing.proto`, `auth.proto`.
- **BuildingBlocks:** `ErrorCodes.cs`.
- **BillingService (~15):** миграция `013` (payments + payment_events), `PaymentStatus`, `LedgerEntryType`,
  `Payment`, `PaymentEvent`, `PaymentEntity`, `BillingPersistenceMapper`, `FxQuoteService`, `IBillingRepository`,
  `BillingRepository`, `InMemoryBillingRepository`, `BillingApplicationService`, `GrpcBillingService`,
  `Workers/PaymentEventRecoveryWorker`, s2s-auth интерсептор (`Startup`).
- **WebApp (~18):** `Services/Payments/*` (incl. `PaymentStartupGuard`, `PaymentReconciliationWorker`),
  `Api/PaymentWebhookEndpoint`, `Services/Billing/BillingProvider`, `Services/Currency/*` (8),
  `Pages/Workspace/Credits*`, `Pages/Pay/Mock*`, `Pages/Workspace/PaymentSuccess`,
  `Pages/Workspace/Settings*`, `Program.cs`, `appsettings.json` + `appsettings.Development.json`.
- **AuthService (~7):** миграция `010`, `UserAccount`, `UserAccountEntity`, `AuthRepository`,
  `InMemoryAuthRepository`, `IAuthRepository`, `GrpcAuthService` (расширение `GetUser`/`UpdateUser`).
- **Tests:** BillingService payment/idempotency/transition/inbox tests, WebApp mock/webhook/FX tests, HMAC golden каркас.
