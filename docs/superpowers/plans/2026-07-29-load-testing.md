# Нагрузочное тестирование ApiGateway (k6) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Создать набор k6-сценариев для проверки стабильности ApiGateway под ожидаемым пиком ~80 одновременных API-пользователей (≈10–25 RPS) против прода `https://api.openrussiarouter.com`.

**Architecture:** Отдельный JS-проект в `load/` (не входит в .NET solution, `dotnet build` его не трогает). Общая библиотека в `load/lib/` (env-парсинг, тела запросов, раздача API-ключей round-robin), пять сценариев в `load/scenarios/`. PowerShell-обёртка `run.ps1` грузит `.env` в окружение и вызывает k6.

**Tech Stack:** k6 (Grafana Labs), JavaScript (ES modules в k6), PowerShell для обёртки запуска.

## Global Constraints

- **Base URL:** `https://api.openrussiarouter.com` (LLM-API на поддомене `api.`; apex обслуживает WebApp/Grafana — НЕ цель теста).
- **Модель:** `gemini-3.5-flash` (сверено с прода через публичный `GET /v1/models`). В тело запроса всегда включать `"reasoning_effort":"none"` (иначе `ReasoningRequestPatcher` подставит дефолт `high` → лишняя latency/cost).
- **Rate-limit прода:** 60 запросов/мин на API-ключ. Обход — пул из 50 ключей, раздача round-robin по VU.
- **Никаких секретов в репо:** `load/.env` (реальные ключи) — gitignored; `load/.env.example` — только заглушки.
- **Тестовый пользователь** должен иметь положительный баланс (иначе 402 `billing_error`).
- **Порядок запуска:** всегда `smoke` первым; только при его успехе — тяжёлые сценарии. `thresholds.abortOnFail` стопит тест при error_rate>5% или p99>30с.
- Коммиты — на каждом задании. Сообщения без эмодзи.

**Ссылка на спецификацию:** `docs/superpowers/specs/2026-07-29-load-testing-design.md`

---

## File Structure

| Файл | Ответственность |
|---|---|
| `load/.gitignore` | Игнорирует `.env` |
| `load/.env.example` | Заглушки env-переменных |
| `load/run.ps1` | Грузит `.env` в окружение процесса, затем вызывает `k6 run` |
| `load/README.md` | Инструкции: подготовка ключей, сверка модели, запуск сценариев, наблюдение Grafana |
| `load/lib/config.js` | Парсинг `__ENV` (BASE_URL, MODEL_ID, API_KEYS[]), экспорт `apiKeyForVU()` round-robin |
| `load/lib/chat-body.js` | Конструкторы тел запросов (non-stream / stream) |
| `load/scenarios/smoke.js` | 1 VU, 1 iter — валидация ключей/баланса/модели |
| `load/scenarios/models-get.js` | 50 RPS на `GET /v1/models` — потолок инфраструктуры |
| `load/scenarios/chat-ramp.js` | ramp 1→25 RPS, кривая деградации |
| `load/scenarios/chat-stream.js` | 20 VU, SSE-стриминг, метрика TTFB |
| `load/scenarios/soak.js` | 15 RPS steady 15 мин — устойчивость во времени |

---

### Task 1: Scaffold load/ проекта

**Files:**
- Create: `load/.gitignore`
- Create: `load/.env.example`
- Create: `load/run.ps1`
- Create: `load/README.md` (скелет — финализируется в Task 8)

**Interfaces:**
- Produces: `run.ps1` (грузит `.env` → env-vars → `k6 run $args`), структура каталогов `load/{lib,scenarios}/`.

- [ ] **Step 1: Создать `.gitignore`**

Создать `load/.gitignore`:
```
.env
```

- [ ] **Step 2: Создать `.env.example`**

Создать `load/.env.example`:
```
# Base URL LLM-API (поддомен api. -> api-gateway:8080). Apex-домен НЕ использовать.
K6_BASE_URL=https://api.openrussiarouter.com

# Публичный model_id (сверить: GET https://api.openrussiarouter.com/v1/models).
K6_MODEL_ID=gemini-3.5-flash

# Пул API-ключей через запятую (sk-or-*). Создать ~50 под тестовым пользователем
# с положительным балансом. Скопировать этот файл в .env и заполнить.
K6_API_KEYS=sk-or-KEY1,sk-or-KEY2,sk-or-KEY3
```

