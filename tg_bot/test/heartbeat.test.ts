import { describe, expect, it } from 'vitest';
import {
  DEFAULT_CATCHUP_DELAY_MS,
  HeartbeatScheduler,
  formatHeartbeatMessage,
  formatUtc3,
  nextHeartbeatUtc,
  type HeartbeatSnapshot,
} from '../src/heartbeat.js';

function snapshot(overrides: Partial<HeartbeatSnapshot> = {}): HeartbeatSnapshot {
  return {
    queueDepth: 0,
    supportSentToday: 0,
    lastSupportSentAt: null,
    deliveryErrorsToday: 0,
    heartbeatSentToday: false,
    ...overrides,
  };
}

const flush = () => new Promise<void>((resolve) => setImmediate(resolve));

describe('nextHeartbeatUtc', () => {
  it('до 07:00 UTC планирует на сегодня', () => {
    const next = nextHeartbeatUtc(new Date('2026-09-07T06:59:59Z'));
    expect(next.toISOString()).toBe('2026-09-07T07:00:00.000Z');
  });

  it('в 07:00 UTC и позже планирует на завтра', () => {
    expect(nextHeartbeatUtc(new Date('2026-09-07T07:00:00Z')).toISOString()).toBe('2026-09-08T07:00:00.000Z');
    expect(nextHeartbeatUtc(new Date('2026-09-07T23:30:00Z')).toISOString()).toBe('2026-09-08T07:00:00.000Z');
  });
});

describe('formatUtc3', () => {
  it('показывает время в UTC+3', () => {
    expect(formatUtc3(Date.parse('2026-09-07T07:00:00Z'))).toBe('07.09.2026, 10:00 (UTC+3)');
    expect(formatUtc3(Date.parse('2026-12-31T21:05:00Z'))).toBe('01.01.2027, 00:05 (UTC+3)');
  });
});

describe('formatHeartbeatMessage', () => {
  it('отмечает отсутствие уведомлений об обращениях', () => {
    const text = formatHeartbeatMessage(snapshot(), Date.parse('2026-09-07T07:00:00Z'));
    expect(text).toContain('ещё не было');
    expect(text).toContain('10:00');
    expect(text).toContain('Бот жив');
  });

  it('показывает время последнего уведомления и счётчики', () => {
    const text = formatHeartbeatMessage(
      snapshot({ supportSentToday: 3, lastSupportSentAt: Date.parse('2026-09-07T04:00:00Z'), deliveryErrorsToday: 1 }),
      Date.parse('2026-09-07T07:00:00Z'),
    );
    expect(text).toContain('за сутки: 3');
    expect(text).toContain('07:00 (UTC+3)');
    expect(text).toContain('Ошибок доставки за сутки: 1');
  });
});

describe('HeartbeatScheduler', () => {
  interface Armed {
    delayMs: number;
    fire: () => void;
    cancelled: boolean;
  }

  function makeScheduler(state: HeartbeatSnapshot, now: number) {
    const armed: Armed[] = [];
    const sent: string[] = [];
    const setTimer = (cb: () => void, delayMs: number) => {
      const handle: Armed = { delayMs, fire: cb, cancelled: false };
      armed.push(handle);
      return () => {
        handle.cancelled = true;
      };
    };
    const scheduler = new HeartbeatScheduler(async (text) => void sent.push(text), () => state, { now: () => now, setTimer });
    return { scheduler, armed, sent };
  }

  it('планирует ближайшие 07:00 UTC', () => {
    const { scheduler, armed } = makeScheduler(snapshot(), Date.parse('2026-09-07T06:00:00Z'));
    scheduler.start();
    expect(armed[0]?.delayMs).toBe(3_600_000);
    scheduler.stop();
  });

  it('не догоняет сигнал, если он уже был отправлен', () => {
    const now = Date.parse('2026-09-07T10:30:00Z');
    const { scheduler, armed } = makeScheduler(snapshot({ heartbeatSentToday: true }), now);
    scheduler.start();
    expect(armed[0]?.delayMs).toBe(Date.parse('2026-09-08T07:00:00Z') - now);
    scheduler.stop();
  });

  it('догоняет пропущенный сигнал после рестарта и планирует следующий день', async () => {
    const now = Date.parse('2026-09-07T10:30:00Z');
    const state = snapshot();
    const { scheduler, armed, sent } = makeScheduler(state, now);
    scheduler.start();
    expect(armed[0]?.delayMs).toBe(DEFAULT_CATCHUP_DELAY_MS);
    armed[0]?.fire();
    await flush();
    await flush();
    expect(sent.length).toBe(1);
    expect(sent[0]).toContain('Бот жив');
    expect(armed[1]?.delayMs).toBe(Date.parse('2026-09-08T07:00:00Z') - now);
    scheduler.stop();
  });

  it('stop отменяет активный таймер', () => {
    const { scheduler, armed } = makeScheduler(snapshot(), Date.parse('2026-09-07T06:00:00Z'));
    scheduler.start();
    scheduler.stop();
    expect(armed[0]?.cancelled).toBe(true);
  });
});
