# 9Router VPS deployment runbook

- Model: GPT 5.5 High

Документ фиксирует фактический деплой отдельного экземпляра `9router` для проекта OpenRussiaRouter, выполненный `12-13 мая 2026`.

Здесь нет root-доступа, SSH-паролей, dashboard-пароля и содержимого секретов из `.env`. Все такие значения намеренно исключены.

## Итоговая схема

| Параметр | Значение |
|---|---|
| VPS IP | `91.186.216.209` |
| ОС | `Ubuntu 24.04.1 LTS` |
| CPU | `2 vCPU` |
| RAM | `4 GB` |
| Диск | `60 GB NVMe` |
| Домен | `ai.web-point.top` |
| DNS | `A ai.web-point.top -> 91.186.216.209`, Cloudflare `DNS only` |
| Reverse proxy | `Caddy` |
| App runtime | `Docker Compose` |
| Backend bind | `127.0.0.1:20128` |
| Public HTTPS | `https://ai.web-point.top` |
| Persistent data | `/opt/9router/data` |

Публичный трафик идет так:

```text
Internet
  -> Caddy :80/:443
  -> reverse_proxy 127.0.0.1:20128
  -> Docker container 9router
```

## Что было установлено на сервере

На чистой Ubuntu были поставлены:

- `ca-certificates`
- `curl`
- `gnupg`
- `git`
- `jq`
- `openssl`
- `dnsutils`
- `ufw`
- `Docker Engine`
- `Docker Compose plugin`
- `Caddy`

Дополнительно добавлен отдельный swap-файл:

```text
/swapfile-9router
```

Размер:

```text
2 GB
```

Он нужен не для steady-state работы приложения, а чтобы сборка Next.js-контейнера на VPS была спокойнее.

## Директории на сервере

Используется такая структура:

```text
/opt/9router/
  app/          git clone https://github.com/decolua/9router.git
  data/         persistent data volume
  .env          runtime environment, contains secrets
  compose.yml   Docker Compose конфигурация
```

В `/opt/9router/data` после старта появляются рабочие файлы приложения, в том числе:

```text
data/db/data.sqlite
data/db/data.sqlite-shm
data/db/data.sqlite-wal
data/mitm/aliases.json
data/bin/cloudflared
```

Главное, что надо сохранять при переносе или восстановлении:

```text
/opt/9router/.env
/opt/9router/data/
```

## Репозиторий и версия

На сервер был клонирован upstream:

```text
https://github.com/decolua/9router.git
```

Во время деплоя использовался commit:

```text
58788a0d314a003d16e036a5831960dbca060279
```

На тот момент `package.json` в upstream показывал:

```text
9router-app 0.4.33
```

При следующем обновлении лучше сначала проверить новый commit и только потом пересобирать контейнер.

## Environment contract

Файл:

```text
/opt/9router/.env
```

Фактически использовались такие переменные:

```dotenv
JWT_SECRET=<secret>
INITIAL_PASSWORD=<secret>
DATA_DIR=/app/data
PORT=20128
HOSTNAME=0.0.0.0
NODE_ENV=production
API_KEY_SECRET=<secret>
MACHINE_ID_SALT=<secret>
ENABLE_REQUEST_LOGS=false
OBSERVABILITY_ENABLED=true
AUTH_COOKIE_SECURE=true
REQUIRE_API_KEY=true
BASE_URL=https://ai.web-point.top
CLOUD_URL=https://9router.com
NEXT_PUBLIC_BASE_URL=https://ai.web-point.top
NEXT_PUBLIC_CLOUD_URL=https://9router.com
```

Смысл ключевых флагов:

- `AUTH_COOKIE_SECURE=true` обязателен, потому что dashboard открыт по HTTPS.
- `REQUIRE_API_KEY=true` оставлен включенным для публичного `/v1`.
- `DATA_DIR=/app/data` должен совпадать с volume mount в Compose.
- `BASE_URL` и `NEXT_PUBLIC_BASE_URL` должны смотреть на реальный внешний домен.