- [ ] **Step 3: Создать `run.ps1`**

Создать `load/run.ps1`:
```powershell
#Requires -Version 7
<#
  Грузит load/.env в окружение процесса, затем вызывает k6 run с переданными аргументами.
  Пример: ./load/run.ps1 load/scenarios/smoke.js
#>
$envFile = Join-Path $PSScriptRoot '.env'
if (Test-Path -LiteralPath $envFile) {
    Get-Content -LiteralPath $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#')) {
            $idx = $line.IndexOf('=')
            if ($idx -gt 0) {
                $name = $line.Substring(0, $idx).Trim()
                $val  = $line.Substring($idx + 1).Trim().Trim('"').Trim("'")
                Set-Item -Path "Env:$name" -Value $val
            }
        }
    }
} else {
    Write-Warning "load/.env не найден — k6 запустится с переменными текущего окружения."
}

k6 run $args
```

- [ ] **Step 4: Создать скелет `README.md`**

Создать `load/README.md`:
```markdown
# Нагрузочное тестирование ApiGateway (k6)

Сценарии k6 для проверки стабильности OpenRussiaRouter под пиковой нагрузкой.
Спецификация: `docs/superpowers/specs/2026-07-29-load-testing-design.md`.

## Установка k6

```powershell
winget install grafana.k6
```

## Подготовка

1. Создать тестового пользователя с положительным балансом.
2. Создать ~50 API-ключей под ним (`/workspace/apikeys`).
3. `cp load/.env.example load/.env`, заполнить `K6_API_KEYS`.
4. Проверить модель: `GET https://api.openrussiarouter.com/v1/models` → `K6_MODEL_ID`.

## Запуск

(Финализируется в Task 8.)
```

- [ ] **Step 5: Создать пустые каталоги lib/ и scenarios/**

Каталоги создадутся файлами из последующих задач; если нужно — создать placeholder-файлы здесь не требуется. Просто убедиться, что `load/lib/` и `load/scenarios/` будут созданы в Tasks 2–7.

- [ ] **Step 6: Проверить, что k6 доступен**

Run: `k6 version`
Expected: версия k6 (напр. `k6 v0.51.0`). Если не установлено — `winget install grafana.k6`.

- [ ] **Step 7: Commit**

```powershell
rtk git add load/.gitignore load/.env.example load/run.ps1 load/README.md
rtk git commit -m "feat(load): scaffold k6 load-testing project"
```

---

### Task 2: Общая библиотека (config + chat-body)

**Files:**
- Create: `load/lib/config.js`
- Create: `load/lib/chat-body.js`

**Interfaces:**
- Produces (потребляется всеми сценариями в Tasks 3–7):
  - `config.baseUrl` (string), `config.modelId` (string), `config.apiKeys` (string[])
  - `apiKeyForVU()` → string: ключ из пула по `__VU` (round-robin). Бросает `Error`, если пул пуст.
  - `nonStreamBody(modelId)` → string (JSON-строка тела для `/v1/chat/completions`)
  - `streamBody(modelId)` → string (JSON-строка тела для стриминга, без поля `stream` — шлюз выставит сам)

- [ ] **Step 1: Создать `lib/config.js`**

Создать `load/lib/config.js`:
```javascript
import exec from 'k6/execution';

const baseUrl = (__ENV.K6_BASE_URL || 'https://api.openrussiarouter.com').replace(/\/+$/, '');
const modelId = __ENV.K6_MODEL_ID || 'gemini-3.5-flash';

const apiKeys = (__ENV.K6_API_KEYS || '')
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);

if (apiKeys.length === 0) {
    throw new Error(
        'K6_API_KEYS не задан. Заполните load/.env (см. .env.example) — список sk-or-* ключей через запятую.'
    );
}

export const config = {
    baseUrl,
    modelId,
    apiKeys,
};

