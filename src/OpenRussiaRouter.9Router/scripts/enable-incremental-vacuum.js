// One-shot SQLite maintenance: switches existing data.sqlite to auto_vacuum=INCREMENTAL via full VACUUM.
// Self-contained: depends only on better-sqlite3 + Node stdlib. Does NOT import src/lib/db/* so it works
// from a standalone Docker runtime image without the Next.js source tree present.
//
// Flags: --force  re-run VACUUM even if _meta.autoVacuumMode is already 'incremental'.

const fs = require("fs");
const path = require("path");
const os = require("os");
const { Worker } = require("worker_threads");

const APP_NAME = "9router";

function defaultDataDir() {
  if (process.platform === "win32") {
    return path.join(process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming"), APP_NAME);
  }
  return path.join(os.homedir(), `.${APP_NAME}`);
}

function resolveDataFile() {
  const dataDir = process.env.DATA_DIR || defaultDataDir();
  return { dataDir, dataFile: path.join(dataDir, "db", "data.sqlite") };
}

function main() {
  const force = process.argv.slice(2).includes("--force");
  const { dataDir, dataFile } = resolveDataFile();

  console.log(`[db-maintenance] DATA_DIR=${dataDir}`);
  console.log(`[db-maintenance] data file: ${dataFile}`);

  if (!fs.existsSync(dataFile)) {
    console.error(`[db-maintenance] Error: data file not found at ${dataFile}`);
    console.error(`[db-maintenance] Nothing to do. Run 9Router at least once first, or set DATA_DIR correctly.`);
    process.exit(1);
  }

  let Database;
  try {
    Database = require("better-sqlite3");
  } catch (err) {
    console.error("[db-maintenance] Error: better-sqlite3 is not available.");
    console.error("[db-maintenance] Manual VACUUM requires native SQLite bindings (better-sqlite3).");
    console.error("[db-maintenance] sql.js is intentionally not supported for VACUUM (see OR-003).");
    console.error(`[db-maintenance] Details: ${err.message}`);
    process.exit(1);
  }

  const dbSize = fs.statSync(dataFile).size;
  const db = new Database(dataFile);

  let currentMode = null;
  try {
    const row = db.prepare(`SELECT value FROM _meta WHERE key = 'autoVacuumMode'`).get();
    currentMode = row ? row.value : null;
  } catch {
    // _meta may be absent on truly fresh DBs — treat as not migrated.
  }

  if (currentMode === "incremental" && !force) {
    console.log("[db-maintenance] already in incremental mode");
    console.log(`[db-maintenance] current db size: ${dbSize} bytes`);
    db.close();
    process.exit(0);
  }

  let freeSpace;
  try {
    if (typeof fs.statfsSync !== "function") {
      throw new Error("fs.statfsSync unavailable (requires Node 18.15+ / 19.6+)");
    }
    const stat = fs.statfsSync(dataFile);
    freeSpace = stat.bfree * stat.bsize;
  } catch (err) {
    console.error("[db-maintenance] Error: cannot determine free disk space.");
    console.error(`[db-maintenance] Reason: ${err.message}`);
    console.error("[db-maintenance] Refusing to run blind. Verify free space manually and rerun on a newer Node.");
    db.close();
    process.exit(1);
  }

  const requiredSpace = Math.ceil(dbSize * 2.2);
  if (freeSpace < requiredSpace) {
    console.error("[db-maintenance] Error: not enough free space.");
    console.error(`[db-maintenance] Required: ~${requiredSpace} bytes (2.2 * db size), available: ${freeSpace} bytes.`);
    db.close();
    process.exit(1);
  }

  console.log(`[db-maintenance] enabling incremental_vacuum dbSize=${dbSize} freeSpace=${freeSpace}`);

  try {
    db.exec("BEGIN IMMEDIATE;");
    db.exec("ROLLBACK;");
  } catch (err) {
    console.error("[db-maintenance] Error: could not obtain write lock. Database is busy.");
    console.error("[db-maintenance] Stop the running 9Router process/container and rerun in a maintenance window.");
    console.error(`[db-maintenance] Details: ${err.message}`);
    db.close();
    process.exit(1);
  }

  // _meta is normally created by the app's migration chain, but a maintenance run may precede first start.
  db.exec(`CREATE TABLE IF NOT EXISTS _meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)`);

  // better-sqlite3.exec is synchronous and blocks the event loop; spawn heartbeat in a worker thread.
  const workerCode = `
    const { parentPort } = require('worker_threads');
    const t0 = Date.now();
    let warned = false;
    const timer = setInterval(() => {
      const s = Math.round((Date.now() - t0) / 1000);
      console.log('[db-maintenance] VACUUM in progress elapsed=' + s + 's');
      if (s > 60 && !warned) {
        warned = true;
        console.warn('[db-maintenance] Warning: VACUUM is taking longer than 60 seconds. Do not interrupt.');
      }
    }, 10000);
    parentPort.on('message', (msg) => {
      if (msg === 'stop') {
        clearInterval(timer);
        process.exit(0);
      }
    });
  `;
  const worker = new Worker(workerCode, { eval: true });
  const tStart = Date.now();

  try {
    db.exec("PRAGMA auto_vacuum = INCREMENTAL;");
    db.exec("VACUUM;");
  } catch (err) {
    worker.postMessage("stop");
    console.error(`[db-maintenance] VACUUM failed: ${err.message}`);
    db.close();
    process.exit(1);
  }

  worker.postMessage("stop");
  const elapsed = Date.now() - tStart;

  const upsert = db.prepare(
    `INSERT INTO _meta(key, value) VALUES(?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value`
  );
  upsert.run("autoVacuumMode", "incremental");
  upsert.run("autoVacuumMigratedAt", new Date().toISOString());

  const newSize = fs.statSync(dataFile).size;
  console.log(`[db-maintenance] Success! VACUUM completed in ${elapsed}ms. New db size: ${newSize} bytes.`);

  db.close();
  process.exit(0);
}

main();
