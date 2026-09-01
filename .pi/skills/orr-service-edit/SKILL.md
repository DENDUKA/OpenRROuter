---
name: orr-service-edit
description: OpenRussiaRouter service-edit guardrail. Use BEFORE editing any file under src/OpenRussiaRouter.*/ — enforces the repo rule "read the service's CLAUDE.md first". Per-service CLAUDE.md files are NOT loaded by default (pi auto-loads only the root AGENTS.md pointer; the root CLAUDE.md itself must be read too); this skill is the on-demand trigger to open the right one. Use ONLY for .NET service edits in this repo.
metadata:
  project: OpenRussiaRouter
---

# OpenRussiaRouter: прочитай CLAUDE.md сервиса перед правкой

Per-service `CLAUDE.md` грузятся **по требованию**, не всегда. Этот skill — триггер на правки
файлов под `src/OpenRussiaRouter.*/`. Это маршрутизатор, **не** копия документации: содержимое
читай по ссылкам.

## Правило (источник: корневой `CLAUDE.md`, раздел «Agent workflow»)

Перед правкой любого файла под `src/OpenRussiaRouter.<Service>/` открой `CLAUDE.md` этого сервиса.
Правка общих библиотек → ещё `BuildingBlocks/CLAUDE.md`; `.proto`/DTO → ещё `Contracts/CLAUDE.md`.

## Какой файл открыть

| Что правишь | Открыть первым |
|---|---|
| `src/OpenRussiaRouter.<Service>/**` | `src/OpenRussiaRouter.<Service>/CLAUDE.md` |
| `src/OpenRussiaRouter.BuildingBlocks/**` | `src/OpenRussiaRouter.BuildingBlocks/CLAUDE.md` (+ сервис-потребитель) |
| `src/OpenRussiaRouter.Contracts/**` | `src/OpenRussiaRouter.Contracts/CLAUDE.md` |

Build/test/deploy-инварианты живут в корневом `CLAUDE.md` — в pi он **не** загружается
автоматически (грузится только `AGENTS.md`-указатель); прочитай его в начале работы над репо.

## Стоп-условие

Целевого `CLAUDE.md` нет → остановись и спроси, не правь вслепую.

## Связанная команда

Явный запуск подготовки к правке: `/svc <Service>` (читает нужные CLAUDE.md и выдаёт отчёт по
observation-контракту: status / summary / next_actions / artifacts).
