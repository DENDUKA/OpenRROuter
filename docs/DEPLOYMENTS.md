# ORR — карта деплоев

Исходники всегда: `github.com/DENDUKA/OpenRussiaRouter`, ветка `main`.
Локальные ключи/секреты: `d:\Dev\Projects\AiGateway\.deploy\` (НЕ в репозитории).

---

## 1. PROD VPS  ⭐ (основной прод)

- **Что:** ORR-only .NET-стек (9 контейнеров: postgres, seq, auth, billing, control-plane, collector, api-gateway, webapp, caddy). f9router НЕ здесь — 9R внешний (на dev-боксе).
- **IP:** `201.51.6.246` (zetalink VDS, Ubuntu 24.04)
- **Домен:** https://rawrter.com (Caddy + Let's Encrypt, авто-renew)
- **Где код:** на боксе `/opt/orr/app/` (git-клон main). Рядом: `compose.yml`, `.env`, `Caddyfile`, скрипты.
- **SSH:**
  ```
  ssh -i .deploy/orr_prod_vps_access -o UserKnownHostsFile=/tmp/orr_kh/known_hosts root@201.51.6.246
  ```
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
| **Prod** | 201.51.6.246 | `ssh … root@201.51.6.246` → `/opt/orr/update-prod.sh` |
| **Dev**  | 31.76.49.2   | `ssh … root@31.76.49.2` → `/opt/orr/update.sh all` |
| **Кластер** | 100.103.25.20 | `git push origin main` (без ssh) |
