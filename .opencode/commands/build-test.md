---
description: Собрать и прогнать тесты OpenRussiaRouter (весь solution или один сервис).
agent: build
---

# Build & Test

`$1` — имя сервиса или `all` (по умолчанию `all`). Варианты сервиса: `ApiGateway`, `AuthService`,
`BillingService`, `ControlPlane`, `ContentService`, `CollectorService`, `WebApp`, `BuildingBlocks`, `all`.

## 1. Restore (один раз; если уже восстановлено — пропусти)

```powershell
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
```

## 2. Build

```powershell
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

**Обязательно**: флаги `-nr:false -m:1` — без них песочницы Windows падают на MSBuild node reuse.
`TreatWarningsAsErrors=true` (`Directory.Build.props`) — **любой warning = ошибка сборки**.
Central Package Management — версии только в `Directory.Packages.props`, не в `.csproj`.
Entity Framework в проекте не используется (только Dapper + Npgsql) — EF-пакеты не добавлять.

### Error recovery (build)

- **root cause**: `TreatWarningsAsErrors=true` → warning это реальная проблема, не шум; Entity
  Framework/`DbContext` запрещены (только Dapper+Npgsql); версия пакета вне
  `Directory.Packages.props` тоже ломает сборку.
- **safe retry**: почини причину warning'а (не `#pragma`) → перезапусти `dotnet build` → только
  потом к тестам.
- **stop condition**: build красный → к тестам не переходим, задачу не сдаём. Покажи первые ~30
  строк ошибки.

## 3. Tests

`all`:

```powershell
dotnet test OpenRussiaRouter.sln --no-build -nr:false -m:1
```

Конкретный сервис:

```powershell
dotnet test tests/OpenRussiaRouter.$1.Tests --no-build -nr:false -m:1
```

Если `--no-build` падает с "project not found" — запускай с `--no-restore` вместо `--no-build`.

### PostgreSQL-интеграционные тесты

На Testcontainers: сами поднимают контейнер postgres; без Docker тихо скипаются.
Переменные `ORR_{AUTH|BILLING|CONTROL}_POSTGRES_TEST_CONNECTION` только **переопределяют** строку
подключения, они не включают тесты.

### E2E (Playwright, отдельно)

```powershell
pwsh tests/OpenRussiaRouter.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
```

Конфиг: `tests/OpenRussiaRouter.E2ETests/e2e.local.json` (gitignored).

## 4. Результат

Краткая сводка: сколько тестов прошло/упало, были ли warnings при сборке.
