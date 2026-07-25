-- Anonymous site traffic: one row per page view on the public site / cabinet.
--
-- Privacy: the raw client IP is NEVER stored. `visitor_hash` is a keyed HMAC over
-- (ip + user agent) with the same ProductAnalytics:IdentitySalt used for `distinct_id`,
-- so the value is stable per visitor but not reversible. `distinct_id` is filled in only
-- while the visitor is signed in, and it equals analytics.events.distinct_id for the same
-- user — that is what makes the visit -> signup -> payment funnel computable in this DB.
create table if not exists analytics.site_visits (
    id               bigint generated always as identity primary key,
    visitor_hash     text        not null,
    distinct_id      text        null,
    occurred_at_utc  timestamptz not null default now(),
    path             text        not null,
    referrer_host    text        null,
    referrer_kind    text        not null,
    utm_source       text        null,
    utm_medium       text        null,
    utm_campaign     text        null,
    device_class     text        not null,
    is_authenticated boolean     not null,
    environment      text        not null
);

-- Daily rollups over the dashboard time range.
create index if not exists ix_analytics_site_visits_time
    on analytics.site_visits (occurred_at_utc);

-- "New vs returning": needs the first visit per visitor.
create index if not exists ix_analytics_site_visits_visitor_time
    on analytics.site_visits (visitor_hash, occurred_at_utc);

-- Every dashboard panel excludes bots, so keep the human traffic index narrow.
create index if not exists ix_analytics_site_visits_human_time
    on analytics.site_visits (occurred_at_utc)
    where device_class <> 'bot';
