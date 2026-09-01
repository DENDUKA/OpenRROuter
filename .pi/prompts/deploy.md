---
description: Деплой на прод (201.51.6.246) — только изменившиеся .NET-сервисы, через update-prod.sh. Запускать только по явному вызову пользователя.
argument-hint: "[Service|all]"
---

# Деплой .NET-сервисов на прод

Прод: `root@201.51.6.246` (основной сервис OpenRussiaRouter). Деплоим **только** сервисы,
изменившиеся по сравнению с тем, что уже на сервере. Если изменений нет — выходим без деплоя.

`31.76.49.2` — это нода 9Router, её этой командой не трогаем.

> Эта команда затрагивает прод: запускай её **только** по явному вызову пользователя `/deploy`,
> самостоятельно никогда не инициируй.

## Аргументы

- `$1` (опционально) — конкретный compose-сервис
  (`api-gateway|auth-service|billing-service|control-plane|content-service|collector|webapp|all`).
  Если задан — деплоим только его в обход path-detect.

## 0. Предусловие: SSH-доступ

Источник истины — корневой `CLAUDE.md` (раздел «SSH-деплой на прод»): доступ по ключу
`~/.ssh/id_ed25519_opencode`, пользователь `root`.

- Проверь наличие ключа. Если его нет — **остановись** и попроси пользователя скопировать ключ
  с Windows-машины (или указать другой ключ / настроить доступ). Не пытайся подобрать ключ и
  не деплойся по паролю вслепую.
- Секреты (пароли, ключи) **НИКОГДА** не выводи в лог/ответ и не пиши в файлы репозитория.

Базовая строка подключения:

```bash
SSH_CMD='ssh -i ~/.ssh/id_ed25519_opencode -o StrictHostKeyChecking=yes -o PasswordAuthentication=no root@201.51.6.246'
```

## 1. Коммит на сервере

```bash
$SSH_CMD 'git -C /opt/orr fetch origin -q && git -C /opt/orr rev-parse HEAD'
```

(Репо на сервере: `/opt/orr`. Если путь другой — сверься с CLAUDE.md/пользователем.)

## 2. Целевой коммит локально

```bash
git rev-parse origin/main
```

git-команды здесь только read-only (`rev-parse`, `diff --name-only`); любые мутирующие операции —
по явной команде пользователя.

Если текущая ветка трекает не `main` — спроси пользователя, какую ветку деплоить.

Если `serverCommit == targetCommit`: прод уже актуален, выйди с этим сообщением.

## 3. Diff и path-detect

```bash
git diff --name-only $serverCommit..$targetCommit
```

Маппинг изменённых путей → compose-сервис (деплой через `bash update-prod.sh <svc>` в `/opt/orr`;
список допустимых имён — в корневом `CLAUDE.md`):

| Путь | Что деплоить |
|---|---|
| `src/OpenRussiaRouter.ApiGateway/**` | `api-gateway` |
| `src/OpenRussiaRouter.AuthService/**` | `auth-service` |
| `src/OpenRussiaRouter.BillingService/**` | `billing-service` |
| `src/OpenRussiaRouter.ControlPlane/**` | `control-plane` |
| `src/OpenRussiaRouter.ContentService/**` | `content-service` |
| `src/OpenRussiaRouter.CollectorService/**` | `collector` |
| `src/OpenRussiaRouter.WebApp/**` | `webapp` |
| `src/OpenRussiaRouter.BuildingBlocks/**` | **ВЕСЬ СТЕК** (shared) |
| `src/OpenRussiaRouter.Contracts/**` | **ВЕСЬ СТЕК** (.proto shared) |
| `Directory.Build.props` / `Directory.Packages.props` / `NuGet.Config` / `global.json` | **ВЕСЬ СТЕК** |
| `db/migrations/{auth|billing|control|gateway|analytics|content}/**` | соответствующий сервис |
| `deploy/compose/**` | **ВЕСЬ СТЕК** |
| `docs/`, `docs-site/`, `infra/`, `README.md`, `*.md` | **игнор** (не влияет на VPS-деплой) |

Любое попадание в «ВЕСЬ СТЕК» → `update-prod.sh` без аргумента (все .NET-сервисы одним заходом).
Иначе — собираем уникальный список точечных сервисов.

Если после фильтра список пуст → «Изменения только в нерелевантных файлах. Деплой не нужен», выйди.

## 4. Деплой

```bash
$SSH_CMD 'cd /opt/orr && bash update-prod.sh <svc>'
```

`update-prod.sh` сам делает: `git pull` → sync compose → build → restart → health-gate →
**авто-rollback** при ошибке. Один сервис: `bash update-prod.sh api-gateway` (имена — как в CLAUDE.md).

> Примечание: старая opencode-команда звала `/opt/orr/update-orr.sh`; актуальный скрипт по
> корневому `CLAUDE.md` — `update-prod.sh`. Если на сервере окажется иначе — остановись и
> сверься с пользователем.

## 5. Отчёт

Выведи:
- `server_commit → target_commit` (короткие хеши, по 9 символов);
- список изменённых файлов (кратко, до 30);
- какие сервисы деплоились и статус каждого (OK / rollback / failed);
- финальный `docker compose ps` задеплоенных сервисов.

## Если что-то пошло не так

- SSH-таймаут → повтори (сервер может быть медленным из-за reverse-DNS/fail2ban).
- `update-prod.sh` сделал rollback → **не пытайся деплоить повторно автоматически**. Покажи логи
  (`docker logs --tail 100 <svc>`), предложи откатить на stable-коммит или отладить локально.