// Раздаёт ключи round-robin по VU: каждый виртуальный пользователь всегда использует один ключ.
export function apiKeyForVU() {
    const vu = exec.vu.idInTest; // 1-based
    return apiKeys[(vu - 1) % apiKeys.length];
}
```

- [ ] **Step 2: Создать `lib/chat-body.js`**

Создать `load/lib/chat-body.js`:
```javascript
// reasoning_effort:'none' обязателен: gemini-3.5-flash имеет default_effort 'high',
// и ReasoningRequestPatcher подставит его, если поле отсутствует → лишняя latency/cost.
const BASE = (modelId) => ({
    model: modelId,
    messages: [{ role: 'user', content: 'hi' }],
    max_tokens: 1,
    reasoning_effort: 'none',
});

export function nonStreamBody(modelId) {
    return JSON.stringify({ ...BASE(modelId), stream: false });
}

// Поле stream НЕ добавляем: путь /v1/chat/completions/stream сам инжектит stream:true
// + stream_options.include_usage:true через InjectStreamOptions на шлюзе.
export function streamBody(modelId) {
    return JSON.stringify(BASE(modelId));
}
```

- [ ] **Step 3: Валидация синтаксиса через k6 inspect**

Создать временный минимальный сценарий для проверки импорта. Run:
```powershell
k6 inspect load/lib/config.js
```
Expected: ошибка про `default function` — это нормально (библиотека, не сценарий). Главное — нет синтаксических ошибок. Если `k6` ругается на синтаксис (`SyntaxError`) — исправить.

- [ ] **Step 4: Commit**

```powershell
rtk git add load/lib/config.js load/lib/chat-body.js
rtk git commit -m "feat(load): shared config and request-body helpers"
```

---

### Task 3: smoke-сценарий

**Files:**
- Create: `load/scenarios/smoke.js`

**Interfaces:**
- Consumes: `config`, `apiKeyForVU` из `lib/config.js`; `nonStreamBody` из `lib/chat-body.js`.
- Produces: `smoke.js` — запускается первым перед любым тяжёлым сценарием.

- [ ] **Step 1: Создать `scenarios/smoke.js`**

Создать `load/scenarios/smoke.js`:
```javascript
import http from 'k6/http';
import { check } from 'k6';
import { config, apiKeyForVU } from '../lib/config.js';
import { nonStreamBody } from '../lib/chat-body.js';

export const options = {
    vus: 1,
    iterations: 1,
    thresholds: {
        // Без abortOnFail: smoke просто репортит. Человек смотрит результат руками.
        checks: ['rate==1'],
    },
};

