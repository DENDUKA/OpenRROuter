# OpenRussiaRouter — мануал

## Что это вообще такое

OpenRussiaRouter — это B2B-платформа для доступа к LLM (ChatGPT и аналогам) через **OpenAI-совместимый API**, но с расчётами в **рублях** и собственным учётом токенов. По смыслу: «свой OpenAI с биллингом в рублях», который под капотом ходит во внешние LLM через пул прокси-нод (9Router) и считает usage.

Зачем оно нужно:

- Клиент пользуется привычным `/v1/chat/completions` (как у OpenAI) — переписывать SDK не надо.
- Биллинг в рублях, баланс prepaid, тарифы по моделям и типам токенов (input / output / cache).
- Доступ по invite от админа, никакой публичной регистрации.
- Запросы к внешним LLM идут через свои 9Router-ноды (внешние сервера + VPN), а не напрямую.
- Есть кабинет пользователя и админка (Blazor).

Текущее состояние — **MVP в работе**. Готовы каркас сервисов, контракты, базовые потоки auth / billing / routing. Полный статус задач лежит в [TODO.md](TODO.md), продуктовое описание — в [docs/prd/open-russia-router-prd.md](docs/prd/open-russia-router-prd.md).

## Из чего состоит проект

Решение — `OpenRussiaRouter.sln`, .NET 8, микросервисы общаются по **gRPC**, у каждого своя БД на **PostgreSQL** (через Dapper, без Entity Framework).

| Сервис | Папка | Что делает |
|---|---|---|
| **API Gateway** | [src/OpenRussiaRouter.ApiGateway](src/OpenRussiaRouter.ApiGateway) | Публичная точка входа, OpenAI-совместимый `/v1/*`. Проверяет ключ, баланс, выбирает 9Router-ноду, проксирует запрос, фиксирует usage. |
| **Auth Service** | [src/OpenRussiaRouter.AuthService](src/OpenRussiaRouter.AuthService) | Пользователи, invites, логин по email/паролю, сессии, API-ключи. |
| **Billing Service** | [src/OpenRussiaRouter.BillingService](src/OpenRussiaRouter.BillingService) | Баланс в рублях, ledger, тарифы моделей, события usage, виртуальные пополнения. |
| **Control Plane** | [src/OpenRussiaRouter.ControlPlane](src/OpenRussiaRouter.ControlPlane) | Реестр 9Router-нод, health-чеки, выбор ноды, lease/release. |
| **WebApp** | [src/OpenRussiaRouter.WebApp](src/OpenRussiaRouter.WebApp) | Blazor: кабинет пользователя и админ-дашборд. |
| **BuildingBlocks** | [src/OpenRussiaRouter.BuildingBlocks](src/OpenRussiaRouter.BuildingBlocks) | Общие примитивы: ошибки, деньги (`MoneyAmount`), usage, correlation id, health. |
| **Contracts** | [src/OpenRussiaRouter.Contracts](src/OpenRussiaRouter.Contracts) | `.proto`-файлы и внутренние DTO. |
| **9Router** | [src/OpenRussiaRouter.9Router](src/OpenRussiaRouter.9Router) | **Внешний fork** (Next.js), сюда не трогаем .NET-кодом. У него свой [AGENTS.md](src/OpenRussiaRouter.9Router/AGENTS.md). |

Миграции БД — обычные SQL-скрипты:

- [db/migrations/auth/001_initial.sql](db/migrations/auth/001_initial.sql)
- [db/migrations/billing/001_initial.sql](db/migrations/billing/001_initial.sql)
- [db/migrations/control/001_initial.sql](db/migrations/control/001_initial.sql)

## Как один запрос проходит насквозь

1. Клиент шлёт `POST /v1/chat/completions` с `Authorization: Bearer <ваш-API-ключ>` в **API Gateway**.
2. Gateway по gRPC спрашивает **Auth**: «этот ключ валиден?» — получает `userId`.
3. Gateway спрашивает **Billing**: «у этого юзера есть баланс и эта модель включена?» — получает разрешение и версию тарифа.
4. Gateway спрашивает **Control Plane**: «дай мне здоровую 9Router-ноду под эту модель» — получает `leaseId`, endpoint, токен.
5. Gateway отправляет HTTPS-запрос на 9Router-ноду, та идёт во внешний LLM через VPN, возвращает ответ.
6. Gateway пишет в **Billing** `RecordUsage` (сколько токенов, сколько стоило), отпускает аренду ноды в **Control Plane**, возвращает ответ клиенту.

