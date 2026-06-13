---
description: "Собрать и протестировать проект OpenRussiaRouter. Поддерживает все сервисы и флаги проекта."
---

# Build & Test

Выполни сборку и тесты для OpenRussiaRouter.

## Параметры

- `$1` — имя сервиса или `all` (по умолчанию `all`). Варианты: `ApiGateway`, `AuthService`, `BillingService`, `ControlPlane`, `WebApp`, `BuildingBlocks`, `all`

## Процедура

### 1. Сборка решения

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

**Важно**: флаги `-nr:false -m:1` обязательны — sandboxed Windows окружения падают без них.

**Важно**: `TreatWarningsAsErrors = true` в `Directory.Build.props` — любое предупреждение = ошибка сборки.

### 2. Тесты

Если `$1` = `all`:

```powershell
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

Если указан конкретный сервис:

```powershell
dotnet test tests/OpenRussiaRouter.$1.Tests --no-build -nr:false -m:1
```

### 3. Обработка ошибок

- Если сборка упала — покажи первые 30 строк ошибки, не продолжай к тестам.
- Если тесты упали — покажи имена упавших тестов и stack trace.
- Если `--no-build` падает с "project not found" — попробуй с `--no-restore` вместо `--no-build`.

### 4. PostgreSQL тесты (опционально)

PostgreSQL интеграционные тесты требуют переменных окружения:
- `ORR_AUTH_POSTGRES_TEST_CONNECTION`
- `ORR_BILLING_POSTGRES_TEST_CONNECTION`
- `ORR_CONTROL_POSTGRES_TEST_CONNECTION`

Без них — тесты пропускаются (Testcontainers, skip если нет Docker).

### 5. E2E тесты (отдельно)

Playwright E2E тесты запускаются отдельно и требуют установленных браузеров:
```powershell
pwsh tests/OpenRussiaRouter.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
```
Конфиг: `tests/OpenRussiaRouter.E2ETests/e2e.local.json` (gitignored).

## Результат

Покажи краткую сводку: сколько тестов прошло/упало, были ли warnings при сборке.