export default function () {
    const res = http.post(
        `${config.baseUrl}/v1/chat/completions`,
        nonStreamBody(config.modelId),
        {
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${apiKeyForVU()}`,
            },
            timeout: '60s',
        }
    );

    check(res, {
        'status is 200': (r) => r.status === 200,
        'has choices': (r) => {
            try {
                const body = JSON.parse(r.body);
                return Array.isArray(body.choices) && body.choices.length > 0;
            } catch {
                return false;
            }
        },
    });

    console.log(`smoke: status=${res.status} ttfb_ms=${res.timings.waiting.toFixed(0)} body=${res.body?.slice(0, 300)}`);
}
```

- [ ] **Step 2: Валидация структуры (без запуска)**

Run: `k6 inspect load/scenarios/smoke.js`
Expected: вывод опций (`vus: 1, iterations: 1, ...`), без ошибок.

- [ ] **Step 3: Реальный прогон smoke против прода**

Требует заполненный `load/.env` (минимум 1 рабочий ключ + баланс). Run:
```powershell
./load/run.ps1 load/scenarios/smoke.js
```
Expected: `status is 200` ✓, `has choices` ✓, exit code 0. Если 401 — неверный ключ; 402 — нет баланса; 404 — неверный model_id (проверить через `/v1/models`); 503 — нет доступных нод.

- [ ] **Step 4: Commit**

```powershell
rtk git add load/scenarios/smoke.js
rtk git commit -m "feat(load): smoke scenario for prod readiness check"
```

---

### Task 4: models-get-сценарий

**Files:**
- Create: `load/scenarios/models-get.js`

**Interfaces:**
- Consumes: `config` из `lib/config.js`.
- Produces: `models-get.js` — лёгкая нагрузка на небиллингущийся эндпоинт.

- [ ] **Step 1: Создать `scenarios/models-get.js`**

Создать `load/scenarios/models-get.js`:
```javascript
import http from 'k6/http';
import { check } from 'k6';
import { config } from '../lib/config.js';

// GET /v1/models публичный (без Bearer), не биллингится, не ходит к upstream.
// Показывает потолок инфраструктуры шлюза без LLM.
export const options = {
    scenarios: {
        models_get: {
            executor: 'constant-arrival-rate',
            rate: 50,
            timeUnit: '1s',
            duration: '3m',
            preAllocatedVUs: 20,
            maxVUs: 100,
        },
    },
    thresholds: {
        http_req_failed: [{ threshold: 'rate<0.001', abortOnFail: true }],
        http_req_duration: ['p(95)<2000'],
    },
};

export default function () {
    const res = http.get(`${config.baseUrl}/v1/models`, { timeout: '30s' });
    check(res, { 'status is 200': (r) => r.status === 200 });
}
```

- [ ] **Step 2: Валидация структуры**

Run: `k6 inspect load/scenarios/models-get.js`
Expected: вывод опций с `executor: constant-arrival-rate`, без ошибок.

- [ ] **Step 3: Короткий сухой прогон (10с, чтобы убедиться, что 50 RPS держится)**

Временно — run:
```powershell
./load/run.ps1 load/scenarios/models-get.js --env K6_DURATION=10s
```
Примечание: `--env` здесь не переопределит duration (он зашит в сценарии). Для реальной проверки просто прервать через Ctrl-C после ~15с или дождаться конца (3 мин — лёгкий эндпоинт, дешев). Expected: ошибок нет, p95 < 2с.

- [ ] **Step 4: Commit**

```powershell
rtk git add load/scenarios/models-get.js
rtk git commit -m "feat(load): models-get scenario for infrastructure ceiling"
```

---

### Task 5: chat-ramp-сценарий (основной)

**Files:**
- Create: `load/scenarios/chat-ramp.js`

**Interfaces:**
- Consumes: `config`, `apiKeyForVU` из `lib/config.js`; `nonStreamBody` из `lib/chat-body.js`.
- Produces: `chat-ramp.js` — главный сценарий: кривая деградации 1→25 RPS.

- [ ] **Step 1: Создать `scenarios/chat-ramp.js`**

Создать `load/scenarios/chat-ramp.js`:
```javascript
import http from 'k6/http';
import { check } from 'k6';
import { config, apiKeyForVU } from '../lib/config.js';
import { nonStreamBody } from '../lib/chat-body.js';

// Основной сценарий: плавный подъём 1 -> 25 RPS, steady 5 мин, спуск до 0.
// Показывает, при каком RPS latency начинает расти и появляются первые 5xx/429.
export const options = {
    scenarios: {
        chat_ramp: {
            executor: 'ramping-arrival-rate',
            startRate: 1,
            timeUnit: '1s',
            preAllocatedVUs: 60,
            maxVUs: 200,
            stages: [
                { duration: '5m', target: 25 },
                { duration: '5m', target: 25 },
                { duration: '2m', target: 0 },
            ],
        },
    },
    thresholds: {
        http_req_failed: [{ threshold: 'rate<0.05', abortOnFail: true }],
        http_req_duration: [
            { threshold: 'p(99)<30000', abortOnFail: true },
            'p(95)<15000',
        ],
        checks: ['rate>0.95'],
    },
};

export default function () {
    const res = http.post(
        `${config.baseUrl}/v1/chat/completions`,
        nonStreamBody(config.modelId),
        {
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${apiKeyForVU()}`,
            },
            timeout: '60s',
        }
    );

    check(res, {
        'status is 200': (r) => r.status === 200,
    });
}
```

- [ ] **Step 2: Валидация структуры**

Run: `k6 inspect load/scenarios/chat-ramp.js`
Expected: три stage в `ramping-arrival-rate`, thresholds с `abortOnFail`, без ошибок.

- [ ] **Step 3: Commit**

```powershell
rtk git add load/scenarios/chat-ramp.js
rtk git commit -m "feat(load): chat-ramp scenario for degradation curve"
```

Примечание: полный прогон (~12 мин, платный) выполняется оператором вручную после smoke. Здесь — только структурная валидация.

---

### Task 6: chat-stream-сценарий (SSE + TTFB)

**Files:**
- Create: `load/scenarios/chat-stream.js`

**Interfaces:**
- Consumes: `config`, `apiKeyForVU` из `lib/config.js`; `streamBody` из `lib/chat-body.js`.
- Produces: `chat-stream.js` — нагрузка на стриминг-путь `/v1/chat/completions/stream`, кастомная метрика `ttfb_ms`.

- [ ] **Step 1: Создать `scenarios/chat-stream.js`**

Создать `load/scenarios/chat-stream.js`:
```javascript
import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';
import { config, apiKeyForVU } from '../lib/config.js';
import { streamBody } from '../lib/chat-body.js';

