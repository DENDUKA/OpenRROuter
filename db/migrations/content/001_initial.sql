-- ContentService: каталог инструментов (Tools), в которых можно использовать наш API.
CREATE SCHEMA IF NOT EXISTS content;

-- Категории инструментов: локализованные, упорядоченные; используются как фильтр на /tools.
CREATE TABLE IF NOT EXISTS content.tool_categories (
    id              text        PRIMARY KEY,
    name_ru         text        NOT NULL,
    name_en         text        NOT NULL,
    sort_order      int         NOT NULL DEFAULT 0,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    updated_at_utc  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_tool_categories_id CHECK (id ~ '^[a-z0-9][a-z0-9._/-]{0,49}$')
);

CREATE TABLE IF NOT EXISTS content.tools (
    id                  text        PRIMARY KEY,
    display_name        text        NOT NULL,
    category_id         text        REFERENCES content.tool_categories(id) ON DELETE SET NULL,
    tags                text[]      NOT NULL DEFAULT '{}',
    website_url         text,
    tagline_ru          text,
    tagline_en          text,
    description_ru      text,
    description_en      text,
    setup_ru            text,
    setup_en            text,
    logo_bytes          bytea,
    logo_content_type   text,
    is_published        boolean     NOT NULL DEFAULT false,
    sort_order          int         NOT NULL DEFAULT 0,
    created_at_utc      timestamptz NOT NULL DEFAULT now(),
    updated_at_utc      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_tools_id CHECK (id ~ '^[a-z0-9][a-z0-9._/-]{0,49}$'),
    CONSTRAINT ck_tools_logo CHECK (
        logo_bytes IS NULL OR (
            logo_content_type IN ('image/png', 'image/jpeg', 'image/webp', 'image/svg+xml')
            AND octet_length(logo_bytes) <= 262144)),
    CONSTRAINT ck_tools_website CHECK (website_url IS NULL OR website_url ~* '^https?://')
);

CREATE INDEX IF NOT EXISTS idx_tools_published_sort
    ON content.tools (is_published, sort_order DESC, display_name);

CREATE INDEX IF NOT EXISTS idx_tools_category
    ON content.tools (category_id);

-- Картинки, встроенные в markdown-описание/инструкцию.
CREATE TABLE IF NOT EXISTS content.tool_images (
    id              uuid        PRIMARY KEY,
    tool_id         text        NOT NULL REFERENCES content.tools(id) ON DELETE CASCADE,
    file_name       text        NOT NULL,
    content_type    text        NOT NULL,
    byte_size       int         NOT NULL,
    bytes           bytea       NOT NULL,
    alt_text        text,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    -- svg намеренно не разрешён: тело картинки отдаётся с нашего origin.
    CONSTRAINT ck_tool_images_type CHECK (
        content_type IN ('image/png', 'image/jpeg', 'image/webp', 'image/gif')),
    CONSTRAINT ck_tool_images_size CHECK (
        byte_size > 0 AND byte_size <= 1048576 AND octet_length(bytes) = byte_size)
);

CREATE INDEX IF NOT EXISTS idx_tool_images_tool
    ON content.tool_images (tool_id, created_at_utc DESC);

INSERT INTO content.tool_categories (id, name_ru, name_en, sort_order) VALUES
    ('ide',     'IDE и редакторы',     'IDEs & editors',      100),
    ('cli',     'CLI и терминал',      'CLI & terminal',       90),
    ('chat-ui', 'Чат-интерфейсы',      'Chat interfaces',      80),
    ('agent',   'Агенты и фреймворки', 'Agents & frameworks',  70),
    ('other',   'Прочее',              'Other',                 0)
ON CONFLICT (id) DO NOTHING;
