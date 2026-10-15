# ORR — карта деплоев

Исходники всегда: `github.com/DENDUKA/OpenRussiaRouter`, ветка `main`.
Локальные ключи/секреты: `d:\Dev\Projects\AiGateway\.deploy\` (НЕ в репозитории).

---

## 1. PROD VPS  ⭐ (основной прод)

- **Что:** ORR-only .NET-стек (postgres, seq, auth, billing, control-plane, content, collector, api-gateway, webapp, docs, caddy, prometheus, grafana + отдельный проект `rawrter-tg-bot`). f9router НЕ здесь — 9R внешний (на dev-боксе).
- **IP:** `89.191.225.228` (AdminVPS «Micro», Москва, 2 vCPU / 4 ГБ / 60 ГБ NVMe, Ubuntu 24.04). Переехал 2026-10-15 с `201.51.6.246` (zetalink, не продлевается; до конца аренды только пробрасывает 80/443 на новый бокс). Хостинг в РФ выбран из-за 152-ФЗ: база с персональными данными должна быть в РФ.
- **Домен:** https://rawrter.com (Caddy + Let's Encrypt, авто-renew)
- **Где код:** на боксе `/opt/orr/app/` (git-клон main). Рядом: `compose.yml`, `.env`, `Caddyfile`, скрипты.
- **SSH:**
  ```
  ssh -i .deploy/orr_prod_ru_vps_access root@89.191.225.228
  ```
- **Наружу открыты только 80/443** (Caddy). Seq, gRPC, health, Grafana и Prometheus привязаны к 127.0.0.1, доступ к ним — через SSH-туннель.
- **DNS бокса:** `/etc/resolv.conf` зафиксирован (`chattr +i`) на Яндекс DNS 77.88.8.8/77.88.8.1. Дефолтный 195.208.4.1 от хостера отдавал устаревший NXDOMAIN и мусорные A-записи.
- **Telegram:** из РФ `api.telegram.org` режется по SNI. Бот ходит через `https://tg.rawrter.com`: это Caddy-прокси на франкфуртском боксе 2.26.108.249 (`deploy/tg-proxy/`, `/opt/tg-proxy`), пускает только IP прода.
- **Обновление (на боксе):**
  ```
  /opt/orr/update-prod.sh          # git pull main → пересинк compose+Caddyfile → build всех .NET → up с health-gate
  ```
  - build-on-box. Если main не компилится — падает на `build` ДО `up`, контейнеры не трогаются (откат не нужен).
  - `compose.yml` намеренно отличается от dev (f9router вырезан). Не копировать compose вслепую.
  - `deploy-prod.sh` = полная раскатка с нуля; для обычного апдейта нужен `update-prod.sh`.

---

## 2. DEV VPS

- **Что:** ORR (ветка develop) + **prod-9R (f9router)**. Прод ходит к этому 9R как к внешнему узлу (`:20128`).
- **IP:** `31.76.49.2` (NL-R9-8, Ubuntu 24.04)
- **Доступ:** IP-only HTTP — API `:8080`, WebApp `:3000`, Seq `:5341`, f9router `:20128`
- **Где код:** `/opt/orr/app/` (ORR) и `/opt/orr/f9router/` — оба git-клоны main.
- **SSH:**
  ```
  ssh -i .deploy/orr_vps_access root@31.76.49.2
  ```
- **Обновление (на боксе):**
  ```
  /opt/orr/update.sh all           # или: orr | 9r
  ```
  Каждый скрипт: `git pull --ff-only` → build → restart → health-gate → rollback.

---

## 3. kind / ArgoCD кластер (GitOps)

- **Что:** dev-кластер на удалённом хосте, GitOps через ArgoCD.
- **IP:** `100.103.25.20` (только через Tailscale)
- **Доступ:** ArgoCD `https://100.103.25.20:9090`, Seq `http://100.103.25.20:18081`, WebApp `http://100.103.25.20`
- **SSH/CLI:** нет — `kubectl`/`argocd`/`docker`/`gh` локально НЕ установлены, только `git`.
- **Обновление = НЕ ssh, а git-push:**
  ```
  git push origin main            # → GitHub Actions собирает образы в ghcr.io/denduka/<svc>:dev-latest
                                  # → ArgoCD Image Updater (~2 мин) → auto-sync в namespace orr-dev
  ```
  - `selfHeal:true` — ручные `kubectl set ...` откатываются; менять только через kustomize+git.
  - ⚠️ ArgoCD «Synced» ≠ свежий код. Проверять, что GitHub **Actions зелёный**, а не только ArgoCD.

---

### Шпаргалка «как обновить»

| Деплой | IP | Команда |
|--------|-----|---------|
| **Prod** | 89.191.225.228 | `ssh … root@89.191.225.228` → `/opt/orr/update-prod.sh` |
| **Dev**  | 31.76.49.2   | `ssh … root@31.76.49.2` → `/opt/orr/update.sh all` |
| **Кластер** | 100.103.25.20 | `git push origin main` (без ssh) |
