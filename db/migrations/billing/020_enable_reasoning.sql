-- 020_enable_reasoning.sql
-- Включает supports_reasoning + безопасный набор efforts (low, medium, high) для моделей,
-- у которых upstream-ID (router_model_id) или публичный model_id совпадает с известными
-- reasoning-capable семействами из каталога 9Router (C:\Programs\F9Router\open-sse\providers\capabilities.js).
--
-- Идемпотентно: не перезаписывает модели, уже размеченные вручную через админку
-- (supports_reasoning = true с непустым allowed_reasoning_efforts).
--
-- Набор efforts 'low,medium,high' — пересечение канонического набора ORR
-- (ReasoningEffort.cs: none,minimal,low,medium,high,xhigh) и F9Router levels; 'max' в ORR
-- не входит и здесь не проставляется. Default 'medium' как умеренное значение.

with reasoning_patterns(pattern) as (
    select * from (values
        -- OpenAI reasoning family
        ('%gpt-5%'),
        ('%gpt-oss%'),
        ('%codex%'),
        -- Google Gemini 3.x / 2.5 (3.x нельзя выключить — 'none' не добавляем)
        ('%gemini-3%'),
        ('%gemini-2.5%'),
        -- Anthropic Claude reasoning family (4.x / fable / mythos)
        ('%claude%opus-4%'),
        ('%claude%sonnet-4%'),
        ('%claude%haiku%'),
        ('%claude%fable%'),
        ('%claude%mythos%'),
        -- Z.ai GLM
        ('%glm-5%'),
        ('%glm-4.7%'),
        ('%glm-4.6%'),
        -- MiniMax
        ('%minimax-m3%'),
        ('%minimax-m2%'),
        -- Moonshot Kimi
        ('%kimi-k2%'),
        -- DeepSeek reasoning
        ('%deepseek-v4%'),
        ('%deepseek-r%'),
        ('%reasoner%'),
        -- Qwen / QwQ
        ('%qwen3%'),
        ('%qwen-max%'),
        ('%qwen-plus%'),
        ('%qwq%'),
        ('%qwen-vl%'),
        ('%qwen-coder%'),
        ('%qwen%coder%'),
        -- xAI Grok
        ('%grok-4%'),
        ('%grok-3%'),
        ('%grok-code%'),
        -- Tencent Hunyuan / hy3
        ('%hunyuan%'),
        ('%hy3%'),
        -- Step
        ('%step-%'),
        -- Nemotron / Ling (reasoning:true в capabilities.js)
        ('%nemotron%'),
        ('%ling-%'),
        -- Doubao thinking
        ('%doubao%thinking%'),
        ('%doubao-seed-1-6-thinking%')
    ) as t(pattern)
),
target_models as (
    select m.model_id
    from billing.model_prices m
    join reasoning_patterns p
      on m.router_model_id ilike p.pattern
      or m.model_id ilike p.pattern
    where coalesce(m.supports_reasoning, false) = false
       or coalesce(m.allowed_reasoning_efforts, '') = ''
)
update billing.model_prices
set supports_reasoning        = true,
    allowed_reasoning_efforts = 'low,medium,high',
    default_reasoning_effort  = coalesce(default_reasoning_effort, 'medium')
where model_id in (select model_id from target_models);
