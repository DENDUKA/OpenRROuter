-- Funnel queries for Grafana dashboards over analytics.events.
-- Distinct IDs are HMAC pseudonyms (not raw user ids), and properties contain only
-- allow-listed keys plus schema_version / environment.

-- 1. Activation funnel: user verified -> api key created -> first api request completed.
with verified as (
    select distinct_id, min(occurred_at_utc) as verified_at
    from analytics.events
    where event_name = 'user verified'
    group by distinct_id
),
api_key as (
    select distinct_id, min(occurred_at_utc) as key_created_at
    from analytics.events
    where event_name = 'api key created'
    group by distinct_id
),
first_request as (
    select distinct_id, min(occurred_at_utc) as first_request_at
    from analytics.events
    where event_name = 'api request completed'
    group by distinct_id
)
select
    count(*) filter (where v.verified_at is not null) as verified,
    count(*) filter (where k.key_created_at is not null and k.key_created_at >= v.verified_at) as created_key,
    count(*) filter (where r.first_request_at is not null and r.first_request_at >= k.key_created_at) as made_first_request
from verified v
left join api_key k on k.distinct_id = v.distinct_id
left join first_request r on r.distinct_id = v.distinct_id;

-- 2. Weekly active users: unique distinct_ids that completed an API request, by ISO week.
select date_trunc('week', occurred_at_utc) as week,
       count(distinct distinct_id) as active_users
from analytics.events
where event_name = 'api request completed'
group by week
order by week;

-- 3. Conversion funnel: first api request completed -> payment completed.
with first_request as (
    select distinct_id, min(occurred_at_utc) as first_request_at
    from analytics.events
    where event_name = 'api request completed'
    group by distinct_id
),
first_payment as (
    select distinct_id, min(occurred_at_utc) as first_payment_at
    from analytics.events
    where event_name = 'payment completed'
    group by distinct_id
)
select
    count(*) filter (where fr.first_request_at is not null) as made_first_request,
    count(*) filter (where fp.first_payment_at is not null and fp.first_payment_at >= fr.first_request_at) as paid
from first_request fr
left join first_payment fp on fp.distinct_id = fr.distinct_id;

-- 4. Usage breakdown by model and endpoint (endpoint is added in TS-016; falls back to 'unknown' until then).
select
    occurred_at_utc::date as date,
    coalesce(properties ->> 'model', 'unknown') as model,
    coalesce(properties ->> 'endpoint', 'unknown') as endpoint,
    count(*) as requests
from analytics.events
where event_name = 'api request completed'
group by date, properties ->> 'model', properties ->> 'endpoint'
order by date desc, requests desc;
