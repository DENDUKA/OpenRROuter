-- Drops the in-house site traffic tracking: analytics.site_visits (002) and its compacted
-- form analytics.visit_sessions (003).
--
-- Site traffic is measured by Yandex Metrika now, so the whole pipeline is gone from the
-- code — the visit middleware in WebApp, the compaction worker in BillingService, the
-- /admin/visitors and /admin/sessions pages and the "Посетители сайта" dashboard section.
-- Nothing reads or writes these tables any more; keeping them would only accumulate rows
-- nobody looks at.
--
-- Migrations 002 and 003 are left untouched on purpose (applied migrations are immutable):
-- on a fresh database both tables are created and then dropped here.
--
-- analytics.events (product analytics) is NOT affected — it is a separate pipeline.
drop table if exists analytics.visit_sessions;
drop table if exists analytics.site_visits;