// Кастомная метрика time-to-first-byte. res.timings.waiting = TTFB (мс, float).
const ttfb = new Trend('ttfb_ms');

// Стриминг на отдельном пути /v1/chat/completions/stream (другой хендлер HandleStreamAsync).
// 20 VU держат долгоживущие SSE-соединения. max_tokens:1 -> ответ быстрый, но путь реальный.
export const options = {
    scenarios: {
        chat_stream: {
            executor: 'constant-vus',
            vus: 20,
            duration: '5m',
        },
    },
    thresholds: {
        http_req_failed: [{ threshold: 'rate<0.05', abortOnFail: true }],
        http_req_duration: ['p(95)<15000'],
        ttfb_ms: ['p(95)<3000'],
    },
};

export default function () {
    const res = http.post(
        `${config.baseUrl}/v1/chat/completions/stream`,
        streamBody(config.modelId),
        {
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${apiKeyForVU()}`,
                Accept: 'text/event-stream',
            },
            timeout: '120s',
        }
    );

    ttfb.add(res.timings.waiting);

    check(res, {
        'status is 200': (r) => r.status === 200,
        'is event-stream': (r) =>
            (r.headers['Content-Type'] || r.headers['content-type'] || '').includes('text/event-stream'),
    });
}
```

- [ ] **Step 2: Валидация структуры**

Run: `k6 inspect load/scenarios/chat-stream.js`
Expected: `executor: constant-vus`, `ttfb_ms` в metrics, без ошибок.

- [ ] **Step 3: Commit**

```powershell
rtk git add load/scenarios/chat-stream.js
rtk git commit -m "feat(load): chat-stream scenario with TTFB metric"
```

---

### Task 7: soak-сценарий (устойчивость во времени)

**Files:**
- Create: `load/scenarios/soak.js`

**Interfaces:**
- Consumes: `config`, `apiKeyForVU` из `lib/config.js`; `nonStreamBody` из `lib/chat-body.js`.
- Produces: `soak.js` — 15 RPS steady 15 мин, выявляет утечки/дрейф outbox.

- [ ] **Step 1: Создать `scenarios/soak.js`**

Создать `load/scenarios/soak.js`:
```javascript
import http from 'k6/http';
import { check } from 'k6';
import { config, apiKeyForVU } from '../lib/config.js';
import { nonStreamBody } from '../lib/chat-body.js';

// 15 RPS steady в течение 15 минут. Выявляет утечки памяти, дрейф outbox-диспетчера,
// рост latency со временем. Запускать ОДИН, не одновременно с другими тяжёлыми сценариями.
export const options = {
    scenarios: {
        soak: {
            executor: 'constant-arrival-rate',
            rate: 15,
            timeUnit: '1s',
            duration: '15m',
            preAllocatedVUs: 50,
            maxVUs: 150,
        },
    },
    thresholds: {
        http_req_failed: [{ threshold: 'rate<0.05', abortOnFail: true }],
        http_req_duration: [
            { threshold: 'p(99)<30000', abortOnFail: true },
            'p(95)<15000',
        ],
        checks: ['rate>0.95'],
    },
};

export default function () {
    const res = http.post(
        `${config.baseUrl}/v1/chat/completions`,
        nonStreamBody(config.modelId),
        {
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${apiKeyForVU()}`,
            },
            timeout: '60s',
        }
    );

    check(res, {
        'status is 200': (r) => r.status === 200,
    });
}
```

- [ ] **Step 2: Валидация структуры**

Run: `k6 inspect load/scenarios/soak.js`
Expected: `constant-arrival-rate` rate 15, duration 15m, без ошибок.

- [ ] **Step 3: Commit**

```powershell
rtk git add load/scenarios/soak.js
rtk git commit -m "feat(load): soak scenario for time-stability"
```

---

### Task 8: Финализация README

**Files:**
- Modify: `load/README.md` (полная замена скелета из Task 1)

**Interfaces:**
- Consumes: все сценарии из Tasks 3–7.

- [ ] **Step 1: Переписать `README.md` полным руководством**

Заменить содержимое `load/README.md` на:
```markdown
# Нагрузочное тестирование ApiGateway (k6)

