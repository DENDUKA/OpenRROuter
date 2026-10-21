-- Гайды opencode, hermes-agent и codex-cli после переезда на rawrter всё ещё называли провайдера
-- старым брендом: ID провайдера в локальном конфиге клиента (`openrussiarouter`) и отображаемое
-- имя / подписи к скриншотам (`OpenRussiaRouter`). Оба значения живут только в конфиге
-- пользователя и в API не уходят, поэтому переименование ничего не ломает — включая тех, кто уже
-- настроился по старому тексту. replace() регистрозависим: две замены покрывают ровно эти формы и
-- не трогают ID моделей (`ORR/...`) и имя переменной окружения (`ORR_API_KEY`). Парные ссылки
-- (`name:` ↔ `provider: custom:<name>` у hermes, `[model_providers.<id>]` ↔ `model_provider` у
-- codex) меняются одной и той же заменой и остаются согласованными.
UPDATE content.tools
SET description_ru = replace(replace(description_ru, 'OpenRussiaRouter', 'Rawrter'), 'openrussiarouter', 'rawrter'),
    description_en = replace(replace(description_en, 'OpenRussiaRouter', 'Rawrter'), 'openrussiarouter', 'rawrter'),
    setup_ru = replace(replace(setup_ru, 'OpenRussiaRouter', 'Rawrter'), 'openrussiarouter', 'rawrter'),
    setup_en = replace(replace(setup_en, 'OpenRussiaRouter', 'Rawrter'), 'openrussiarouter', 'rawrter'),
    updated_at_utc = now()
WHERE id IN ('opencode', 'hermes-agent', 'codex-cli')
  AND concat_ws(' ', description_ru, description_en, setup_ru, setup_en) ~ '(OpenRussiaRouter|openrussiarouter)';
