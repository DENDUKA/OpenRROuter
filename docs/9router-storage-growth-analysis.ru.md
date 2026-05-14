# 9Router: рост локального хранилища — анализ и направление решения

- Дата: 2026-05-14
- Статус: design decisions зафиксированы, готово к нарезке на ТЗ
- Скоуп: `src/OpenRussiaRouter.9Router` (vendored fork upstream 9Router)

Документ фиксирует результаты разбора темы «локальная БД ноды быстро разрастается» и обосновывает выбранное направление решения. Служит входными данными для последующего ТЗ.

## TL;DR

- На активной ноде с одним пользователем и плотным использованием китайских LLM (GLM / QWEN / MiniMax) наблюдается рост `/opt/9router/data/db/` до **~1 ГБ за месяц**.
- Причина роста — **не движок** (SQLite). Это сочетание: `usageHistory` без ретеншена (растёт линейно) + директория `backups/` × до 5 копий БД (множитель на каждом upgrade / schema migration).
- **Замена SQLite на Postgres проблему не решает** — те же данные в pg будут занимать больше (MVCC + TOAST), плюс операционная нагрузка ×N нод.
- Целевой драйвер для ORR-managed prod-нод — **`better-sqlite3` native**. Описанные ниже стратегии (батчевые DELETE, `incremental_vacuum`, `VACUUM`) проверены штатно только для него; для sql.js fallback retention работает best-effort.
- Решение — три независимых тактических рычага: ретеншн `usageHistory` (с переводом в `auto_vacuum = INCREMENTAL` как отдельным one-shot maintenance шагом, а не автоматически на старте) + ограничение `backups/` до 1 копии + понижение дефолтов observability. Стратегически — **durable outbox на Gateway** перед `BillingService.RecordUsage` (а не «стримить с ноды»).
- Никаких изменений в схеме персистентности апстрима не требуется. Патчи локализуются в `src/lib/db/`, что сохраняет merge-friendly статус vendored fork.

## Контекст

`src/OpenRussiaRouter.9Router` — vendored fork upstream 9Router, который в продакшене играет роль **внешней ноды роутинга** в пуле, управляемом нашим `ControlPlane`. Локальная БД ноды — это `SQLite` (`better-sqlite3` с fallback на `sql.js`), файл `<DATA_DIR>/db/data.sqlite`.

Изначальная тревога была сформулирована как «БД слишком быстро разрастается, пишет всё подряд». В ходе обсуждения выяснилось:

- На тестовой ноде `91.186.216.209` файл всего **192 КБ**, 13 записей в `requestDetails`, 4 в `usageHistory` — рост ещё не материализовался, нода обкатана только ping-pong запросами на одном провайдере (MiniMax).
- На другой инсталляции под реальной нагрузкой за месяц активного кодинга через китайские LLM наблюдался рост до **~1 ГБ на одного пользователя**.

Эти две точки — крайности одного профиля, и решение должно опираться на верхнюю.

## Что в БД и как оно растёт

Схема описана в [src/OpenRussiaRouter.9Router/src/lib/db/schema.js](../src/OpenRussiaRouter.9Router/src/lib/db/schema.js). Ключевые таблицы по поведению роста:

