# Дизайн-документ: Админские уведомления в Telegram (Пополнения, Первый API-запрос, Утренняя сводка)

- **Дата:** 2026-09-08
- **Статус:** В реализации
- **Связанные задачи:** BOT-001, BOT-003, BOT-004

## 1. Цели и назначение

Расширить функционал внутреннего Telegram-бота платформы (`rawrter-tg-bot`) тремя ключевыми возможностями:
1. **Успешное пополнение баланса (`topup_completed`):** мгновенное уведомление в чат команды о поступившей оплате (email пользователя, сумма в рублях и зачисленные USD, платёжный провайдер).
2. **Первый боевой API-запрос (`first_api_request`):** мгновенное уведомление о первой активации пользователя (email пользователя, запрошенная модель).
3. **Утренняя сводка в 10:00 UTC+3 (`daily_summary`):** ежедневный дайджест за последние 24 часа:
   - Количество новых регистраций;
   - Количество и сумма успешных пополнений (в рублях и в долларах);
   - Количество уникальных активных пользователей (посетителей по продуктовой аналитике).

---

## 2. Архитектура и контракты данных

### 2.1. Контракт событий бота (`rawrter-tg-bot`)

#### Маршруты:
- `POST /v1/events/topup` — уведомление об успешном пополнении.
- `POST /v1/events/first-api-request` — уведомление о первом API-запросе.
- `GET /internal/bot/daily-summary` (на стороне WebApp) — опрос агрегированной статистики за 24ч для утреннего дайджеста.

Заголовок авторизации: `Authorization: Bearer <INBOUND_AUTH_TOKEN>`.

#### Форматы событий (JSON, camelCase):

1. **Top-up (`topup_completed`):**
```json
{
  "id": "pay-uuid-or-order-id",
  "type": "topup_completed",
  "occurredAt": "2026-09-08T10:00:00Z",
  "payload": {
    "email": "user@example.com",
    "amountRub": 1500.00,
    "creditedUsd": 16.25,
    "provider": "Rollypay (СБП)"
  }
}
```

2. **First API Request (`first_api_request`):**
```json
{
  "id": "req-uuid-or-user-id",
  "type": "first_api_request",
  "occurredAt": "2026-09-08T10:00:00Z",
  "payload": {
    "email": "user@example.com",
    "model": "anthropic/claude-3-5-sonnet"
  }
}
```

3. **Daily Summary (ответ WebApp на запрос бота):**
```json
{
  "periodHours": 24,
  "newRegistrations": 12,
  "topupsCount": 5,
  "topupsTotalRub": 7500.00,
  "topupsTotalUsd": 81.25,
  "uniqueUsers": 84
}
```

---

## 3. Форматирование сообщений в Telegram

### 3.1. Успешное пополнение
```text
💳 Успешное пополнение баланса
Пользователь: user@example.com
Сумма: 1 500 ₽ ($16.25)
Провайдер: Rollypay (СБП)
```

### 3.2. Первый боевой API-запрос
```text
⚡️ Первый API-запрос пользователя
Пользователь: user@example.com
Модель: anthropic/claude-3-5-sonnet
```

### 3.3. Ежедневная утренняя сводка (10:00 UTC+3)
```text
📊 Утренняя сводка Rawrter
Период: за последние 24 часа

👥 Новых регистраций: 12
💳 Пополнений: 5 на сумму 7 500 ₽ ($81.25)
🌐 Уникальных пользователей: 84
```

---

## 4. Реализация компонентов

### 4.1. Сервис бота (`/opt/rawrter-tg-bot`)
- **`src/events.ts`:**
  - Определение интерфейсов `TopUpCompletedEvent`, `FirstApiRequestEvent`.
  - Валидация типов событий и полей payload.
  - Расширение `BotEvent = SupportTicketEvent | UserRegisteredEvent | TopUpCompletedEvent | FirstApiRequestEvent`.
- **`src/format.ts`:**
  - Добавление `formatTopUpNotification`, `formatFirstApiRequestNotification`, `formatDailySummaryMessage`.
  - Обновление `formatEventNotification` для поддержки новых типов.
- **`src/server.ts`:**
  - Обработчики `POST /v1/events/topup` и `POST /v1/events/first-api-request` (с дедупликацией по ID).
- **`src/heartbeat.ts`:**
  - При наступлении 10:00 UTC+3 бот вызывает `GET http://webapp:8080/internal/bot/daily-summary`.
  - При успешном ответе формирует и отправляет сообщение `formatDailySummaryMessage` вместо технического «Бот жив». При ошибке запроса — отправляет fallback-сигнал «Бот жив» с техническими счетчиками.

### 4.2. WebApp (`OpenRussiaRouter.WebApp`)
- **Уведомление об оплате (`topup_completed`):**
  - В `PaymentWebhookProcessor`: при успешном `result.Confirmed` резолвит `PaymentInfo` и `email` через `AuthProvider.GetUserAsync(payment.UserId)`.
  - Вызывает `ISupportBotNotifier.NotifyTopUpCompleted(...)`.
  - Постановка в неблокирующую очередь `SupportBotEventQueue`.
- **Эндпоинт утренней сводки (`GET /internal/bot/daily-summary`):**
  - Защищён проверкой Bearer-токена `Support:Bot:AuthToken`.
  - Агрегирует:
    1. Новых пользователей за 24ч через `_authClient.ListUsersAsync` или запрос к `auth.users`.
    2. Успешные пополнения за 24ч через `_billing.ListAllTopUpsAsync`.
    3. Уникальные посетители/активные пользователи за 24ч через `SELECT count(distinct distinct_id) FROM analytics.events WHERE occurred_at_utc >= now() - interval '24 hours'`.

### 4.3. BillingService (`OpenRussiaRouter.BillingService`)
- **Уведомление о первом API-запросе (`first_api_request`):**
  - При `RecordUsageWithTransactionAsync`, когда вычисляется `isFirstUsage == true`:
    - Фиксируется факт первого запроса пользователя.
    - Асинхронно через HTTP клиент отправляет событие боту на `http://rawrter-tg-bot:8080/v1/events/first-api-request`.
    - Резолвинг email пользователя: через gRPC `AuthService.GetUser` (подключается stub `auth.proto` в BillingService).
    - Ошибки отправки уведомления или недоступность бота не влияют на списание средств и обработку запроса (fail-soft).
