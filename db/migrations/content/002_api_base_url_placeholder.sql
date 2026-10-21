-- Гайды инструментов писались с вшитым хостом API (https://api.openrussiarouter.com) и пережили
-- переезд на api.rawrter.com. WebApp теперь подставляет {{API_BASE_URL}} из ApiBaseUrlResolver
-- (админская настройка public_api_base_url), поэтому вшитые адреса меняем на плейсхолдер.
-- Плейсхолдер — без хвостового слэша и без /v1, как и сам резолвер: "https://api.x.com/v1"
-- становится "{{API_BASE_URL}}/v1". Голые упоминания хоста без схемы плейсхолдером не заменить
-- (он несёт схему), их переводим на актуальное имя. Taglines — плоский текст без подстановки.
UPDATE content.tools
SET description_ru = replace(replace(replace(description_ru,
        'https://api.openrussiarouter.com', '{{API_BASE_URL}}'),
        'https://api.rawrter.com', '{{API_BASE_URL}}'),
        'api.openrussiarouter.com', 'api.rawrter.com'),
    description_en = replace(replace(replace(description_en,
        'https://api.openrussiarouter.com', '{{API_BASE_URL}}'),
        'https://api.rawrter.com', '{{API_BASE_URL}}'),
        'api.openrussiarouter.com', 'api.rawrter.com'),
    setup_ru = replace(replace(replace(setup_ru,
        'https://api.openrussiarouter.com', '{{API_BASE_URL}}'),
        'https://api.rawrter.com', '{{API_BASE_URL}}'),
        'api.openrussiarouter.com', 'api.rawrter.com'),
    setup_en = replace(replace(replace(setup_en,
        'https://api.openrussiarouter.com', '{{API_BASE_URL}}'),
        'https://api.rawrter.com', '{{API_BASE_URL}}'),
        'api.openrussiarouter.com', 'api.rawrter.com'),
    updated_at_utc = now()
WHERE concat_ws(' ', description_ru, description_en, setup_ru, setup_en) ~ 'api\.(openrussiarouter|rawrter)\.com';
