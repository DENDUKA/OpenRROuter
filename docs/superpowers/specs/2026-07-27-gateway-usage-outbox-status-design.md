# T-005: статус usage_missing в Gateway Usage Outbox

## Цель

Синхронизировать PostgreSQL-схему `gateway.usage_outbox` с поведением ApiGateway, чтобы запросы с ответом без usage сохранялись в outbox со статусом `usage_missing`, а не терялись после ошибки `CHECK`-констрейнта.

## Решение

Добавить новую SQL-миграцию `db/migrations/gateway/003_usage_outbox_allow_usage_missing.sql`. Она заменит `gateway.ck_usage_outbox_status` так, чтобы допустимыми оставались существующие значения `pending`, `processing`, `completed`, `failed` и добавлялся `usage_missing`.

Существующую миграцию `002_usage_outbox_status_check.sql` не изменять: она могла быть применена в развёрнутых окружениях. Обработчики и диспетчер outbox уже используют `usage_missing`; изменения C#-кода не нужны.

## Поток данных и ошибки

`ChatCompletionHandler` и `TextToSpeechHandler` записывают `usage_missing`, когда upstream-ответ не содержит учитываемых токенов. После миграции `PostgresUsageOutboxRepository.EnqueueAsync` успешно сохраняет такую запись. Фоновый `UsageOutboxDispatcher` продолжает доставлять событие по существующему контракту; клиентский HTTP-ответ не зависит от ошибки постановки в очередь.

## Проверка

Добавить регрессионный тест репозитория ApiGateway, который применяет миграции к тестовой PostgreSQL-базе и вызывает `EnqueueAsync` с `GatewayUsageEvent.Status = "usage_missing"`. Тест должен подтвердить, что запись сохранена и возвращается при выборке pending-событий. Текущие статусы должны оставаться допустимыми.

## Вне области

Не менять семантику `usage_missing`, механизм повторной доставки, модель биллинга, существующие миграции и публичные HTTP/gRPC-контракты.
