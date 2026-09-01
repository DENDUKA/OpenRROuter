---
description: Подготовиться к правке сервиса — прочитать его CLAUDE.md и shared-библиотеки перед редактированием.
argument-hint: "<Service>"
---

# Подготовка к правке сервиса `$1`

`$1` — имя сервиса из `src/OpenRussiaRouter.<Service>/`. Допустимые: `ApiGateway`, `AuthService`,
`BillingService`, `ControlPlane`, `ContentService`, `CollectorService`, `WebApp`, `BuildingBlocks`,
`Contracts`.

Обязательный шаг (правило «Agent workflow» в `CLAUDE.md`): **не правь сервис, не открыв его CLAUDE.md.**

## 1. Прочитай (запусти Read параллельно)

- `src/OpenRussiaRouter.$1/CLAUDE.md` — паттерны, порты, ключевые классы, подводные камни сервиса.
- `src/OpenRussiaRouter.BuildingBlocks/CLAUDE.md` — если правка затрагивает общие библиотеки
  (Money, TokenUsage, ошибки, миграции, телеметрия, gRPC-хелперы).
- `src/OpenRussiaRouter.Contracts/CLAUDE.md` — если правка затрагивает `.proto` или DTO.

Если файла `CLAUDE.md` для сервиса нет — **остановись**, не начинай правку вслепую.

## 2. Отчёт (observation-контракт)

Верни одно короткое сообщение:
- **status**: `ready` | `blocked`
- **summary**: 2–4 строки — роль сервиса, ключевые классы, главные подводные камни из CLAUDE.md
- **next_actions**: какие файлы собираешься править и зачем
- **artifacts**: список прочитанных CLAUDE.md (с путями)

## 3. После правки — обязательная сборка

```bash
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

- Флаги `-nr:false -m:1` обязательны (без них Windows-песочницы падают на MSBuild node reuse).
- `TreatWarningsAsErrors=true` → **любой warning = ошибка**. Чини причину, `#pragma` не глушить.
- Central Package Management: версии только в `Directory.Packages.props`.
- EF запрещён — только Dapper + Npgsql.

## Error recovery

- **root cause**: если build упал на warning — это реальная проблема, не шум; ищи причину в коде.
- **safe retry**: почини warning → перезапусти `dotnet build` → только потом к тестам.
- **stop condition**: build красный → к тестам не переходим, задачу не сдаём.
