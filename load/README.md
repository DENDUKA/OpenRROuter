# Нагрузочное тестирование ApiGateway (k6)

Сценарии k6 для проверки стабильности OpenRussiaRouter под пиковой нагрузкой (~80
одновременных API-пользователей ≈ 10–25 RPS). Спецификация:
`docs/superpowers/specs/2026-07-29-load-testing-design.md`.

## Установка k6

```powershell
winget install GrafanaLabs.k6
k6 version
```

> После установки откройте **новый** терминал, чтобы `k6` появился в PATH.

## Подготовка данных

1. **Тестовый пользователь.** Создать пользователя с положительным балансом (иначе
   шлюз вернёт `402 billing_error`).
2. **Пул из 50 API-ключей** под этим пользователем (`/workspace/apikeys`). Rate-limit
   прода — 60 запросов/мин на ключ; один ключ задушит тест на 1 RPS. k6 раздаёт ключи
   round-robin по VU (`apiKeyForVU()` в `lib/config.js`).
3. **`.env`:** `cp load/.env.example load/.env`, заполнить `K6_API_KEYS` (через запятую).
4. **Сверить модель:** `GET https://api.rawrter.com/v1/models` → `K6_MODEL_ID`
   (дефолт `gemini-3.5-flash`).

## Сценарии

| Сценарий | Команда | Профиль |
|---|---|---|
| smoke | `./load/run.ps1 load/scenarios/smoke.js` | 1 VU, 1 iter — проверка готовности |
| models-get | `./load/run.ps1 load/scenarios/models-get.js` | 50 RPS, 3 мин — потолок инфраструктуры (без ключей и LLM) |
| chat-ramp | `./load/run.ps1 load/scenarios/chat-ramp.js` | 1→25 RPS, ~12 мин — кривая деградации |
| chat-stream | `./load/run.ps1 load/scenarios/chat-stream.js` | 20 VU, 5 мин — SSE + TTFB |
| soak | `./load/run.ps1 load/scenarios/soak.js` | 15 RPS, 15 мин — устойчивость во времени |

## Порядок запуска

1. **Всегда `smoke` первым.** Падение (не 200) = стоп, дальше не гонять. Возможные
   коды: 401 — неверный ключ; 402 — нет баланса; 404 — неверный `model_id`; 503 — нет
   доступных нод 9Router.
2. Затем один тяжёлый сценарий за раз (не параллельно): `chat-ramp` ИЛИ `chat-stream`
   ИЛИ `soak`. `models-get` можно гонять независимо (он не тратит токены).
3. `thresholds.abortOnFail` стопит тест автоматически при error_rate>5% или p99>30с.

## Наблюдение за продом во время прогона

SSH-туннель к Grafana (метрики прода):
```powershell
ssh -L 3001:127.0.0.1:3001 root@89.191.225.228
# затем http://localhost:3001 — дашборды ORR
```

Смотреть: CPU/RAM контейнеров (`api-gateway`, `auth`, `billing`, `control-plane`,
`postgres`), `orr_http_requests_total`, `orr_http_request_duration_seconds`,
`orr_limit_rejections_total`, рост очереди `gateway.usage_outbox`, Postgres-пулы.

Опционально — прямая запись метрик k6 в прод-Prometheus:
```powershell
./load/run.ps1 load/scenarios/chat-ramp.js -o experimental-prometheus-rw=http://localhost:9090/api/v1/write
```

## Структура

```
load/
  lib/
    config.js        — env: BASE_URL, MODEL_ID, API_KEYS[]; apiKeyForVU() round-robin
    chat-body.js     — nonStreamBody(modelId) / streamBody(modelId)
  scenarios/
    smoke.js         — readiness check
    models-get.js    — инфраструктурный потолок
    chat-ramp.js     — основной, кривая деградации
    chat-stream.js   — SSE-стриминг + метрика ttfb_ms
    soak.js          — устойчивость 15 мин
  run.ps1            — грузит .env в окружение, вызывает k6 run
  .env.example       — заглушки (скопировать в .env, заполнить)
```

## Безопасность

- `load/.env` gitignored — реальные ключи туда, в репозиторий **не коммитить**.
- k6 гоняем с машины оператора, не с прода (чтобы не отъедать CPU у тестируемого хоста).
- На случай падения прода: `update-prod.sh` / `deploy-prod.sh` для рестарта.
