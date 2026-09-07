# Дизайн-документ: Уведомления о регистрации пользователей в Telegram-бот

- **Дата:** 2026-09-07
- **Статус:** Утверждён
- **Связанные задачи:** BOT-001 (Telegram-бот поддержки Rawrter), BOT-003

## 1. Цель и контекст

При регистрации нового пользователя на платформе Rawrter (как при подтверждении email после обычной регистрации, так и при первой регистрации через Google/Yandex OAuth) отправлять краткое информационное уведомление в закрытую рабочую группу Telegram через внутренний сервис бота `rawrter-tg-bot`.

## 2. Архитектура решения

Связка состоит из двух компонентов:
1. **WebApp (`OpenRussiaRouter`):**
   - Источник событий регистрации.
   - Использует существующий неблокирующий фоновый конвейер (`ISupportBotNotifier`, `SupportBotEventQueue`, `SupportBotEventWorker`) в режиме fail-soft: отправка события выполняется асинхронно, с ограниченной in-memory очередью, без влияния на время ответа пользователю и работу сессий/балансов.
   - При сбоях сети/бота выполняется ограниченный retry (до 4 попыток с таймаутом).
2. **Telegram-бот (`rawrter-tg-bot` на сервере):**
   - TypeScript/Node.js сервис, принимающий события по закрытому HTTP API с Bearer-авторизацией (`INBOUND_AUTH_TOKEN`).
   - Валидирует входящие данные, дедуплицирует повторные попытки по `id` (GUID) в памяти.
   - Форматирует сообщение и отправляет его в Telegram через Bot API.

## 3. Контракт HTTP API бота

### Маршрут
`POST /v1/events/registration` (также поддерживается общий приём в `/v1/events/support`)
Заголовок: `Authorization: Bearer <Support:Bot:AuthToken>`
Content-Type: `application/json`

### Формат события
```json
{
  "id": "6ba7b8109dad11d180b400c04fd430c8",
  "type": "user_registered",
  "occurredAt": "2026-09-07T18:00:00.0000000Z",
  "payload": {
    "email": "user@example.com"
  }
}
```

### Ответы бота
- `202 Accepted` (`{"status":"queued"}`): принято в очередь на отправку.
- `200 OK` (`{"status":"duplicate"}`): событие с таким `id` уже было обработано (терминальный успех, retry не требуется).
- `400 Bad Request` (`{"error":"invalid_event","fields":[...]}`): ошибка валидации контракта (перманентный отказ, без retry).
- `401 Unauthorized`: неверный или отсутствующий токен авторизации.
- `503 Service Unavailable` (`{"status":"queue_full"}` / `bot_misconfigured` / `notifications_disabled`): повторить позже.

## 4. Формат сообщения в Telegram

Обычный текст без parse_mode:
```text
🎉 Новая регистрация
Email: user@example.com
```

## 5. Детали реализации по проектам

### 5.1. Сервис бота (`rawrter-tg-bot` на сервере в `/opt/rawrter-tg-bot`)
1. **`src/events.ts`:**
   - Добавить интерфейс `UserRegisteredPayload { email: string }`.
   - Добавить интерфейс `UserRegisteredEvent`.
   - Добавить объединение `BotEvent = SupportTicketEvent | UserRegisteredEvent`.
   - Функция `parseUserRegisteredEvent(input: unknown): ParseEventResult`.
2. **`src/format.ts`:**
   - Функция `formatRegistrationNotification(event: UserRegisteredEvent): string`.
   - Единая функция диспетчеризации `formatEventNotification(event: BotEvent): string`.
3. **`src/queue.ts`:**
   - Перевод элементов очереди с `SupportTicketEvent` на `BotEvent`.
4. **`src/server.ts`:**
   - Регистрация маршрута `POST /v1/events/registration`.
   - Приём и валидация события `user_registered`, дедупликация и постановка в очередь.
5. **`src/index.ts`:**
   - Отправка сообщений из очереди через `formatEventNotification`.
6. **Тесты:**
   - Тесты валидации, форматирования и HTTP API роута регистрации в `test/`.

### 5.2. WebApp (`OpenRussiaRouter.WebApp`)
1. **`SupportBotEvent.cs`:**
   - Поддержка типа `EventType` (`"support_ticket"` и `"user_registered"`).
   - Поля: `Id`, `OccurredAt`, `Type`, `Email`, `Subject?`, `Message?`.
2. **`ISupportBotNotifier.cs` и `SupportBotNotifier.cs`:**
   - Добавление метода: `void NotifyUserRegistered(string email)`.
   - Проверка флага включения (`SupportBotSettings.Enabled`) и корректности email.
   - Постановка события в очередь `SupportBotEventQueue`.
3. **`SupportBotEventWorker.cs`:**
   - Построение URL эндпоинта: для `user_registered` используется путь `/v1/events/registration` (вычисляется из базового адреса или замены `/support` на `/registration`).
   - Сериализация тела с учётом типа события.
   - Повторы при 503 и сбоях сети до `MaxAttempts` (4 попытки).
4. **Интеграция точек регистрации:**
   - `Pages/VerifyEmail.cshtml.cs`: вызов `_botNotifier.NotifyUserRegistered(Email)` сразу после успешного `_authClient.VerifyEmailAsync(Email, Code)`.
   - `Pages/Login/ExternalCallback.cshtml.cs`: внедрение `ISupportBotNotifier` и вызов `_botNotifier.NotifyUserRegistered(loginResult.Email)` внутри блока `if (loginResult.IsNewUser)`.
5. **Тестирование:**
   - Юнит-тесты `SupportBotNotifierTests`: проверка энкьювинга события регистрации.
   - Юнит-тесты `SupportBotEventWorkerTests`: проверка корректной сериализации и маршрутизации на `/v1/events/registration`.
   - Юнит-тесты `VerifyEmailModelTests` и `ExternalCallbackModelTests`.

## 6. Безопасность и отказоустойчивость

- **Безопасность:** В событии передаётся исключительно подтверждённый email пользователя. Пароли, хеши, токены сессий и IP-адреса не передаются.
- **Fail-soft:** Сбой или недоступность бота ни при каких условиях не ломает авторизацию, регистрацию или начисление стартового промо-баланса пользователю.
- **Deduplication:** Стабильный ID события предотвращает повторные отправки при сетевых ретраях.
