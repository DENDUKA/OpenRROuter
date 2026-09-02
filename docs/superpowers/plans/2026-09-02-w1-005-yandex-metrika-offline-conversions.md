# W1-005 — офлайн-конверсии Яндекс Метрики: Implementation Plan

> **For agentic workers:** выполнять план последовательно, отмечая шаги через `- [ ]`. Перед изменением каждого сервиса обязательно прочитать его `CLAUDE.md`.

**Goal:** надёжно отправлять серверные цели `topup_completed` и `first_api_request` в Яндекс Метрику без потери событий и без двойного учёта конверсий.

**Architecture:** связка `user_id → ym_client_id` хранится в analytics-БД и записывается WebApp. Бизнес-события создаются в transactional outbox BillingService в одной транзакции с оплатой или первой записью usage; worker позднее разрешает ClientID через analytics-БД и отправляет событие через Offline Conversions API.

**Tech Stack:** .NET 8, ASP.NET Core, BackgroundService, Dapper, Npgsql, PostgreSQL, xUnit, FluentAssertions, Moq, Testcontainers, Яндекс Метрика Management API.

## Global Constraints

- Все асинхронные методы имеют суффикс `Async`; ожидание выполняется только через `await`, без `GetAwaiter()`.
- BillingService остаётся единственным владельцем фактов оплаты и usage.
- Распределённой транзакции между billing- и analytics-БД нет.
- Недоступность analytics-БД не откатывает платёж, usage или создание outbox-события.
- Реализуются только новые события после включения функции; backfill не выполняется.
- Изменения деплоя ограничены production compose; Kubernetes не входит в W1-005.
- OAuth-токен хранится только в gitignored `.env` и не попадает в логи.

---

## Task 1: Хранение ClientID в analytics-БД

**Files:**
- Create: `db/migrations/analytics/005_yandex_metrika_identities.sql`
- Modify: `src/OpenRussiaRouter.BuildingBlocks/Analytics/`
- Modify: тесты BuildingBlocks и PostgreSQL-интеграционные тесты аналитики

**Interfaces:**
- `IYandexMetrikaIdentityStore.UpsertAsync(userId, clientId, observedAtUtc, cancellationToken)`
- `IYandexMetrikaIdentityStore.GetClientIdAsync(userId, cancellationToken)`
- `IProductAnalyticsErasure.PurgeUserAsync` удаляет также ClientID пользователя.

- [ ] Создать таблицу `analytics.yandex_metrika_identities` с `user_id` как первичным ключом, полями `client_id`, `first_seen_at_utc`, `last_seen_at_utc` и last-write-wins обновлением.
- [ ] Написать тесты на вставку, чтение, повторный upsert и сохранение исходного `first_seen_at_utc`.
- [ ] Добавить PostgreSQL-реализацию хранилища через `ProductAnalytics:ConnectionString`.
- [ ] Добавить no-op реализацию для отключённой продуктовой аналитики.
- [ ] Расширить erasure пользователя удалением записи из `analytics.yandex_metrika_identities`.
- [ ] Проверить миграционные и интеграционные тесты.

