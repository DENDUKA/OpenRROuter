import { getAdapter } from "../driver.js";
import { exportSettings } from "./settingsRepo.js";

const DEFAULT_RETENTION_DAYS = 30;
const DEFAULT_RETENTION_INTERVAL_MS = 3600000;
const DEFAULT_RETENTION_BATCH_SIZE = 10000;
const JOB_KEY = "__usageHistoryRetentionJob";

function getJobState() {
  if (!globalThis[JOB_KEY]) {
    globalThis[JOB_KEY] = {
      started: false,
      running: false,
      timer: null,
    };
  }
  return globalThis[JOB_KEY];
}

function parseIntSetting(value, fallback, { allowZero = false } = {}) {
  const parsed = Number.parseInt(String(value ?? ""), 10);
  if (!Number.isFinite(parsed)) return fallback;
  if (parsed > 0) return parsed;
  if (allowZero && parsed === 0) return 0;
  return fallback;
}

async function readRetentionConfig() {
  const raw = await exportSettings();

  return {
    days: parseIntSetting(
      raw?.usageHistoryRetentionDays ?? process.env.USAGE_HISTORY_RETENTION_DAYS,
      DEFAULT_RETENTION_DAYS,
      { allowZero: true }
    ),
    intervalMs: parseIntSetting(
      raw?.usageHistoryRetentionIntervalMs ?? process.env.USAGE_HISTORY_RETENTION_INTERVAL_MS,
      DEFAULT_RETENTION_INTERVAL_MS
    ),
    batchSize: parseIntSetting(
      raw?.usageHistoryRetentionBatchSize ?? process.env.USAGE_HISTORY_RETENTION_BATCH_SIZE,
      DEFAULT_RETENTION_BATCH_SIZE
    ),
  };
}

function getAutoVacuumMode(row) {
  if (!row || typeof row !== "object") return null;
  if (Number.isFinite(row.auto_vacuum)) return Number(row.auto_vacuum);

  for (const value of Object.values(row)) {
    if (Number.isFinite(value)) return Number(value);
  }

  return null;
}

async function pruneUsageHistory(db, { days, batchSize }) {
  const cutoff = new Date(Date.now() - days * 86400000).toISOString();
  let deleted = 0;

  while (true) {
    const result = db.run(
      `DELETE FROM usageHistory
       WHERE id IN (
         SELECT id FROM usageHistory
         WHERE timestamp < ?
         LIMIT ?
       )`,
      [cutoff, batchSize]
    );

    const changes = Number(result?.changes ?? 0);
    if (changes === 0) break;

    deleted += changes;
    await new Promise((resolve) => setImmediate(resolve));
  }

  return deleted;
}

async function runRetentionTick() {
  const config = await readRetentionConfig();
  const db = await getAdapter();
  const deleted = await pruneUsageHistory(db, config);

  if (deleted === 0) return config.intervalMs;

  if (db.driver === "sql.js") {
    console.log(`[usageRetention] deleted=${deleted} vacuumed=0 mode=skip reason=sqljs_conservative`);
    return config.intervalMs;
  }

  const autoVacuumMode = getAutoVacuumMode(db.get(`PRAGMA auto_vacuum`));
  if (autoVacuumMode !== 2) {
    console.log(`[usageRetention] deleted=${deleted} vacuumed=0 mode=skip reason=auto_vacuum_disabled`);
    return config.intervalMs;
  }

  db.exec(`PRAGMA incremental_vacuum(1000)`);
  console.log(`[usageRetention] deleted=${deleted} vacuumed=1000 mode=incremental`);
  return config.intervalMs;
}

function scheduleNextTick(state, delayMs) {
  if (state.timer) clearTimeout(state.timer);
  state.timer = setTimeout(() => {
    state.timer = null;
    runScheduledTick(state).catch((error) => {
      console.error("[usageRetention] tick failed:", error);
    });
  }, delayMs);
  if (typeof state.timer.unref === "function") state.timer.unref();
}

async function runScheduledTick(state) {
  if (state.running) return;

  state.running = true;
  let nextIntervalMs = DEFAULT_RETENTION_INTERVAL_MS;

  try {
    nextIntervalMs = await runRetentionTick();
  } catch (error) {
    console.error("[usageRetention] tick failed:", error);
  } finally {
    state.running = false;
    scheduleNextTick(state, nextIntervalMs);
  }
}

export function startUsageHistoryRetentionJob() {
  const state = getJobState();
  if (state.started) return state;

  state.started = true;
  scheduleNextTick(state, 0);
  return state;
}
