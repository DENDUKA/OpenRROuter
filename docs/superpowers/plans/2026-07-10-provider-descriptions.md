# Provider Descriptions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish clear Russian and English provider descriptions for every production provider card.

**Architecture:** Content is stored directly in the `description_ru` and `description_en` fields of the existing `billing.model_providers` records. The update preserves provider metadata, models, prices, and logos, then reads the six records back to verify publication.

**Tech Stack:** PostgreSQL 16, production Docker Compose deployment, `psql`.

---

### Task 1: Prepare the bilingual provider copy

**Files:**
- Create: `docs/superpowers/plans/2026-07-10-provider-descriptions.md`
- Modify: production `billing.model_providers.description_ru`
- Modify: production `billing.model_providers.description_en`

- [ ] **Step 1: Use the following approved provider descriptions**

| Provider | Description (RU) | Description (EN) |
|---|---|---|
| Anthropic | Anthropic разрабатывает семейство Claude — модели общего назначения с сильным фокусом на надёжное рассуждение, работу с текстом и инструментами. Линейка включает флагманский Claude Opus для наиболее сложных задач, сбалансированный Claude Sonnet для повседневных рабочих сценариев и быстрый Claude Haiku для задач с высокой частотой запросов. Claude хорошо подходит для анализа и обобщения документов, сложного программирования, подготовки текстов, исследовательских задач и агентных процессов, где важны качество ответа и следование инструкциям. | Anthropic develops the Claude family of general-purpose models, with a strong focus on reliable reasoning, text work, and tool use. The line includes flagship Claude Opus for the most demanding tasks, balanced Claude Sonnet for everyday work, and fast Claude Haiku for high-throughput requests. Claude is a strong fit for document analysis and summarisation, complex software engineering, writing, research, and agentic workflows where response quality and instruction following matter. |
| Google | Google развивает семейство Gemini — мультимодальные модели для работы с текстом, изображениями, аудио, видео и кодом. Флагманские модели Gemini Pro рассчитаны на глубокое рассуждение, сложное решение задач и агентные сценарии, а варианты Flash ориентированы на скорость и масштабируемую обработку. Gemini особенно полезен для анализа больших массивов информации, работы с длинными документами, мультимодального понимания, поиска и подготовки контента, а также для разработки и автоматизации процессов. | Google develops the Gemini family of multimodal models for text, images, audio, video, and code. Flagship Gemini Pro models target deep reasoning, complex problem solving, and agentic scenarios, while Flash variants focus on speed and scalable processing. Gemini is especially useful for analysing large information sets, working with long documents, multimodal understanding, search and content preparation, as well as software development and workflow automation. |
| MiniMax | MiniMax создаёт модели для диалоговых приложений, генерации контента и интеллектуальных агентов. Флагманская линейка MiniMax M сочетает сильные возможности в программировании и рассуждении с поддержкой очень длинного контекста и мультимодальных сценариев. Модели MiniMax стоит выбирать для работы с крупными кодовыми базами и документами, многошаговых агентных задач, подготовки контента, анализа изображений и запросов, которым требуется удерживать большой объём контекста. | MiniMax builds models for conversational applications, content generation, and intelligent agents. The flagship MiniMax M line combines strong coding and reasoning capabilities with very long-context support and multimodal workflows. MiniMax models are well suited to large codebases and document collections, multi-step agent tasks, content production, image analysis, and requests that need to retain a substantial amount of context. |
| OpenAI | OpenAI развивает универсальную экосистему моделей GPT и специализированных моделей для reasoning, изображений, речи и работы в реальном времени. Флагманские GPT-модели предназначены для сложных аналитических, творческих и инженерных задач; компактные версии помогают снизить задержку и стоимость массовых операций. Модели OpenAI подходят для ассистентов, разработки и ревью кода, анализа данных и документов, поиска решений в многошаговых задачах, генерации изображений, голосовых интерфейсов и автоматизации бизнес-процессов. | OpenAI develops a general-purpose ecosystem of GPT models and specialised models for reasoning, images, speech, and real-time interactions. Flagship GPT models target demanding analytical, creative, and engineering work, while smaller variants help reduce latency and cost for high-volume operations. OpenAI models fit assistants, code generation and review, data and document analysis, multi-step problem solving, image generation, voice interfaces, and business-process automation. |
| Xiaomi | Xiaomi развивает семейство MiMo для рассуждений, программирования и агентных приложений. Флагманская модель MiMo-V2.5-Pro ориентирована на сложный анализ, глубокое рассуждение и обработку длинных документов; линейка также может включать более быстрые варианты для повседневных запросов. MiMo подходит для технических и исследовательских задач, написания и отладки кода, планирования многошаговых действий, работы с объёмными текстами и создания интеллектуальных помощников. | Xiaomi develops the MiMo family for reasoning, software engineering, and agent applications. Its flagship MiMo-V2.5-Pro model targets complex analysis, deep reasoning, and long-document processing, while the line can also include faster variants for everyday requests. MiMo is a good choice for technical and research tasks, writing and debugging code, planning multi-step actions, working with substantial text collections, and building intelligent assistants. |
| Z.ai | Z.ai развивает семейство GLM — модели общего назначения с акцентом на программирование, рассуждение и агентную инженерную работу. Флагманская линейка GLM-5 рассчитана на сложные задачи разработки, длительные последовательности действий и работу с инструментами; более быстрые варианты позволяют оптимизировать интерактивные сценарии. GLM хорошо подходит для генерации и рефакторинга кода, отладки, технического анализа, многошаговых агентов, обработки длинного контекста и автоматизации инженерных процессов. | Z.ai develops the GLM family of general-purpose models, with an emphasis on coding, reasoning, and agentic engineering. The flagship GLM-5 line targets demanding development tasks, long action sequences, and tool use, while faster variants can optimise interactive scenarios. GLM is well suited to code generation and refactoring, debugging, technical analysis, multi-step agents, long-context work, and engineering-process automation. |

- [ ] **Step 2: Review factual boundaries**

Ensure every description names provider families and typical use cases without claiming that a model is enabled, priced, or available through the gateway. Preserve the distinction between model families and any deployment-specific catalogue.

### Task 2: Publish only the two description fields

**Files:**
- Modify: production `billing.model_providers.description_ru`
- Modify: production `billing.model_providers.description_en`

- [ ] **Step 1: Update all six records in one transaction**

Run a parameterised `UPDATE` statement against `billing.model_providers`, matching the existing IDs exactly: `antropic`, `google`, `minimax`, `openai`, `xiaomi`, and `z.ai`. Set only `description_ru`, `description_en`, and `updated_at_utc = now()`.

- [ ] **Step 2: Verify the update before commit**

Run:

```sql
SELECT id,
       length(description_ru) AS ru_length,
       length(description_en) AS en_length
FROM billing.model_providers
ORDER BY id;
```

Expected: six rows, with positive RU and EN lengths for every provider.

- [ ] **Step 3: Commit the transaction**

Expected: only description fields and timestamps change; IDs, logos, provider display names, models, and prices remain untouched.

### Task 3: Verify the published result

**Files:**
- Modify: production `billing.model_providers.description_ru`
- Modify: production `billing.model_providers.description_en`

- [ ] **Step 1: Read the persisted content**

Run:

```sql
SELECT id, description_ru, description_en
FROM billing.model_providers
ORDER BY id;
```

Expected: six non-empty RU/EN description pairs matching Task 1.

- [ ] **Step 2: Report the outcome**

State that descriptions have been published for all six providers and that model availability was intentionally not changed.