## Task 2: Захват `_ym_uid` в WebApp

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/` — middleware/регистрация аналитики и конфигурация
- Modify: тесты WebApp

**Behavior:**
- Сохранять ClientID только для авторизованного пользователя при `orr_consent=1`.
- Принимать `_ym_uid` только по выражению `^[0-9]{1,32}$`.
- Успешно сохранённую пару `user_id + client_id` кэшировать на один час.
- Работать fail-open: ошибка analytics-БД не влияет на HTTP-ответ.
- Не писать полный ClientID в журналы.

- [ ] Написать тесты для отсутствующей авторизации, отсутствующего согласия, невалидного cookie, валидного cookie, кэша и отказа БД.
- [ ] Добавить middleware после аутентификации и зарегистрировать `IYandexMetrikaIdentityStore`.
- [ ] Проверить, что WebApp пишет привязку напрямую в analytics-БД и не создаёт новый межсервисный RPC.
- [ ] Запустить тесты WebApp.

## Task 3: Transactional outbox в BillingService

**Files:**
- Create: `db/migrations/billing/031_yandex_metrika_offline_conversion_outbox.sql`
- Modify: `src/OpenRussiaRouter.BillingService/` — репозитории оплаты, usage и offline conversion outbox
- Modify: тесты BillingService

**Data contract:**
- Поля outbox: `id`, уникальный `dedup_key`, `user_id`, `target`, `occurred_at_utc`, необязательные `price`/`currency`, `resolved_client_id`, `status`, lease, `attempts`, `next_attempt_at_utc`, upload id/status, детерминированный comment и `last_error`.
- Статусы: `Pending`, `Reconciling`, `Uploaded`, `Processed`, `Failed`, `Expired`.
- Отдельный серверный реестр целей: `YandexMetrikaOfflineConversionTargets`.

- [ ] Написать миграционные тесты для схемы, статусов и уникальности `dedup_key`.
- [ ] При первом атомарном переходе платежа `Created → Paid` создавать `topup_completed:{payment_id}` в той же транзакции.
- [ ] В событии оплаты сохранять `ProviderAmount`, `ProviderCurrency` и время подтверждения.
- [ ] При создании первой строки пользователя в `billing.usage_records` создавать `first_api_request:{user_id}` в той же транзакции.
- [ ] Не создавать `first_api_request`, если строка `usage_records` не была создана, включая нулевую или неизвестную стоимость.
- [ ] Проверить идемпотентность повторного webhook/reconciliation и конкурентных записей usage.

## Task 4: Worker отправки и reconciliation

**Files:**
- Modify: `src/OpenRussiaRouter.BillingService/` — worker, клиент Метрики, options, DI и метрики
- Modify: тесты BillingService

**Configuration:**
- Секция `YandexMetrika:OfflineConversions`: `Enabled`, `CounterId`, `OAuthToken`, `BaseUrl`, интервалы worker/lease/retry.
- При `Enabled=true` обязательны `CounterId`, `OAuthToken` и `ProductAnalytics:ConnectionString`.
- Production counter: `111582266`.

- [ ] Написать тесты на lease, ожидание отсутствующего ClientID, сохранение снимка ClientID, retry и expiry.
- [ ] Реализовать `YandexMetrikaOfflineConversionWorker` на `BackgroundService`, `async/await` и `PeriodicTimer`.
- [ ] После получения ClientID сохранить его в outbox как неизменяемый снимок; последующие изменения identity не должны менять событие.
- [ ] При отсутствии ClientID отложить событие; через 20 дней перевести его в `Expired`.
- [ ] Формировать UTF-8 CSV с `ClientId`, `Target`, `DateTime`, а для оплаты также `Price` и `Currency`.
- [ ] Отправлять одно событие в одной multipart-загрузке с комментарием `orr:<event_id>`.
- [ ] После таймаута или `5xx` сначала искать существующую загрузку по комментарию, а не отправлять событие повторно вслепую.
- [ ] Отслеживать upload до `PROCESSED`; `LINKAGE_FAILURE` и расхождение числа строк переводить в `Failed`.
- [ ] Для `429` и временных ошибок применять backoff и `Retry-After`; `400` считать терминальной ошибкой события; `401/403` считать ошибкой конфигурации.
- [ ] Добавить метрики ограниченной кардинальности только по `target` и `status`.

## Task 5: Цели Метрики, клиентская дедупликация и production rollout

**Files:**
- Modify: WebApp-код клиентских analytics goals
- Modify: production compose и пример конфигурации без секрета
- Modify: корневой `CLAUDE.md`, `CLAUDE.md` WebApp/BillingService/BuildingBlocks и runbook Метрики

- [ ] В Метрике оставить точную цель `topup_completed` с ID `597470019`.
- [ ] Перенести зависимости Яндекс Директа с дублирующей цели `604447478`, затем удалить дубликат.
- [ ] Создать точную JavaScript-событийную цель `first_api_request`.
- [ ] Создать OAuth-токен со scope `metrika:offline_data` и поместить его в production `.env`.
- [ ] Добавить `Analytics:YandexTopUpCompletedClientEnabled`, отдельно управляющий только вызовом `ym(..., reachGoal, "topup_completed")`; GA4-событие сохранить.
- [ ] Развернуть миграции и worker с `Enabled=false`.
- [ ] Включить worker, временно сохранив клиентскую отправку `topup_completed`, и проверить `first_api_request` на новом пользователе.
- [ ] После успешного smoke-теста отключить клиентскую отправку `topup_completed` и выполнить тестовое пополнение.
- [ ] Проверить в отчёте офлайн-конверсий правильный ClientID, одно достижение, сумму и RUB; учитывать обработку до трёх часов.

## Test Plan

- WebApp: consent/auth/cookie validation, часовой кэш, fail-open.
- BuildingBlocks: upsert/read, last-write-wins, no-op, erasure.
- Billing PostgreSQL: атомарность и идемпотентность оплаты/usage, независимость бизнес-транзакции от analytics-БД.
- Worker: missing identity, immutable snapshot, CSV/OAuth, upload status, reconciliation, retry, terminal errors, expiry.
- Регрессия: GA4 `topup_completed` продолжает отправляться после отключения клиентской цели Метрики.

Финальные проверки:

```powershell
rtk slopwatch analyze --fail-on warning
rtk dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
rtk dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

## Acceptance Criteria

- Новая подтверждённая оплата даёт ровно одно серверное `topup_completed` с правильными суммой и валютой.
- Первая реально записанная строка usage пользователя даёт ровно одно `first_api_request`.
- Событие переживает отсутствие ClientID и временные ошибки Метрики без потери и неконтролируемого дубля.
- После переключения `topup_completed` не отправляется в Метрику из браузера, но продолжает отправляться в GA4.
- В репозитории нет OAuth-токена, полного ClientID в логах и Kubernetes-изменений.