Сценарии k6 для проверки стабильности OpenRussiaRouter под пиковой нагрузкой (~80
одновременных API-пользователей ≈ 10–25 RPS). Спецификация:
`docs/superpowers/specs/2026-07-29-load-testing-design.md`.

## Установка k6

```powershell
winget install grafana.k6
k6 version
```

## Подготовка данных

1. **Тестовый пользователь.** Создать пользователя с положительным балансом (иначе
   шлюз вернёт `402 billing_error`).
2. **Пул из 50 API-ключей** под этим пользователем (`/workspace/apikeys`). Rate-limit
   прода — 60 запросов/мин на ключ; один ключ задушит тест на 1 RPS. k6 раздаёт ключи
   round-robin по VU.
3. **`.env`:** `cp load/.env.example load/.env`, заполнить `K6_API_KEYS` (через запятую).
4. **Сверить модель:** `GET https://api.openrussiarouter.com/v1/models` → `K6_MODEL_ID`
   (дефолт `gemini-3.5-flash`).

## Сценарии

| Сценарий | Команда | Профиль |
|---|---|---|
| smoke | `./load/run.ps1 load/scenarios/smoke.js` | 1 VU, 1 iter — проверка готовности |
| models-get | `./load/run.ps1 load/scenarios/models-get.js` | 50 RPS, 3 мин — потолок инфраструктуры |
| chat-ramp | `./load/run.ps1 load/scenarios/chat-ramp.js` | 1→25 RPS, ~12 мин — кривая деградации |
| chat-stream | `./load/run.ps1 load/scenarios/chat-stream.js` | 20 VU, 5 мин — SSE + TTFB |
| soak | `./load/run.ps1 load/scenarios/soak.js` | 15 RPS, 15 мин — устойчивость |

## Порядок запуска

1. **Всегда `smoke` первым.** Падение = стоп, дальше не гонять.
2. Затем один тяжёлый сценарий за раз (не параллельно): `chat-ramp` ИЛИ `chat-stream`
   ИЛИ `soak`. `models-get` можно гонять независимо.
3. `thresholds.abortOnFail` стопит тест автоматически при error_rate>5% или p99>30с.

## Наблюдение за продом во время прогона

SSH-туннель к Grafana (метрики прода):
```powershell
ssh -L 3001:127.0.0.1:3001 root@201.51.6.246
# затем http://localhost:3001 — дашборды ORR
```

Смотреть: CPU/RAM контейнеров, `orr_http_requests_total`,
`orr_http_request_duration_seconds`, `orr_limit_rejections_total`, рост очереди
`gateway.usage_outbox`, Postgres-пулы.

## Безопасность

- `load/.env` gitignored — реальные ключи туда, в репо не коммитить.
- k6 гоняем с машины оператора, не с прода.
- На случай падения прода: `update-prod.sh` / `deploy-prod.sh` для рестарта.
```

- [ ] **Step 2: Commit**

```powershell
rtk git add load/README.md
rtk git commit -m "docs(load): full runbook for load-testing scenarios"
```

---

## Self-Review

**1. Spec coverage:** smoke (T3), models-get (T4), chat-ramp (T5), chat-stream (T6), soak (T7) — все 5 сценариев из спеки. Base URL/api-домен (T1 .env.example, T2 config). Пул ключей round-robin (T2 apiKeyForVU). reasoning_effort:none (T2 chat-body). thresholds с abortOnFail (T5/T6/T7). TTFB-метрика (T6). .env gitignored (T1). README runbook (T8). Раздел безопасности спеки → вне кода, операционное замечание.

**2. Placeholder scan:** плейсхолдеров нет; каждый шаг содержит полный код/команды.

**3. Type consistency:** `apiKeyForVU()` (T2) используется в T3/T5/T6/T7 идентично. `nonStreamBody(modelId)`/`streamBody(modelId)` (T2) — сигнатуры совпадают во всех сценариях. `config.baseUrl`/`config.modelId` — единые имена во всех задачах.