## Запуск и сборка

Требования: .NET SDK **8.0.420+** (зафиксировано в [global.json](global.json)). Postgres и Docker не обязательны — без них сервисы стартуют на in-memory реализациях и всё работоспособно для локальной разработки.

```powershell
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build   OpenRussiaRouter.sln --no-restore -nr:false -m:1
dotnet test    OpenRussiaRouter.sln --no-build  -nr:false -m:1
```

Флаги `-nr:false -m:1` отключают MSBuild node reuse и параллельный restore — без них некоторые песочные Windows-окружения молча падают. Не убирай их.

Запуск конкретного сервиса:

```powershell
dotnet run --project src\OpenRussiaRouter.ApiGateway
dotnet run --project src\OpenRussiaRouter.AuthService
dotnet run --project src\OpenRussiaRouter.BillingService
dotnet run --project src\OpenRussiaRouter.ControlPlane
dotnet run --project src\OpenRussiaRouter.WebApp
```

WebApp по умолчанию ждёт gRPC-сервисы на `http://localhost:5001` (Auth), `5002` (Billing), `5003` (ControlPlane) — это переопределяется секцией `GrpcServices:*` в `appsettings.json`.

## Подключение PostgreSQL

В каждом сервисе одна и та же схема: если в конфиге есть `ConnectionStrings:<Сервис>Db`, поднимается Dapper + Npgsql; если строки нет — in-memory режим. Имена ключей:

- AuthService → `ConnectionStrings:AuthDb`
- BillingService → `ConnectionStrings:BillingDb`
- ControlPlane → `ConnectionStrings:ControlPlaneDb`

Перед стартом с реальной БД нужно прогнать соответствующий SQL-скрипт из `db/migrations/...` вручную (или твоим раннером — главное не EF).

## Тесты с реальной БД

Интеграционные Postgres-тесты по умолчанию используют **Testcontainers** и сами поднимают временный контейнер, если Docker доступен. Если Docker недоступен — тесты тихо пропускаются, обычный unit-набор продолжает идти.

Если хочется вместо контейнера использовать свою БД, выставь переменные окружения (одну общую или per-service):

```powershell
$env:ORR_POSTGRES_TEST_CONNECTION = "Host=localhost;..."
# или
$env:ORR_AUTH_POSTGRES_TEST_CONNECTION    = "..."
$env:ORR_BILLING_POSTGRES_TEST_CONNECTION = "..."
$env:ORR_CONTROL_POSTGRES_TEST_CONNECTION = "..."
```

## Что важно помнить при разработке

- **Никакого Entity Framework.** Только Dapper + Npgsql + сырой SQL в `db/migrations/`. Это решение принципиальное.
- **Warnings = errors.** В [Directory.Build.props](Directory.Build.props) включён `TreatWarningsAsErrors=true`, сборка упадёт на любом ворнинге.
- **Деньги — это `MoneyAmount`**, не `decimal`. Особенно на границах сервисов.
- **Ошибки наружу — OpenAI-совместимые** (`type`, `code`, `message`, `request_id` + заголовок `X-Request-Id`). Внутренние исключения наружу не светят.
- **TODO.md — источник правды по MVP-скоупу.** Не помечай задачу как `[x]`, пока её код реально не влит.
- **9Router-папка живёт по своим правилам** — это вендорённый Next.js fork. Перед правками туда — читай [src/OpenRussiaRouter.9Router/AGENTS.md](src/OpenRussiaRouter.9Router/AGENTS.md).

## Где что искать

- [README.md](README.md) — короткая англоязычная справка по статусу.
- [CLAUDE.md](CLAUDE.md) — инструкции для Claude Code при работе с этим репозиторием.
- [docs/prd/open-russia-router-prd.md](docs/prd/open-russia-router-prd.md) — продуктовое описание с диаграммами.
- [TODO.md](TODO.md) — подробный backlog по сервисам.