| Таблица | Что пишет | Ретеншн в коде | Поведение роста |
|---|---|---|---|
| `requestDetails` | Полные дампы request / providerRequest / providerResponse / response (JSON, каждое поле truncate до `observabilityMaxJsonSize` = 5 КБ по умолчанию) | **Есть.** Кольцо на `observabilityMaxRecords` = 1000 записей ([requestDetailsRepo.js:109-115](../src/OpenRussiaRouter.9Router/src/lib/db/repos/requestDetailsRepo.js#L109-L115)) | Потолок ~20 МБ (4 поля × 5 КБ × 1000). Не растёт линейно. |
| `usageHistory` | По одной строке на запрос: tokens / cost / status / endpoint / connectionId | **Нет.** Append-only, никаких DELETE / TTL по коду нет | Линейный рост. При активном кодинге (3–5K req/day) — ~100–200 МБ/мес с учётом 4 индексов. |
| `usageDaily` | Агрегаты по дню (JSON в одной строке на день) | Нет, 1 строка/день. На длинном горизонте ~50 КБ × 365 = ~18 МБ/год | Растёт медленно. Технический долг — см. ниже. |
| `backups/` (директория) | Полные копии `data.sqlite` через `fs.copyFileSync` ([backup.js:21](../src/OpenRussiaRouter.9Router/src/lib/db/backup.js#L21)). **Триггер не периодический** — только при app version bump или применении schema migration ([migrate.js:233-247](../src/OpenRussiaRouter.9Router/src/lib/db/migrate.js#L233-L247)) | **Есть.** `KEEP_BACKUPS = 5` захардкожено ([backup.js:6](../src/OpenRussiaRouter.9Router/src/lib/db/backup.js#L6)) | До × 5 множитель, но не «само со временем» — растёт скачком при апгрейдах. |

### Декомпозиция 1 ГБ/мес

Под профиль «1 пользователь × плотное использование GLM / QWEN / MiniMax × 30 дней», теоретическая оценка:

| Источник | Оценка |
| --- | --- |
| `usageHistory` (данные + индексы) | 100–200 МБ |
| `requestDetails` (потолок) | до 20 МБ |
| `usageDaily` + остальные таблицы | < 5 МБ |
| **Активный файл `data.sqlite`** | **~150–200 МБ** |
| `backups/` (1–5 копий, зависит от числа апгрейдов в окне) | **до ~1 ГБ при стечении (5 копий по 200 МБ)** |
| **Итого `/opt/9router/data/db/`** | **до ~1.2 ГБ** |

Долевое соотношение между `usageHistory` и `backups/` зависит от того, **как часто за месяц происходили апгрейды**: апстрим 9Router выпускает релизы регулярно, и каждый bump app version создаёт новую копию БД. В пределе (5 апгрейдов в окне) бэкапы дают ~80% объёма; при отсутствии апгрейдов — практически 0%. Без фактического `du -sh /opt/9router/data/db/*` на проблемной ноде точное соотношение не известно — но это не блокирует план: оба рычага (рычаги 1 и 2) включены в работу независимо.

## Почему не Postgres

Технически замена возможна — в репозитории чистый адаптерный слой ([driver.js](../src/OpenRussiaRouter.9Router/src/lib/db/driver.js) + четыре SQLite-адаптера в [adapters/](../src/OpenRussiaRouter.9Router/src/lib/db/adapters)). Но это **решение не той проблемы**:

1. **Postgres не уменьшит данные.** Те же ~150 МБ `usageHistory` в pg займут больше: page size 8 КБ против 4 КБ у SQLite, MVCC versioning, TOAST overhead для JSON.
2. **Бэкапы pg тоже размножаются** — `pg_dump` × N снапшотов или WAL archive дают тот же × 5 множитель, если их не чистить. Просто другой механизм.
3. **Аргумент «SQLite плохо на запись в много потоков» некорректен для нашего рантайма.** 9Router — один процесс Node.js, JS однопоточный, WAL включён ([schema.js:4](../src/OpenRussiaRouter.9Router/src/lib/db/schema.js#L4)). Контеншена на запись физически нет.
4. **Vendored fork.** Переписать слой персистентности — это переход на async-API во всех 10 репозиториях ([usageRepo.js](../src/OpenRussiaRouter.9Router/src/lib/db/repos/usageRepo.js), [requestDetailsRepo.js](../src/OpenRussiaRouter.9Router/src/lib/db/repos/requestDetailsRepo.js) и др.) + переписать SQL (`INSERT OR REPLACE` → `ON CONFLICT`, `AUTOINCREMENT` → `IDENTITY`, выкинуть PRAGMA). Каждый upstream-pull превращается в merge-ад.
5. **Per-node Postgres ×N** — отдельный pg на каждой VPS-ноде в пуле — операционно неудобно. Общий pg для всех нод — узкое место на горячем пути и сетевая латентность.

Postgres у нас в архитектуре **уже есть** — на `BillingService` ([docs/prd/open-russia-router-prd.md](prd/open-russia-router-prd.md)). Туда и должен стекаться централизованный учёт. Это не аргумент против — это «правильное место для pg уже выбрано, не нужно второе».

## Принятые решения

Закрытые точки, по которым делается ТЗ.

| # | Вопрос | Решение |
| --- | --- | --- |
| 1 | Локальные бэкапы | **Оставить 1 копию, без UI.** Дефолт `BACKUPS_KEEP_COUNT = 1`, читается **из env-only**. VPS-снапшоты и (при наличии) cloud sync покрывают остальное. UI-управления нет, потому что `backup.js` сейчас не зависит от БД — добавлять туда async-чтение `settings` ради одной настройки нерационально. Значение передаётся параметром в `pruneOldBackups(maxKeep)` из `migrate.js`. |
| 2 | Источник конфигурации (для retention / observability) | **`raw?.field ?? env ?? default`** через прямое чтение из таблицы `settings` без подмешивания дефолтов. **Не** копировать текущий паттерн observability в [requestDetailsRepo.js:19-28](../src/OpenRussiaRouter.9Router/src/lib/db/repos/requestDetailsRepo.js#L19-L28) — он содержит известный баг (env-fallback недостижим, см. секцию «Известные проблемы»). |
| 3 | Cron-механизм ретеншена | **`setInterval` внутри Next.js**, по образцу observability flush в [requestDetailsRepo.js:183-200](../src/OpenRussiaRouter.9Router/src/lib/db/repos/requestDetailsRepo.js#L183-L200). Таймер `unref()`, периодичность раз в час. Если процесс упал между тиками — следующий старт всё доберёт. |
| 4 | Совместимость с upstream | **Новые файлы + минимум inline.** Вся новая логика — в отдельных файлах под `src/lib/db/repos/` (например, `usageRepoRetention.js`) или подпапке `src/lib/db/orr/`. Неизбежные inline-правки (`KEEP_BACKUPS` в `backup.js`, init-хук) — с маркером `// ORR-PATCH BEGIN` / `// ORR-PATCH END`. |
| 5 | Дефолты для prod | **Сбалансированный профиль:** `BACKUPS_KEEP_COUNT=1`, `usageHistoryRetentionDays=30`, `observabilityMaxRecords=200`, `observabilityMaxJsonSize=2 КБ`. Эти дефолты применимы к ORR-managed нодам; оператор может временно повысить observability через UI на время отладки прод-инцидента. |
| 6 | `usageHistoryEnabled` (глобальный флажок выключить локальный лог) | **Отказались.** `usageHistory` используется не только для аудита, но и для recent requests / last10min / 24h-stats / recent logs в UI ([usageRepo.js](../src/OpenRussiaRouter.9Router/src/lib/db/repos/usageRepo.js)). Полное отключение сломает дашборд ноды. Оставляем только ретеншн по времени. |
| 7 | Тайминг централизации (рычаг 4) | **После рычагов 1–3.** Локальные фиксы дают комфортный потолок за дни. Стратегическая ветка — это **outbox-доставка на Gateway**, см. раздел 4 ниже; идёт отдельной вехой. |

## Решение в деталях

Три независимых тактических рычага + одна стратегическая ветка, в порядке убывания эффекта на «1 ГБ → норма».

### 1. Ограничить локальные бэкапы

`KEEP_BACKUPS` в [backup.js:6](../src/OpenRussiaRouter.9Router/src/lib/db/backup.js#L6) — захардкоженная константа. Делаем параметром:

- env `BACKUPS_KEEP_COUNT`, дефолт **1**, значение `0` отключает бэкапы полностью,
- значение **не** читается из `settings` — `backup.js` сейчас чисто `fs`-зависимый модуль, добавлять async-чтение БД ради одного числа нерационально,
- `pruneOldBackups()` принимает параметр `maxKeep`, вызывающий код в [migrate.js:223,240,246](../src/OpenRussiaRouter.9Router/src/lib/db/migrate.js#L223) передаёт его (читает env один раз при старте),
- inline-правка в `backup.js` помечается `// ORR-PATCH`.

**Эффект ограничен:** бэкапы создаются только при апгрейдах ([migrate.js:233-247](../src/OpenRussiaRouter.9Router/src/lib/db/migrate.js#L233-L247)), не периодически. На ноде, где апгрейды редки, рычаг даёт мало. На ноде, где апстрим часто бампается — даёт ощутимое сокращение.

### 2. Ретеншн `usageHistory` — главный линейный рычаг

Состоит из двух компонент с **разным» жизненным циклом**: штатный фоновый ретеншн (выполняется постоянно) + отдельный maintenance-шаг для существующих БД (запускается оператором один раз).

**Целевой драйвер.** Всё, описанное ниже (батчевые DELETE, `incremental_vacuum`, `VACUUM`), штатно проверено только для **`better-sqlite3` native** — это целевой драйвер для ORR-managed prod-нод. Для sql.js / node:sqlite fallback retention поддерживается best-effort: батчевые DELETE работают, но reclaim места на диске и поведение `VACUUM` под WebAssembly-рантаймом не гарантируются и должны валидироваться отдельно. При обнаружении sql.js драйвера retention-job логирует это и продолжает работу с консервативной стратегией (только DELETE, без VACUUM).

**Конфигурация** (по схеме `raw?.field ?? env ?? default`):

- `usageHistoryRetentionDays` (env `USAGE_HISTORY_RETENTION_DAYS`), default **30**,
- `usageHistoryRetentionIntervalMs` (env `USAGE_HISTORY_RETENTION_INTERVAL_MS`), default `3600000` (раз в час),
- `usageHistoryRetentionBatchSize` (env `USAGE_HISTORY_RETENTION_BATCH_SIZE`), default `10000` — лимит DELETE за итерацию.

`usageHistoryEnabled` **не вводим** (см. решение #6 в таблице выше).

#### 2a. Фоновый retention job (штатный, постоянный)

Новый файл `src/lib/db/repos/usageRepoRetention.js`:

```text
setInterval(unref) → tick():
  loop:
    DELETE FROM usageHistory
    WHERE id IN (
      SELECT id FROM usageHistory WHERE timestamp < ? LIMIT ?
    )
    if rowsAffected == 0: break
    await new Promise(setImmediate)         // yield event loop
  if auto_vacuum == INCREMENTAL:
    PRAGMA incremental_vacuum(N)            // освободить страницы → ОС
  log: deleted={N} rows, vacuumed={M} pages, mode={incremental|skip}
```

- `setImmediate` между итерациями обязателен — `better-sqlite3` синхронный, без yield event loop Node.js залипает при DELETE миллионов строк,
- `incremental_vacuum` срабатывает только если БД уже в `auto_vacuum = INCREMENTAL` режиме (проверяется `PRAGMA auto_vacuum`); иначе — пропуск с логом, оператор знает что нужно запустить maintenance (см. 2b),
- `usageDaily` **не** трогаем — он маленький и нужен для агрегатных графиков на длинных периодах (см. «технический долг» ниже).

#### 2b. Переход в incremental_vacuum mode (one-shot maintenance)

SQLite по умолчанию `auto_vacuum = NONE` (проверено: в [schema.js:4-12](../src/OpenRussiaRouter.9Router/src/lib/db/schema.js#L4-L12) этого PRAGMA нет). `DELETE` освобождает страницы внутри файла, но **не сжимает файл на диске**. Чтобы это поведение появилось, нужно перевести БД в `auto_vacuum = INCREMENTAL` — а это **требует полного `VACUUM`** для существующих БД (устройство SQLite, не наша прихоть).

**`VACUUM` — не безобидная операция:**

- перестраивает всю БД, создаёт временную копию: требует ~2× размера файла свободного места на диске;
- блокирует пишущие транзакции на всё время выполнения (на 1 ГБ — секунды, на 10+ ГБ — минуты);
- может провалиться при недостатке места, активной долгой транзакции, нехватке прав на временный файл.

Поэтому **не делаем разовый `VACUUM` автоматически на старте retention job'а** — это превратит фоновую уборку в мини-операцию обслуживания, которая может упасть посреди ночи.

**Делаем отдельный maintenance-шаг:**

- новый npm-script `npm run db:enable-incremental-vacuum` (или эквивалент через admin endpoint),
- env-флаг `ORR_AUTO_RUN_DB_MAINTENANCE=true` для opt-in автозапуска при старте контейнера (для прод-деплоев, которые хотят автоматизации),
- скрипт делает:
  1. читает размер `data.sqlite` и доступное место на диске; отказывает, если `free_space < 2.2 * db_size`,
  2. логирует план: «`VACUUM` на файле {N} МБ, ожидаемое время {M} сек, требуется {2N} МБ свободного места»,
  3. `PRAGMA auto_vacuum = INCREMENTAL` → `VACUUM`,
  4. фиксирует факт в `_meta`: `autoVacuumMode = incremental`, `autoVacuumMigratedAt = <ISO>`,
  5. логирует результат: «`VACUUM` завершён за {T} сек, новый размер {N'} МБ».

На свежих нодах перевод происходит при первом старте автоматически и мгновенно: добавляем `PRAGMA auto_vacuum = INCREMENTAL` в [schema.js:4-12](../src/OpenRussiaRouter.9Router/src/lib/db/schema.js#L4-L12) PRAGMA-блоке (inline-правка с маркером); на пустой БД это бесплатно. Старые БД переводятся через явный maintenance-запуск оператором.

**Подключение фонового job'а** — из init-хука 9Router (`src/shared/services/initializeApp.js` или эквивалент), inline-правка с маркером.

### 3. Понизить дефолты observability на prod-нодах

Параметры **уже настраиваемы** через [settingsRepo.js:26-30](../src/OpenRussiaRouter.9Router/src/lib/db/repos/settingsRepo.js#L26-L30). Меняем дефолтные значения в коде:

- `observabilityMaxRecords`: 1000 → **200**,
- `observabilityMaxJsonSize`: 5 → **2** КБ,
- `enableObservability` остаётся `true`,
- env-переменные документировать в `.env.example` (`OBSERVABILITY_MAX_RECORDS`, `OBSERVABILITY_MAX_JSON_SIZE`, `OBSERVABILITY_BATCH_SIZE`, `OBSERVABILITY_FLUSH_INTERVAL_MS`) — сейчас читаются из кода, но в `.env.example` отсутствуют.

Потолок `requestDetails`: 20 МБ → ~2 МБ (4 поля × 2 КБ × 200 записей + overhead). Для отладки конкретного инцидента оператор временно поднимает значения через UI.

**Важно:** одновременно нужно починить шаблон чтения этих параметров — текущий код не даёт env-fallback (см. «Известные проблемы»). Иначе изменение дефолтов сработает только для свежих нод, где в `settings` нет соответствующего поля.

### 4. Стратегически — durable outbox на стороне Gateway

Реальная архитектура **отличается** от того, как было сформулировано в первой версии документа. По PRD и текущему коду ([Startup.cs:132](../src/OpenRussiaRouter.ApiGateway/Startup.cs#L132)) usage идёт с **Gateway** в `BillingService.RecordUsage` после ответа 9Router, причём fire-and-forget. **Gateway — источник истины** учёта, а не нода. Локальный `usageHistory` на 9Router — это **дубликат**, существующий только для дашборда самой ноды.

Поэтому стратегическая ветка — не «перенести учёт на нодах в центр», а **закрыть durability на Gateway**:

- outbox **обязательно на стороне Gateway** — в его локальном persistent store (Postgres схема Gateway, или отдельный лёгкий store). Это критически важно: outbox в Postgres `BillingService` смысла **не имеет** — если центр в дауне, до него и не дойти, чтобы записать в outbox,
- если требуется ещё больше изоляции (Gateway-Postgres тоже может быть деградирован) — отдельный durable channel (Kafka / Redis Streams / append-only file), доступный Gateway без BillingService,
- асинхронный воркер на Gateway читает outbox и доставляет в `BillingService.RecordUsage` с retry/backoff → ack → DELETE из outbox,
- на 9Router локальный `usageHistory` остаётся только для дашборда самой ноды; ретеншн из рычага 2 даёт ему окно в 30 дней,
- никакого double-accounting: учётный канал один — Gateway → BillingService, локальная история ноды в учёте не участвует.

Эта ветка идёт отдельным треком в скоупе `OpenRussiaRouter.ApiGateway` / `BillingService` и **не блокируется** этим документом. Рычаги 1–3 закрывают рост хранилища 9Router независимо.

## Что точно **не** делаем

- Не меняем SQLite на Postgres внутри 9Router.
- Не вводим параллельный `data.sqlite` для отдельного типа данных.
- Не патчим SQL upstream-схем — все изменения только настроечные параметры и фоновые job'ы.
- Не делаем массовый inline-патчинг upstream-файлов — только в неизбежных точках интеграции (`backup.js`, init-хук), всё остальное в новых файлах.
- Не вводим глобальный «выключатель» `usageHistory` — таблица нужна для дашборда самой ноды.

## Известные проблемы текущей реализации upstream

Эти проблемы существуют в upstream-коде и обнаружены при разборе. Сами по себе они не входят в скоуп этого документа, но **знание о них необходимо** для корректной реализации рычагов.

### Шаблон чтения настроек observability не даёт env-fallback

Текущая последовательность в [requestDetailsRepo.js:19-28](../src/OpenRussiaRouter.9Router/src/lib/db/repos/requestDetailsRepo.js#L19-L28):

1. `getSettings()` возвращает результат `mergeWithDefaults(raw)` — всегда подмешивает дефолты ([settingsRepo.js:48-64](../src/OpenRussiaRouter.9Router/src/lib/db/repos/settingsRepo.js#L48-L64)).
2. Затем код делает `typeof settings.enableObservability === "boolean" ? ... : envEnabled`. Поскольку default `true` уже подставлен, проверка `typeof === "boolean"` **всегда** true.
3. Для числовых полей: `settings.observabilityMaxRecords || envValue || default`. Поскольку дефолт уже подставлен (1000), `1000 || env` = 1000. Env игнорируется.

**Следствие:** env-переменные `OBSERVABILITY_*`, документированные в `.env.example`, на практике не работают, если у админа уже есть запись в `settings`. Для наших новых полей (retention) нужно **не повторять** этот паттерн — читать `raw` settings напрямую через `readRaw()` и применять `raw?.field ?? env ?? default`. Починка существующего observability-кода — отдельная задача в той же зоне.

### Бэкап создаётся ПОСЛЕ миграций, без WAL-checkpoint

В [migrate.js:233-247](../src/OpenRussiaRouter.9Router/src/lib/db/migrate.js#L233-L247) `backupFile(DATA_FILE, ...)` вызывается **после** `runVersionedMigrations`. То есть как «страховка перед миграцией» он концептуально не работает — копирует уже изменённую БД. Если миграция испортила данные, бэкап их не спасёт.

Плюс копирование — простой `fs.copyFileSync` ([backup.js:21](../src/OpenRussiaRouter.9Router/src/lib/db/backup.js#L21)) без явного `wal_checkpoint(TRUNCATE)`. В WAL-режиме это значит, что свежие коммиты могут сидеть в `-wal` и не быть в скопированном `data.sqlite`. Скопированный файл — потенциально неконсистентен.

**Следствие:** аргумент «5 копий — это 5 страховок» в реальности слабее, чем кажется. Снижение до 1 копии не сильно ухудшает позицию по сравнению с upstream. Если в будущем будет отдельная задача «починить backup-стратегию», стоит:

1. вызывать `backupFile` ДО `runVersionedMigrations`,
2. делать `wal_checkpoint(TRUNCATE)` перед копированием.

Не блокирует наш план, но фиксируется как смежная проблема.

## Технический долг за горизонтом

- **Ретеншн `usageDaily`.** Сейчас не предусмотрен. 1 строка/день × ~50 КБ (агрегаты по уникальным комбинациям model × provider × apiKey × endpoint) = ~18 МБ/год. К отметке 6 месяцев работы стоит вернуться и добавить TTL (например, 180 дней) или агрегацию в месячные строки.
- **Починка env-fallback в observability** — см. выше. Самостоятельная задача.
- **Backup-стратегия в upstream** — см. выше. Опционально, не блокирует.

## Расчётный профиль после внедрения рычагов 1–3

Под ту же нагрузку, что давала 1 ГБ/мес у upstream-дефолтов:

| Компонент | До | После |
| --- | --- | --- |
| `usageHistory` + индексы | 100–200 МБ (растёт линейно) | 15–25 МБ (стабилизировано окном 30 дней + incremental_vacuum) |
| `requestDetails` | до 20 МБ (потолок) | ~2 МБ (новый потолок) |
| `backups/` | до 1 ГБ (1–5 копий, зависит от истории апгрейдов) | до ~200 МБ при одном свежем апгрейде, обычно ~25–30 МБ |
| **Итого `/opt/9router/data/db/`** | **до ~1.2 ГБ** | **~50–250 МБ** в зависимости от свежести апгрейда |

Без `auto_vacuum = INCREMENTAL` + разового `VACUUM` для существующих БД активный `data.sqlite` останется большим даже после DELETE — поэтому VACUUM-стратегия в рычаге 2 **обязательна**, не «опция».

## Нарезка на ТЗ

Предлагаемое разбиение на 4 независимых задачи + 1 смежная починка:

1. **`BACKUPS_KEEP_COUNT` (env-only), дефолт = 1.** Inline-патч в `backup.js`: `KEEP_BACKUPS` → читается из env при старте процесса; `pruneOldBackups(maxKeep)` принимает параметр; вызовы в `migrate.js` передают значение. UI **не** добавляем. Документация в `.env.example`. **Размер: часовая задача.**
2. **Schema-bump + фоновый retention job.** Добавить `PRAGMA auto_vacuum = INCREMENTAL` в `PRAGMA_SQL` ([schema.js:4-12](../src/OpenRussiaRouter.9Router/src/lib/db/schema.js#L4-L12)) — для свежих БД режим включится сразу при создании. Новый файл `src/lib/db/repos/usageRepoRetention.js` с пакетным DELETE через `setImmediate`-цикл, условным `PRAGMA incremental_vacuum` (только если текущая БД уже в INCREMENTAL-режиме, иначе пропуск + лог), логированием `[retention] deleted={N} vacuumed={M} mode={...}`. Поля в `settings` через `raw?.field ?? env ?? default`. Best-effort на sql.js — детект драйвера, отключение VACUUM-части. Подключение из init-хука 9Router. **Размер: 2–3 дня.**
3. **One-shot maintenance: перевод существующих БД в incremental_vacuum.** Новый npm-script `db:enable-incremental-vacuum` (либо admin endpoint) с проверкой свободного места (`free_space >= 2.2 * db_size`), логированием плана, `PRAGMA auto_vacuum = INCREMENTAL` + `VACUUM`, фиксацией в `_meta`. Env-флаг `ORR_AUTO_RUN_DB_MAINTENANCE` для opt-in автозапуска при старте. **Размер: 1 день**, плюс runbook-инструкция для оператора в `docs/9router-vps-deployment-runbook.ru.md`.
4. **Понизить дефолты observability + задокументировать env.** Правки дефолтов в [settingsRepo.js:26-30](../src/OpenRussiaRouter.9Router/src/lib/db/repos/settingsRepo.js#L26-L30), документация env в `.env.example`. **Размер: час.**
5. **(Смежная) Починить env-fallback в observability-коде.** В [requestDetailsRepo.js:19-28](../src/OpenRussiaRouter.9Router/src/lib/db/repos/requestDetailsRepo.js#L19-L28) перейти на чтение `raw` settings и `raw?.field ?? env ?? default`. Без этого изменение дефолтов из задачи 4 сработает только для свежих нод. **Размер: час.** Можно объединить с задачей 4.

Стратегическая ветка (durable outbox на Gateway) — отдельным ТЗ в скоупе `OpenRussiaRouter.ApiGateway` / `BillingService`, не блокируется этим документом.

## Ссылки

- Спецификация продукта: [docs/prd/open-russia-router-prd.md](prd/open-russia-router-prd.md)
- Деплой ноды: [docs/9router-vps-deployment-runbook.ru.md](9router-vps-deployment-runbook.ru.md)
- Upstream 9Router (vendored): [src/OpenRussiaRouter.9Router/AGENTS.md](../src/OpenRussiaRouter.9Router/AGENTS.md)
- Корневая логика DB upstream: [src/OpenRussiaRouter.9Router/src/lib/db/](../src/OpenRussiaRouter.9Router/src/lib/db/)
