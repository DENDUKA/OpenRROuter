-- Visit sessions: one row per visitor "sitting", collapsed from analytics.site_visits.
--
-- A background compactor (BillingService) groups the raw page views of a visitor into
-- sessions (activity gap > 30 minutes or a UTC day change starts a new session), writes
-- one row here and DELETES the source rows. That makes analytics.site_visits ephemeral —
-- it only holds the last ~35 minutes of traffic — while this table is the long-lived
-- store of site traffic.
--
-- Privacy is inherited from analytics.site_visits: the raw client IP is NEVER stored,
-- `visitor_hash` is the same keyed HMAC over (ip + user agent), and `distinct_id` is
-- filled in only for signed-in visitors and equals analytics.events.distinct_id.
create table if not exists analytics.visit_sessions (
    id               bigint      generated always as identity primary key,
    visitor_hash     text        not null,
    distinct_id      text        null,
    started_at_utc   timestamptz not null,
    ended_at_utc     timestamptz not null,
    duration_seconds int         not null,
    page_view_count  int         not null,
    entry_path       text        not null,
    exit_path        text        not null,
    referrer_host    text        null,
    referrer_kind    text        not null,
    utm_source       text        null,
    utm_medium       text        null,
    utm_campaign     text        null,
    device_class     text        not null,
    is_authenticated boolean     not null,
    -- [{"t":"2026-07-27T10:00:03Z","p":"/models"}, ...], truncated to MaxPagesPerSession.
    -- Truncation is detected as page_view_count > jsonb_array_length(pages).
    pages            jsonb       not null default '[]'::jsonb,
    environment      text        not null,
    created_at_utc   timestamptz not null default now(),
    constraint ck_visit_sessions_range check (ended_at_utc >= started_at_utc),
    constraint ck_visit_sessions_count check (page_view_count > 0)
);

-- Identity of a session, and the `on conflict` arbiter the compactor merges into when a
-- late-flushed visit lands after the session was already written.
create unique index if not exists ux_analytics_visit_sessions_visitor_start
    on analytics.visit_sessions (visitor_hash, started_at_utc);

-- Admin listing and daily rollups over the dashboard time range.
create index if not exists ix_analytics_visit_sessions_start
    on analytics.visit_sessions (started_at_utc);

-- Every dashboard panel excludes bots, so keep the human traffic index narrow.
create index if not exists ix_analytics_visit_sessions_human_start
    on analytics.visit_sessions (started_at_utc)
    where device_class <> 'bot';

-- Resolving a signed-in user back to their visitor_hash (/admin/sessions?userId=,
-- and the SiteVisitReader fallback once the raw visits are gone).
create index if not exists ix_analytics_visit_sessions_distinct
    on analytics.visit_sessions (distinct_id)
    where distinct_id is not null;
