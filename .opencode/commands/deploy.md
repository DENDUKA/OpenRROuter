---
description: Деплой на прод (89.191.225.228) — только изменившиеся .NET-сервисы, через готовый update-orr.sh.
agent: build
---

# Деплой .NET-сервисов на прод

Прод: `root@89.191.225.228` (основной сервис OpenRussiaRouter). Деплоим **только** сервисы,
изменившиеся по сравнению с тем, что уже на сервере. Если изменений нет — выходим без деплоя.

`31.76.49.2` — это нода 9Router, её этой командой не трогаем.

## Аргументы

- `$1` (опционально) — конкретный compose-сервис (`api-gateway|auth-service|billing-service|control-plane|content-service|collector|webapp|all`).
  Если задан — деплоим только его в обход path-detect.

## 0. Секрет

Пароль от root лежит **вне репозитория**: `~/.config/opencode/secrets/orr-prod.password`
(одна строка без перевода; источник — Google Sheets «основной сервис»).

- Если файла нет — **остановись** и попроси пользователя создать его:
  ```powershell
  New-Item -ItemType File -Force -Path "$HOME\.config\opencode\secrets\orr-prod.password"
  # затем вставить пароль из Google Sheets одной строкой
  ```
- Пароль **НИКОГДА** не выводи в лог/ответ и не пиши в файлы репозитория.

## 1. Коммит на сервере (через Posh-SSH)

Обычный `ssh.exe` на Windows не передаёт пароль неинтерактивно — используем Posh-SSH:

```powershell
Import-Module Posh-SSH
$pass = ConvertTo-SecureString (Get-Content -Raw "$HOME\.config\opencode\secrets\orr-prod.password").Trim() -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential('root', $pass)
$s = New-SSHSession -ComputerName '89.191.225.228' -Credential $cred -AcceptKey -ConnectionTimeout 45
$r = Invoke-SSHCommand -SessionId $s.SessionId -Command 'git -C /opt/orr/app fetch origin -q && git -C /opt/orr/app rev-parse HEAD' -TimeOut 60
$serverCommit = $r.Output.Trim()
```

## 2. Целевой коммит локально

```powershell
$targetCommit = git rev-parse origin/main
```

Если текущая ветка трекает не `main` — спроси пользователя, какую ветку деплоить.

Если `$serverCommit -eq $targetCommit`: прод уже актуален, выйди с этим сообщением.

## 3. Diff и path-detect

```powershell
git diff --name-only $serverCommit..$targetCommit
```

Маппинг изменённых путей → compose-сервис (деплой через `/opt/orr/update-orr.sh <svc>`):

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

Любое попадание в «ВЕСЬ СТЕК» → вызываем `update-orr.sh` без аргумента (все .NET-сервисы одним заходом).
Иначе — собираем уникальный список точечных сервисов.

Если после фильтра список пуст → «Изменения только в нерелевантных файлах. Деплой не нужен», выйди.

## 4. Деплой

Переиспользуй SSH-сессию `$s` для всех сервисов:

```powershell
Invoke-SSHCommand -SessionId $s.SessionId -Command "/opt/orr/update-orr.sh $svc" -TimeOut 600
```

`update-orr.sh` сам делает: `build` → `up -d` → health-gate 60с → **авто-rollback** при падении.

После всех сервисов: `Remove-SSHSession -SessionId $s.SessionId | Out-Null`.

## 5. Отчёт

Выведи:
- `server_commit → target_commit` (короткие хеши, по 9 символов);
- список изменённых файлов (кратко, до 30);
- какие сервисы деплоились и статус каждого (OK / rollback / failed);
- финальный `docker compose ps` задеплоенных сервисов.

## Если что-то пошло не так

- SSH-таймаут → повтори с `-ConnectionTimeout 90`. Сервер медленный из-за reverse-DNS/fail2ban.
- `update-orr.sh` сделал rollback → **не пытайся деплоить повторно автоматически**. Покажи логи
  (`docker logs --tail 100 <svc>`), предложи откатить на stable-коммит или отладить локально.