## Docker Compose

Файл:

```text
/opt/9router/compose.yml
```

Рабочая конфигурация:

```yaml
services:
  9router:
    container_name: 9router
    image: 9router-local:latest
    build:
      context: ./app
      dockerfile: Dockerfile
    restart: unless-stopped
    env_file:
      - .env
    command:
      - sh
      - -lc
      - >-
        mkdir -p /app/app/.next &&
        ln -sfn /app/.next/static /app/app/.next/static &&
        ln -sfn /app/public /app/app/public &&
        exec node /app/app/server.js
    ports:
      - "127.0.0.1:20128:20128"
    volumes:
      - ./data:/app/data
```

## Почему Compose выглядит именно так

Во время первого запуска обнаружились две проблемы текущего upstream Dockerfile.

### 1. Контейнер падал на старте

Симптом:

```text
Error: Cannot find module '/app/server.js'
```

Причина:

- upstream Dockerfile запускает:

```dockerfile
CMD ["node", "server.js"]
```

- но собранный standalone-сервер реально оказывается здесь:

```text
/app/app/server.js
```

Поэтому рабочий Compose запускает:

```text
node /app/app/server.js
```

### 2. UI зависал на `Loading...`

Симптом:

- домен открывается;
- HTML `/login` приходит;
- браузер бесконечно показывает `Loading...`.

Проверка показала `404` для:

```text
/_next/static/chunks/...
/_next/static/css/...
/favicon.svg
/providers/claude.png
```

Причина:

- приложение стартует из standalone-root `/app/app`;
- статические файлы и `public` были скопированы Dockerfile в `/app/.next/static` и `/app/public`;
- runtime ожидал их относительно `/app/app`.

Временный production workaround реализован в `command` Compose:

```sh
mkdir -p /app/app/.next &&
ln -sfn /app/.next/static /app/app/.next/static &&
ln -sfn /app/public /app/app/public &&
exec node /app/app/server.js
```

После этого:

- JS chunks стали `200`;
- CSS стал `200`;
- static images стали `200`;
- dashboard перестал висеть на `Loading...`.

При следующем обновлении стоит проверить, не исправил ли upstream Dockerfile этот баг. Если исправил, symlink workaround можно убрать.

## Caddy

Файл:

```text
/etc/caddy/Caddyfile
```

Рабочий конфиг:

```caddyfile
ai.web-point.top {
	encode zstd gzip
	reverse_proxy 127.0.0.1:20128
}
```

Сервис:

```bash
systemctl enable --now caddy
systemctl reload caddy
```

После появления DNS-записи Caddy сам получил Let's Encrypt сертификат.

Фактически был выдан сертификат:

```text
CN = ai.web-point.top
Issuer = Let's Encrypt E8
Valid until = 2026-08-10
```

## Firewall

Включен `ufw`.

Разрешено:

```text
22/tcp
80/tcp
443/tcp
```

Публично не открыт:

```text
20128/tcp
```

Порт приложения опубликован только на loopback:

```text
127.0.0.1:20128:20128
```

## Проверки после деплоя

### DNS

```bash
dig +short ai.web-point.top A
```

Ожидаемо:

```text
91.186.216.209
```

### Caddy

```bash
systemctl is-active caddy
journalctl -u caddy -n 100 --no-pager
```

### Container

```bash
cd /opt/9router
docker compose -f compose.yml ps
docker logs --tail 100 9router
```

Ожидаемо:

```text
container status Up
Next.js Ready
[DB] Driver: better-sqlite3
```

### Backend locally

```bash
curl -I http://127.0.0.1:20128/dashboard
curl -I http://127.0.0.1:20128/login
curl -sS http://127.0.0.1:20128/api/auth/status
```

Нормальное поведение:

- `/dashboard` без авторизации дает redirect на `/login`;
- `/login` возвращает `200`;
- `/api/auth/status` возвращает JSON.

