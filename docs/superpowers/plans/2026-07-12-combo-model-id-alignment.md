# Combo Model ID Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every catalog card expose the same value in `Model ID` and `Router Model`, while fully validating the target Combo set.

**Architecture:** Apply a one-time production data migration to the `billing.model_prices` catalog table. The migration changes only catalog primary keys and deliberately leaves immutable usage and ledger history unchanged.

**Tech Stack:** PostgreSQL 16, Docker Compose, `psql`, OpenRussiaRouter Billing catalog.

---

### Task 1: Verify migration preconditions

**Files:**
- Modify: production table `billing.model_prices`
- Test: read-only `psql` preflight query

- [ ] **Step 1: Confirm that each old model ID maps to its expected Combo and that no target `model_id` already exists**

```sql
WITH mapping(old_model_id, new_model_id) AS (
    VALUES
        ('DeepSeek-V4-Pro', 'deepseek-v4-pro'),
        ('ag/gemini-3-flash', 'gemini-3-flash'),
        ('glm/glm-5', 'glm-5'),
        ('glm/glm-5.1', 'glm-5.1'),
        ('glm/glm-5.2', 'glm-5.2'),
        ('ocg/deepseek-v4-flash', 'deepseek-v4-flash'),
        ('ocg/kimi-k2.6', 'kimi-k2.6'),
        ('ocg/kimi-k2.7-code', 'kimi-k2.7-code'),
        ('ocg/mimo-v2.5', 'mimo-v2.5'),
        ('ocg/mimo-v2.5-pro', 'mimo-v2.5-pro'),
        ('ocg/minimax-m2.5', 'minimax-m2.5'),
        ('ocg/minimax-m2.7', 'minimax-m2.7'),
        ('ocg/minimax-m3', 'minimax-m3'),
        ('ocg/qwen3.6-plus', 'qwen3.6-plus'),
        ('ocg/qwen3.7-max', 'qwen3.7-max'),
        ('ocg/qwen3.7-plus', 'qwen3.7-plus')
)
SELECT m.old_model_id, m.new_model_id, p.router_model_id,
       EXISTS (SELECT 1 FROM billing.model_prices x WHERE x.model_id = m.new_model_id) AS target_exists
FROM mapping m
LEFT JOIN billing.model_prices p ON p.model_id = m.old_model_id
ORDER BY m.old_model_id;
```

Expected: 16 rows, every `router_model_id` equals `new_model_id`, and every `target_exists` is `false`.

- [ ] **Step 2: Confirm the two already-aligned cards**

```sql
SELECT model_id, router_model_id
FROM billing.model_prices
WHERE model_id IN ('gemini-3.1-pro', 'gemini-3.5-flash')
ORDER BY model_id;
```

Expected: 2 rows where the two columns are identical.

### Task 2: Rename catalog identifiers atomically

**Files:**
- Modify: production table `billing.model_prices`
- Test: `UPDATE ... RETURNING` result

- [ ] **Step 1: Execute the one-statement transactional rename**

```sql
BEGIN;
WITH mapping(old_model_id, new_model_id) AS (
    VALUES
        ('DeepSeek-V4-Pro', 'deepseek-v4-pro'),
        ('ag/gemini-3-flash', 'gemini-3-flash'),
        ('glm/glm-5', 'glm-5'),
        ('glm/glm-5.1', 'glm-5.1'),
        ('glm/glm-5.2', 'glm-5.2'),
        ('ocg/deepseek-v4-flash', 'deepseek-v4-flash'),
        ('ocg/kimi-k2.6', 'kimi-k2.6'),
        ('ocg/kimi-k2.7-code', 'kimi-k2.7-code'),
        ('ocg/mimo-v2.5', 'mimo-v2.5'),
        ('ocg/mimo-v2.5-pro', 'mimo-v2.5-pro'),
        ('ocg/minimax-m2.5', 'minimax-m2.5'),
        ('ocg/minimax-m2.7', 'minimax-m2.7'),
        ('ocg/minimax-m3', 'minimax-m3'),
        ('ocg/qwen3.6-plus', 'qwen3.6-plus'),
        ('ocg/qwen3.7-max', 'qwen3.7-max'),
        ('ocg/qwen3.7-plus', 'qwen3.7-plus')
)
UPDATE billing.model_prices p
SET model_id = m.new_model_id
FROM mapping m
WHERE p.model_id = m.old_model_id
  AND p.router_model_id = m.new_model_id
RETURNING p.model_id, p.router_model_id;
COMMIT;
```

