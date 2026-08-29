# Reasoning / Thinking / Effort в ORR — исследование и рекомендации

> Статус: research → согласованный контракт. Дата: 2026-06-08 (контракт v1 добавлен 2026-06-09).
> Вопрос: как дать пользователю выбирать «режим рассуждений» у тех моделей, которые его поддерживают, и одинаково ли он задаётся для всех моделей.
>
> **Итог обсуждения зафиксирован в разделе [«Финальный контракт v1»](#финальный-контракт-v1-согласовано) в конце документа — он является спекой для реализации (задача `docs/tasks/TS-008.md`). Разделы 1–4 ниже — фон/обоснование.**

## TL;DR

- **Единого формата нет.** Есть два семейства: budget-стиль (`thinking.budget_tokens`, Anthropic/Gemini/GLM/DeepSeek/Qwen) и effort-стиль (`reasoning_effort: low|medium|high`, OpenAI/Codex/xAI). OpenRouter поверх них даёт зонтичный `reasoning: {...}`.
- **Тяжёлую трансляцию под провайдеров в вашей цепочке уже делает нода 9Router.** ORR-гейтвей — прозрачный прокси, он меняет только поле `model`, остальное тело уходит как есть. Клиент уже сегодня может прислать `reasoning_effort`/`thinking` — долетит до провайдера в правильном формате.
- Поэтому задача в ORR — не трансляция, а **продуктовая обвязка**: признак поддержки в каталоге, приём/валидация/нормализация одного абстрактного параметра в гейтвее, биллинг reasoning-токенов и UI-бейджи.

## 1. Как это работает у провайдеров (и почему не единообразно)

| Семейство | Кто | Как задаётся | Поле в запросе |
|---|---|---|---|
| **Budget (extended thinking)** | Anthropic Claude, Gemini, GLM, DeepSeek, Qwen, большинство | числовой бюджет токенов на размышление + вкл/выкл | `thinking: { type: "enabled", budget_tokens: 10000 }` |
| **Effort (категория)** | OpenAI (o-series, gpt-5), Codex, xAI Grok | дискретный уровень усилия | `reasoning_effort: "low" \| "medium" \| "high"` |
| **Unified** | OpenRouter (агрегатор) | зонтичный объект, нормализует оба | `reasoning: { effort: "high" }` или `reasoning: { max_tokens: 2000 }` |

Различия, которые ломают «один параметр на всех»:

- Budget-модели хотят число токенов; у Anthropic жёсткое правило `max_tokens > budget_tokens`.
- Effort-модели не принимают число — только категорию. У OpenAI ещё есть `"minimal"` (gpt-5), нет `budget`.
- Часть моделей рассуждение нельзя выключить (o1); у части оно по умолчанию выключено.
- Биллинг: reasoning-токены тарифицируются (обычно по ставке output, иногда отдельной — в 9RFork `pricing.js` для Claude есть отдельная колонка `reasoning`).

## 2. Что уже готово ниже по стеку (9Router)

ORR forward'ит на ноду как есть (`SseRequestPatcher.PatchModel` меняет только `model`). Нода 9Router нормализует reasoning под каждый провайдер:

- `9RFork/open-sse/handlers/chatCore.js:45-57` — принимает и `body.thinking` (budget), и `body.reasoning_effort` (effort); есть серверный override `providerThinking`.
- `9RFork/open-sse/translator/request/openai-to-claude.js:178-195` — конвертит OpenAI `reasoning_effort` → Claude `thinking.budget_tokens`:

  ```
  none → 0 (выкл) · low → 4096 · medium → 8192 · high → 16384 · xhigh → 32768
  ```

- `9RFork/open-sse/translator/helpers/maxTokensHelper.js:21` — сам чинит `max_tokens > budget_tokens`.
- `9RFork/src/shared/constants/providers.js:33` — `THINKING_CONFIG`: `extended` (on/off/auto) vs `effort` (none/low/medium/high), привязка `thinkingConfig` к провайдерам.

**Вывод:** трансляция под провайдеров уже есть. Но нода гарантированно конвертит лишь часть пар (например OpenAI→Claude), поэтому маппинг effort↔budget безопаснее держать ещё и у себя в ORR.

## 3. Что добавить в ORR (3 уровня)

### L1. Каталог: признак «модель поддерживает рассуждения»

Сейчас в `ModelPrice` (`src/OpenRussiaRouter.BillingService/Domain/Entities/ModelPrice.cs`) нет capability-флага. Добавить:

- `ReasoningSupport`: `none | effort | budget` (enum как у 9Router);
- опционально дефолтные пределы (`maxBudgetTokens`).

Источник истины — OpenRouter `/models`, поле `supported_parameters` (содержит `"reasoning"`/`"reasoning_effort"`). Сейчас `src/OpenRussiaRouter.WebApp/Services/Enrichment/OpenRouterCatalogClient.cs` тянет модальности, но `supported_parameters` не сохраняет (хотя уже срезает `-low/-medium/-high` суффиксы у id, строка 126). Это место, где подхватить флаг. Альтернатива/дополнение — ручная правка в Admin → Models.

Зачем: чтобы `GET /v1/models` и UI показывали, что у модели есть режим рассуждений и какого он типа.

### L2. Gateway: нормализация и валидация одного абстрактного параметра

Ввести один зонтичный вход (как у OpenRouter): принимать `reasoning: { effort | max_tokens | enabled }` и/или сырые `reasoning_effort`/`thinking`, а в патчере приводить к понятному ноде виду. Логику — рядом с `PatchModel` в `SseRequestPatcher.cs`, новый шаг патча в `ChatCompletionHandler.cs`:

- модель `ReasoningSupport=none` → **вырезать** reasoning-поля (иначе провайдер может ответить 400);
- `effort`-модель, а пришёл budget → смаппить число в категорию (и наоборот);
- валидировать диапазон budget / допустимые значения effort и вернуть OpenAI-shaped ошибку через `GatewayErrors`, а не ловить 400 от ноды.

Правильное место: метаданные модели (`ReasoningSupport`) резолвятся в `CheckBalanceAndModelAsync` вместе с `RouterModelId` — протащить флаг тем же путём через `BalanceCheckResult`.

### L3. Биллинг + UI

- **Биллинг**: reasoning-токены приходят в usage как часть completion/output (проверить `RouterUsageExtractor`/`TokenUsageExtractor` — отдельный счётчик reasoning, если нода его отдаёт, сейчас, вероятно, схлопывается в output). Решить — тарифицировать по output (просто) или завести отдельную ставку как в 9RFork.
- **UI**: сам выбор режима делает клиент (Cline, Cursor, ваш фронт) — ему достаточно capability в `/v1/models` и приёма параметра в API. В витрине ORR (`Models.cshtml.cs`) — показывать бейдж «Reasoning: effort/budget».

## 4. Рекомендация

Делать **зонтичный параметр в стиле OpenRouter** (`reasoning: {...}`), а не два сырых поля наружу.

1. **L1 + L2 сначала** — флаг в каталоге + нормализация/валидация в гейтвее. Это и есть «юзер может выбрать у поддерживающих моделей»: API принимает режим, неподдерживающие модели его не получают, кривые значения отбиваются осмысленной ошибкой.
2. Маппинг effort↔budget держать у себя в ORR, не полагаясь только на 9Router.
3. Биллинг reasoning-токенов и UI-бейджи — следующим заходом, не блокируют запуск (рассуждения и так тарифицируются как output).

Объём: трансляция под провайдеров уже готова (9Router); остаётся обвязка в трёх местах ORR — `ModelPrice` (флаг), enrichment (автодетект из `supported_parameters`), патчер запроса (приём + нормализация + валидация).

## Ссылки на код

- ORR passthrough-патчер: `src/OpenRussiaRouter.ApiGateway/OpenAi/SseRequestPatcher.cs`
- ORR обработчик: `src/OpenRussiaRouter.ApiGateway/Application/Endpoints/ChatCompletionHandler.cs`
- ORR каталог: `src/OpenRussiaRouter.BillingService/Domain/Entities/ModelPrice.cs`
- ORR enrichment: `src/OpenRussiaRouter.WebApp/Services/Enrichment/OpenRouterCatalogClient.cs`
- 9Router thinking: `9RFork/open-sse/handlers/chatCore.js`, `.../translator/request/openai-to-claude.js`, `9RFork/src/shared/constants/providers.js`

---

# Финальный контракт v1 (согласовано)

Раздел фиксирует то, что реализуем. Принцип: **ORR решает, что разрешено и показывается пользователю; 9R решает, как доставить выбранный режим конкретному провайдеру.** ORR не учит особенности Claude/Gemini/OpenAI — он работает только со словарём «уровней усилия» (effort).

## Публичный контракт API

`POST /v1/chat/completions` принимает оба распространённых варианта:

```jsonc
{ "reasoning": { "effort": "high" } }   // зонтичный (OpenRouter-стиль)
{ "reasoning_effort": "high" }          // плоский (OpenAI-стиль)
```

Перед отправкой в 9R ORR приводит их к **одному плоскому виду**:

```jsonc
{ "reasoning_effort": "high" }
```

Шлём **только плоское поле**, не оба. Причина — техническая находка по коду 9R: его переводчики в формат провайдера (`openai-to-claude.js:181`, `openai-to-gemini.js:234`) и provider-override (`chatCore.js:55`) читают **плоское** `reasoning_effort`. Вложенный `reasoning.effort` понимают лишь отдельные нативные пути (Codex/Responses: `codex.js:417`, `kiroConstants.js:108`). Если переслать вложенный объект на Claude/Gemini — рассуждение **молча потеряется**, а юзер заплатит за «усиленный» ответ, которого не было. Поэтому нормализация в плоское поле обязательна (это переименование поля, а не знание провайдера).

## Полный пример (запрос → что уходит в 9R → /v1/models)

**Запрос клиента** `POST /v1/chat/completions` (плоский и зонтичный варианты эквивалентны):

```jsonc
{
  "model": "RWT/GLM-4.7",
  "messages": [{ "role": "user", "content": "Реши задачу пошагово" }],
  "reasoning_effort": "high"
  // либо: "reasoning": { "effort": "high" }
}
```

**Что ORR форвардит в 9R** — `model` заменён на upstream-id, reasoning схлопнут в одно плоское поле (вложенный `reasoning` удалён):

```jsonc
{
  "model": "glm-4.7",
  "messages": [{ "role": "user", "content": "Реши задачу пошагово" }],
  "reasoning_effort": "high"
}
```

- **Выключить** размышление у модели, где оно вкл по дефолту: `"reasoning_effort": "none"`.
- **Не прислать** reasoning вовсе → ORR тело не трогает, действует дефолт модели.

## Правила Gateway (порядок проверок — детерминированный)

Проверки идут строго в этом порядке, чтобы на кривой запрос всегда возвращался один и тот же код:

**Этап 1 — shape (форма запроса):**

| Ситуация | Действие |
|---|---|
| Юзер ничего не указал | ORR **ничего не добавляет**; работают дефолты 9R/провайдера |
| `reasoning: { max_tokens }` или `reasoning: { enabled }` | **v1 не поддерживает** → `400 unsupported_parameter` |
| Указаны оба поля, **значения совпадают** | принять, оставить только `reasoning_effort` |
| Указаны оба поля, **значения разные** | `400 conflicting_parameters` |
| Указано одно поле | нормализовать регистр (`"High"` → `"high"`), идти на этап 2 |

**Этап 2 — capability (по каталогу модели):**

| Ситуация | Действие |
|---|---|
| Модель не поддерживает reasoning (`SupportsReasoning=false`) | `400 unsupported_parameter` |
| Уровень не входит в `AllowedReasoningEfforts` | `400 unsupported_reasoning_effort` |
| Всё валидно | положить плоский `reasoning_effort` в тело, переслать в 9R |

Пример тела ошибки (OpenAI-shaped, как остальные ошибки гейтвея):

```jsonc
{
  "error": {
    "code": "unsupported_reasoning_effort",
    "message": "Model supports reasoning efforts: low, medium, high",
    "type": "invalid_request_error",
    "request_id": "..."
  }
}
```

### `none` — это значение, а не отсутствие

Важно не схлопывать два разных случая:

- **поле не прислано** → ORR не трогает тело → действует **дефолт модели** (а у некоторых моделей thinking включён по умолчанию);
- **`effort: "none"`** → это **явное «выключить рассуждение»** (9R мапит `none → budget 0`). Единственный способ дать юзеру выключить thinking у модели, где он включён по дефолту. В UI это отдельный пункт «Off», а не пустой выбор.

Поэтому `none` разрешён как значение `AllowedReasoningEfforts`, но это осознанный выбор админа per-model.

## Каталог модели

Хранить минимум:

```
SupportsReasoning:        bool
AllowedReasoningEfforts:  string[]   // напр. ["low","medium","high"] или ["none","low","medium","high"]
DefaultReasoningEffort:   string?    // ТОЛЬКО подсказка UI (что пред-выбрать), ORR её НЕ инжектит на сервере
```

Глобальный enum как жёсткий per-model список не нужен. Но есть **канонический словарь** для проверки формата при сохранении модели (чтобы админ не записал `super-ultra-high`):

```
none | minimal | low | medium | high | xhigh
```

### Инварианты при сохранении модели (валидация в админке/Billing)

- каждое значение `AllowedReasoningEfforts` ∈ канонический словарь;
- `DefaultReasoningEffort` (если задан) ∈ `AllowedReasoningEfforts`;
- если `SupportsReasoning=false` → `AllowedReasoningEfforts` пуст и `DefaultReasoningEffort` пуст.

Иначе админ соберёт модель, где дефолт не входит в разрешённые, и юзер получит 400 на штатный пресет.

## Что в `GET /v1/models`

Отдаём capability **двумя** способами, иначе внешние клиенты reasoning не увидят:

1. **`supported_parameters`** (OpenRouter-стиль) — **именно его читают клиенты** (Kilo Code, Cline, Roo и пр.), чтобы показать пикер reasoning. Для reasoning-моделей в массив добавляются `reasoning` и `reasoning_effort`.
2. **`capabilities.reasoning`** (наш формат) — для витрины ORR; внешние клиенты его игнорируют.

```jsonc
{
  "id": "claude-sonnet-4.6",
  "supported_parameters": ["tools","max_tokens","temperature","reasoning","reasoning_effort"],
  "capabilities": { "reasoning": { "supported": true, "efforts": ["low","medium","high"] } }
}
// модель без reasoning (в supported_parameters нет "reasoning"):
{ "id": "gpt-4o",
  "supported_parameters": ["tools","max_tokens","temperature"],
  "capabilities": { "reasoning": { "supported": false } } }
```

> Если клиент всё равно не показывает пикер — он, вероятно, опирается на собственный каталог моделей (напр. **opencode** → models.dev), а не на `/v1/models`. Тогда reasoning включается в конфиге клиента per-model, со стороны ORR это не лечится.

## Скоуп v1 — что НЕ делаем

- `reasoning: { max_tokens: N }` — budget наружу **не показываем** (плохо переносится между моделями) → явный `400`.
- `reasoning: { enabled: true }` — неоднозначная семантика → явный `400`.
- Биллинг reasoning-токенов отдельной ставкой — **позже**. Но в v1 проверить, что `RouterUsageExtractor`/`TokenUsageExtractor` не теряет reasoning-токены (обычно приходят в usage как часть output или отдельным `reasoning_tokens`); тарифицировать пока по output-ставке. Не блокер релиза, но если usage схлопнет их в ноль — дорогой ответ уйдёт дешевле себестоимости.

## Разделение ответственности (итог)

| Слой | Делает |
|---|---|
| **ORR каталог** | `SupportsReasoning`, `AllowedReasoningEfforts[]`, `DefaultReasoningEffort?` (UI-хинт) |
| **ORR gateway** | shape-проверка → capability-проверка → иначе чистый `400`; нормализация `reasoning.effort` → плоский `reasoning_effort` |
| **9R** | по реальному провайдеру: `reasoning_effort` → Claude `thinking.budget_tokens` / Gemini thinkingConfig / OpenAI как есть |
| **юзер не прислал reasoning** | ORR не трогает, дефолты снизу |

> Реализация разбита по файлам в `docs/tasks/TS-008.md`.

---

## Контракт v2: синхронизация с 9Router и fail-soft (2026-08-29)

Этот раздел заменяет противоречащие ему правила v1.

- 9Router публикует для каждой модели точный `caps.reasoningEfforts`; это единственный
  источник доступных уровней ORR. Поля `thinkingFormat` и `thinkingOptions` остаются
  диагностическими и обратносуместимыми.
- Для публичной combo-модели `reasoningEfforts` — пересечение уровней **всех** fallback-моделей,
  а не уровни первой модели и не их объединение. 9Router также публикует
  `comboReasoningMembers` для диагностики: после ручной синхронизации форма показывает
  каждую внутреннюю модель и её уровни. Для эквивалентных fallback-настроек пересечение
  fail-closed: любое различие списков либо пустой список одного fallback даёт пустой
  effective-список. Он отключает управляемый reasoning и очищает default.
- Collector передаёт уровни в WebApp. В `/admin/models?action=edit` кнопка
  **Sync reasoning from 9Router** заполняет форму и не сохраняет её автоматически;
  администратор проверяет результат и нажимает Save.
- Непустой список уровней определяет публичную поддержку reasoning. Администратор может
  сохранить только подмножество списка 9Router; флаг `ReasoningEffortsOverridden` отмечает
  это ограничение. При отсутствии override фоновая синхронизация заменяет список значениями
  9Router, при override пересекает его с актуальными возможностями.
- Gateway всегда продолжает корректный запрос: неизвестный, конфликтующий или неверно
  сформированный reasoning заменяется default-уровнем. Если модель не поддерживает
  управляемый reasoning, все reasoning-поля удаляются до отправки на ноду. Ошибка
  `reasoning not supported` клиенту не возвращается.
- `GET /v1/models` публикует `reasoning.supported_efforts` и
  `reasoning.default_effort` только для моделей с непустым эффективным списком.
  Клиенты могут обнаруживать возможности через этот endpoint. Обычный custom-provider
  Pi не опрашивает его автоматически: для Pi требуется вручную обновить `models.json`
  либо использовать отдельное расширение, которого ORR не поставляет.
