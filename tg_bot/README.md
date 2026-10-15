# Rawrter · Telegram Bot

Актуальный код бота перенесён из `/opt/rawrter-tg-bot` в `OpenRussiaRouter/tg_bot` по разрешению владельца. Исходный отдельный локальный репозиторий не изменён. Бот остаётся отдельным TypeScript/Node.js 24/Fastify контейнером, вне .NET solution.

## Подтверждённое состояние

При переносе сборка всех 10 `dist/*.js` побайтно совпала (SHA-256) с файлами работающего контейнера. Production image на момент сверки: `sha256:6ebc3ca5799ea2d21a527fa3d72821e75a8d01b0d982e23f44ed81f67dadd40c`. Исходники `src/` сохранены без изменения поведения. Серверный `.env` не копировался, сервер не изменялся.

- `POST /v1/events/support`: `support_ticket`, payload `{email, subject, message}`.
- `POST /v1/events/registration`: `user_registered`, payload `{email}`.
- `POST /v1/events/topup`: `topup_completed`, payload `{email, amountRub, creditedUsd, provider}`.
- `POST /v1/events/first-api-request`: `first_api_request`, payload `{email, model}`.
- `POST /v1/events/payment-created`: `payment_created`, payload `{email, amountRub, creditedUsd, provider}`.
- Все события: `{id, type, occurredAt, payload}`, `Authorization: Bearer <INBOUND_AUTH_TOKEN>`. Ответы: 202 принято в память, 200 дубль, 400 невалидный вход, 401 отказ доступа, 413 большой запрос, 503 выключено/конфигурация/переполнение. Получатель фиксирован конфигурацией.
- `GET /healthz`: безопасная локальная диагностика. HTTP 200 может содержать `status: degraded`; Compose проверяет именно `status: ok`.
- В 10:00 UTC+3 (07:00 UTC) бот читает `GET <WEBAPP_BASE_URL>/internal/bot/daily-summary` с Bearer-аутентификацией, таймаут 10 с. Успех → бизнес-сводка за последние 24 часа, недоступность → технический «Бот жив». После рестарта в интервале 07:00–24:00 UTC — догоняющая отправка через 5 с.
- Прямого подключения к БД, Transactional Outbox, Telegram-команд, топиков, API Директа/Метрики и ответов клиентам нет. Не считать HTTP endpoints командами Telegram.

## Проверки

```bash
cd tg_bot
npm ci --ignore-scripts
npm run typecheck
npm test
npm run build
bash test-deploy.sh
```

80 тестов Vitest: серверные новые сценарии плюс восстановленные старые проверки Support, общая матрица четырёх маршрутов, сводка и fallback. 4 изолированных shell-сценария обновления: успех, отказ сборки, нездоровый контейнер, отказ запуска; ошибки запуска/health проверяют откат образа. Внешние сервисы подменены.

При переносе также прошли .NET solution build (0 предупреждений), 41 релевантный тест WebApp, Docker build и запуск образа с `--network none`/синтетическими настройками и проверкой `/healthz`. `npm audit --omit=dev`: 0; полный audit: 2 moderate в Vitest/его mocker, исправление требует отдельного обновления major (не выполнено при переносе).

## Конфигурация и ограничения

Обязательные секреты/настройки: `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`, `INBOUND_AUTH_TOKEN`. Дополнительные: `WEBAPP_BASE_URL` (по умолчанию `http://webapp:8080`), `TELEGRAM_API_BASE_URL` (по умолчанию `https://api.telegram.org`; на проде — обратный прокси во Франкфурте, потому что из РФ api.telegram.org режется по SNI), `PORT` (8080), `SUPPORT_NOTIFY_ENABLED`, `HEARTBEAT_ENABLED` (оба true), `QUEUE_MAX_ITEMS` (100), `DEDUP_MEMORY` (1000).

- Отдельную БД для бота не создаём. Будущий доступ к существующей `openrussiarouter` — только чтение по отдельной задаче; сейчас данные поступают через HTTP WebApp.
- Очередь, дедупликация и флаг утреннего отчёта — в памяти. Рестарт теряет незавершённые задания и может повторить отчёт. Чтение бизнес-данных из БД НЕ восстанавливает историю доставки и НЕ заменяет outbox.
- `unknown` после сетевого таймаута не повторяется: доставка могла состояться. Нельзя объявлять это доказанным недоставленным сообщением.
- Сохранённая особенность серверной версии: `supportSentToday`/`lastSupportSentAt` учитывают ВСЕ четыре типа событий, хотя технический отчёт подписывает их как обращения. Счётчики обнуляются в 00:00 UTC и при рестарте; время последнего уведомления сохраняется между сутками, но не рестартами. Нужна отдельная корректировка семантики.
- Успешный health не доказывает доступность WebApp/Telegram. Сводка доверяет JSON WebApp без runtime-валидации; это известный пробел, не исправлявшийся переносом.
- BOT-002/BOT-003 не закрыты этим переносом: устойчивость финансовых уведомлений не реализована.

## GitHub и обновление production

`.github/workflows/tg-bot.yml` запускает проверки и сборку Docker на PR, а на push в main/ручном запуске после проверок публикует `ghcr.io/denduka/rawrter-tg-bot:{sha-<commit>,dev-latest}`. Сам push НЕ выполняет production deploy.

Существующий `update-prod.sh` собирает основной сервис из Git **на сервере**, не скачивает GHCR. Этот подход сохранён. После успешного health-gate основного приложения он вызывает `app/deploy/compose/update-tg-bot.sh`: сборка из `app/tg_bot`, обновление отдельного Compose-проекта `rawrter-tg-bot`, ожидание health до 90 с, откат на предыдущий image ID при ошибке. При ошибке бота основной сервис остаётся на обновлённой версии, команда завершается ошибкой. Начальный deploy при отсутствии существующего контейнера запрещён этим скриптом.

Имена проекта/контейнера, сети default + `orr_default`, лимиты и серверный `extra_hosts` для Telegram сохранены. IP override — существующий workaround, не гарантия надёжности. `TELEGRAM_API_IP` позволяет изменить его отдельно. Production secrets читаются из существующего `/opt/rawrter-tg-bot/.env` (переопределение `TG_BOT_ENV_FILE`). Старую папку с секретами не удалять; она больше не источник сборки.

### Одноразовое подключение — только после разрешённого push и разрешения владельца на изменения сервера

На сервере `/opt/orr/update-prod.sh` — самостоятельная старая копия: Git pull внутри неё НЕ обновляет сам скрипт. Поэтому первый раз нужны:

```bash
cd /opt/orr
git -C app pull --ff-only
cp -p update-prod.sh update-prod.sh.before-tg-bot
install -m 755 app/deploy/compose/update-prod.sh update-prod.sh
```

После этого для последующих обновлений основного приложения и бота достаточно:

```bash
cd /opt/orr
bash update-prod.sh
```

Не запускать параллельно несколько обновлений. Legacy workflow `deploy-prod.yml` использует другой скрипт `update-orr.sh` и в этот путь не переведён: для совместного обновления использовать именно `update-prod.sh`.

Эти серверные действия, GitHub run/publish и внешняя доставка после миграции ещё НЕ выполнялись. Статус: **implementation complete / external verification pending**.