Expected: 16 returned rows, each with equal values in both columns; `COMMIT` succeeds.

### Task 3: Align pre-existing non-Combo cards

**Files:**
- Modify: production table `billing.model_prices`
- Test: read-only mismatch query

- [ ] **Step 1: Rename the three remaining mismatched cards in one guarded transaction**

```sql
BEGIN;
WITH mapping(old_model_id, new_model_id) AS (
    VALUES
        ('Gemini-3.1-Pro', 'ag/gemini-pro-agent'),
        ('Xiaomi-Mimo-2.5-Pro-free', 'Xiaomi-Mimo-2.5-Pro'),
        ('testcombo', 'test-combo')
)
UPDATE billing.model_prices p
SET model_id = m.new_model_id
FROM mapping m
WHERE p.model_id = m.old_model_id
  AND p.router_model_id = m.new_model_id
RETURNING p.model_id, p.router_model_id, p.enabled;
COMMIT;
```

Expected: three returned rows, each with identical IDs; their enabled state is unchanged.

### Task 4: Verify the complete Combo catalog

**Files:**
- Modify: none
- Test: read-only `psql` postflight query

- [ ] **Step 1: Check all target Combo cards and their required fields**

```sql
WITH expected(model_id) AS (
    VALUES
        ('deepseek-v4-flash'), ('deepseek-v4-pro'), ('gemini-3-flash'),
        ('gemini-3.1-pro'), ('gemini-3.5-flash'), ('glm-5'), ('glm-5.1'),
        ('glm-5.2'), ('kimi-k2.6'), ('kimi-k2.7-code'), ('mimo-v2.5'),
        ('mimo-v2.5-pro'), ('minimax-m2.5'), ('minimax-m2.7'), ('minimax-m3'),
        ('qwen3.6-plus'), ('qwen3.7-max'), ('qwen3.7-plus')
)
SELECT e.model_id, COUNT(p.model_id) AS cards,
       BOOL_AND(p.model_id = p.router_model_id) AS ids_match,
       BOOL_AND(p.enabled) AS enabled,
       BOOL_AND(NULLIF(BTRIM(p.description_ru), '') IS NOT NULL) AS ru_set,
       BOOL_AND(NULLIF(BTRIM(p.description_en), '') IS NOT NULL) AS en_set,
       BOOL_AND(NULLIF(BTRIM(p.input_modalities), '') IS NOT NULL) AS input_modalities_set,
       BOOL_AND(NULLIF(BTRIM(p.output_modalities), '') IS NOT NULL) AS output_modalities_set
FROM expected e
LEFT JOIN billing.model_prices p ON p.model_id = e.model_id
GROUP BY e.model_id
ORDER BY e.model_id;
```

Expected: 18 rows with `cards = 1` and every boolean value `true`.

- [ ] **Step 2: Confirm the excluded uppercase card remains untouched**

```sql
SELECT model_id, router_model_id
FROM billing.model_prices
WHERE model_id = 'GLM-5.2';
```

Expected: one unchanged row with `model_id = router_model_id = 'GLM-5.2'`.

- [ ] **Step 3: Confirm that no catalog card has mismatched IDs**

```sql
SELECT COUNT(*) AS mismatched_cards
FROM billing.model_prices
WHERE model_id <> router_model_id;
```

Expected: `mismatched_cards = 0`.