### Public HTTPS

```bash
curl -I https://ai.web-point.top
```

Нормальный результат:

```text
HTTP/2 307
location: /dashboard
```

или redirect на `/login`, если пользователь не авторизован.

### Static assets

Это критичная проверка из-за найденного upstream-багa:

```bash
curl -I https://ai.web-point.top/_next/static/chunks/main-app-dd34b0e775cca20f.js
curl -I https://ai.web-point.top/_next/static/css/599369d853c61df7.css
curl -I https://ai.web-point.top/favicon.svg
```

Ожидаемо:

```text
HTTP/2 200
```

Если снова виден вечный `Loading...`, первым делом перепроверять именно `_next/static/*`.

## Обновление текущего сервера

Базовый сценарий:

```bash
cd /opt/9router/app
git fetch --all --prune
git status --short
git rev-parse HEAD
git log --oneline -n 5
```

Если репозиторий чистый и обновление согласовано:

```bash
git pull --ff-only
cd /opt/9router
docker compose -f compose.yml up -d --build
docker compose -f compose.yml ps
docker logs --tail 120 9router
```

После обновления обязательно повторить:

```bash
curl -I https://ai.web-point.top/login
curl -I https://ai.web-point.top/_next/static/chunks/main-app-dd34b0e775cca20f.js
curl -sS https://ai.web-point.top/api/auth/status
```

Если upstream поменял структуру standalone-образа:

- проверить наличие `/app/server.js`;
- проверить наличие `/app/app/server.js`;
- проверить раздачу `_next/static/*`;
- только после этого решать, оставлять или убрать symlink workaround.

## Бэкап

Минимум для ручного резервного копирования:

```bash
tar -czf /root/9router-backup-$(date +%F).tar.gz \
  /opt/9router/.env \
  /opt/9router/data \
  /opt/9router/compose.yml \
  /etc/caddy/Caddyfile
```

Перед любым крупным обновлением или переносом на новый сервер лучше делать такой архив.

## Перенос на новый физический сервер

Рекомендуемый порядок:

1. Поднять новую Ubuntu 24.04.
2. Установить Docker, Docker Compose, Caddy, Git, UFW.
3. Создать `/opt/9router`.
4. Склонировать `https://github.com/decolua/9router.git` в `/opt/9router/app`.
5. Перенести:
   - `/opt/9router/.env`
   - `/opt/9router/data`
   - `/opt/9router/compose.yml`
   - `/etc/caddy/Caddyfile`
6. Запустить:

```bash
cd /opt/9router
docker compose -f compose.yml up -d --build
```

7. Проверить локально:

```bash
curl -I http://127.0.0.1:20128/login
```

8. Переключить DNS `ai.web-point.top` на новый IP.
9. Перезагрузить Caddy:

```bash
systemctl reload caddy
```

10. Дождаться нового сертификата Let's Encrypt и проверить:

```bash
curl -I https://ai.web-point.top
```

## Переустановка на том же сервере

Если контейнер нужно пересобрать с нуля, но данные сохранить:

```bash
cd /opt/9router
docker compose -f compose.yml down
docker compose -f compose.yml up -d --build
```

Не удалять:

```text
/opt/9router/.env
/opt/9router/data
```

Если их удалить, сервис поднимется как новый экземпляр.

## Что еще стоит помнить

- Домашний домен `web-point.top` может жить на другом сервере. Это не мешает, если `ai.web-point.top` отдельной DNS-записью смотрит на VPS `9router`.
- `Caddy` выпускал сертификат только после того, как запись стала видна authoritative DNS и публичным резолверам. До этого в логах был `NXDOMAIN`.
- На момент деплоя Windows `curl` локально однажды дал ошибку Schannel, но серверная Linux-проверка HTTPS и certificate chain были успешными. Для диагностики лучше доверять `curl`/`openssl` на Linux-сервере.
- Dashboard-пароль и все секреты должны храниться только вне этого документа.

