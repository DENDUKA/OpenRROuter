import { describe, expect, it } from 'vitest';
import { HeartbeatScheduler, type HeartbeatSnapshot } from '../src/heartbeat.js';
import type { DailySummaryData } from '../src/format.js';

const summary: DailySummaryData = {
  newRegistrations: 2, topupsCount: 1, topupsTotalRub: 100,
  topupsTotalUsd: 1.1, uniqueUsers: 3, activeApiUsers: 4,
};
const state: HeartbeatSnapshot = {
  queueDepth: 0, supportSentToday: 4, lastSupportSentAt: null,
  deliveryErrorsToday: 0, heartbeatSentToday: false,
};

describe('daily summary / technical fallback', () => {
  it.each(['success', 'empty', 'error'] as const)('%s: sends one message and reschedules', async (mode) => {
    const timers: Array<() => void> = [];
    const sent: string[] = [];
    const scheduler = new HeartbeatScheduler(
      async text => { sent.push(text); }, () => state,
      {
        now: () => Date.parse('2026-09-09T11:00:00Z'),
        setTimer: cb => { timers.push(cb); return () => {}; },
        summaryFetcher: { fetchSummary: async () => {
          if (mode === 'error') throw new Error('synthetic failure');
          return mode === 'success' ? summary : null;
        } },
      },
    );
    scheduler.start();
    timers[0]!();
    await new Promise<void>(resolve => setImmediate(resolve));
    expect(sent).toHaveLength(1);
    expect(sent[0]).toContain(mode === 'success' ? 'Утренняя сводка' : 'Бот жив');
    if (mode === 'success') expect(sent[0]).toContain('Новых регистраций: 2');
    expect(timers).toHaveLength(2);
    scheduler.stop();
  });
});
