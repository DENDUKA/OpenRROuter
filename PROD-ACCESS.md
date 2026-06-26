# PROD-ACCESS — доступы к продакшн-серверу OpenRussiaRouter

> Внимание: этот файл содержит секреты в открытом виде. Не форкать, не
> публиковать, не копировать в публичные репозитории. Файл живёт только в
> приватном репо `DENDUKA/OpenRussiaRouter`.

## Продакшн-сервер (единый хост)

| Параметр | Значение |
|---|---|
| IP | `31.76.49.2` |
| Домен | `https://openrussiarouter.com/` (спереди Cloudflare/реверс-прокси) |
| Хостинг | `xorek.cloud` (VPS), hostname `1798389.xorek.cloud` |
| OS | Ubuntu 24.04, kernel 6.8.0-111-generic |
| Docker | 29.5.2 |

### SSH (root)

```
host:     31.76.49.2
user:     root
password: QDld3AJ0wxFk
```

Подключение с Windows через Posh-SSH (обычный `ssh.exe` не передаёт пароль
неинтерактивно). Таймаут подключения ≥ 45 сек (медленный reverse-DNS / fail2ban):

```powershell
Import-Module Posh-SSH
$pass = ConvertTo-SecureString 'QDld3AJ0wxFk' -AsPlainText -Force
$cred  = New-Object System.Management.Automation.PSCredential('root', $pass)
$s = New-SSHSession -ComputerName '31.76.49.2' -Credential $cred -AcceptKey -ConnectionTimeout 45
Invoke-SSHCommand -SessionId $s.SessionId -Command '...' -TimeOut 30
Remove-SSHSession -SessionId $s.SessionId | Out-Null
```

## Веб-интерфейсы

| URL | Назначение | Доступ |
|---|---|---|
| `https://openrussiarouter.com/` | WebApp (Razor cabinet + admin) | email+pass пользователя-админа (см. `/opt/orr/.env`: `INITIAL_ADMIN_EMAIL` / `INITIAL_ADMIN_PASSWORD`) |
| `http://31.76.49.2:5341` | **Seq** (структурированные логи) | пароль `SEQ_ADMIN_PASSWORD` из `/opt/orr/.env` |
| `http://31.76.49.2:20128` | f9router admin напрямую | Bearer `ROUTER_CONTROL_TOKEN` (= `ORR_NODE_CONTROL_TOKEN` из `.env`) |
| `http://31.76.49.2:3000` | WebApp напрямую (без TLS) | — |
| `http://31.76.49.2:8080` | ApiGateway напрямую | public `/v1/*` |

## Расположение на сервере

```
/opt/orr/
├── app/             git@github.com:DENDUKA/OpenRussiaRouter.git   (.NET сервисы)
├── f9router/        git@github.com:Inc64/F9Router.git            (Node.js, наш форк 9router)
├── compose.yml      -> app/deploy/compose/compose.yml
├── update.sh        -> app/deploy/compose/update.sh
├── update-orr.sh    -> app/deploy/compose/update-orr.sh
├── update-9router.sh-> app/deploy/compose/update-9router.sh
├── bootstrap-git.sh -> app/deploy/compose/bootstrap-git.sh
└── .env             # секреты (POSTGRES_PASSWORD, JWT, S2S tokens, SMTP, F9_*, ORR_NODE_CONTROL_TOKEN)
```

SSH-алиасы на сервере в `/root/.ssh/config`:
- `github-orr` → github.com (для `DENDUKA/OpenRussiaRouter`)
- `github-f9`  → github.com (для `Inc64/F9Router`)

## Контейнеры и порты

| Контейнер | Образ (источник) | Порт host→ctr |
|---|---|---|
| `orr-postgres-1` | `postgres:16-alpine` | 5432 (внутр.) |
| `orr-seq-1` | `datalust/seq:latest` | 5341→80 |
| `orr-auth-service-1` | build `./app` (AuthService) | 5001→8080 (gRPC Http2) |
| `orr-billing-service-1` | build `./app` (BillingService) | 5002→8080 |
| `orr-control-plane-1` | build `./app` (ControlPlane) | 5003→8080 |
| `orr-collector-1` | build `./app` (CollectorService) | 5005→8081 (gRPC), 5006→8080 (HTTP) |
| `orr-api-gateway-1` | build `./app` (ApiGateway) | 8080→8080 |
| `orr-webapp-1` | build `./app` (WebApp Razor) | 3000→8080 |
| `orr-f9router-1` | build `./f9router` (Next.js) | 20128→20128 |

БД: `openrussiarouter`, юзер `postgres`, volume `postgres_data`.

## Команды обновления

```bash
cd /opt/orr
./update.sh             # обновить и ORR, и f9router
./update.sh orr         # только .NET-сервисы
./update.sh 9r          # только f9router (git pull → build → up → health → авто-rollback)
```

## Типовые операции

```bash
# Логи
docker logs --tail 200 orr-api-gateway-1
docker logs -f --since 10m orr-webapp-1

# БД
docker exec -it orr-postgres-1 psql -U postgres -d openrussiarouter

# Health
curl -s http://127.0.0.1:20128/api/health   # f9router → {"ok":true}
curl -s http://127.0.0.1:8080/health         # api-gateway
docker compose ps                            # весь стек

# Версия f9router
curl -s http://127.0.0.1:20128/api/version
```
