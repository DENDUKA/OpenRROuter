import { describe, expect, it } from 'vitest';
import { DeliveryStats } from '../src/stats.js';

const DAY1 = Date.parse('2026-09-07T12:00:00Z');
const DAY2 = Date.parse('2026-09-08T12:00:00Z');

describe('DeliveryStats', () => {
  it('считает уведомления и ошибки за сутки', () => {
    const stats = new DeliveryStats();
    stats.recordSupportSent(DAY1);
    stats.recordSupportSent(DAY1 + 1000);
    stats.recordDeliveryFailure(DAY1 + 2000);
    const snap = stats.snapshot(DAY1 + 3000, 2);
    expect(snap.supportSentToday).toBe(2);
    expect(snap.deliveryErrorsToday).toBe(1);
    expect(snap.lastSupportSentAt).toBe(DAY1 + 1000);
    expect(snap.queueDepth).toBe(2);
  });

  it('обнуляет суточные счётчики при смене суток UTC, сохраняя последнее уведомление', () => {
    const stats = new DeliveryStats();
    stats.recordSupportSent(DAY1);
    stats.recordHeartbeat(DAY1);
    const snap = stats.snapshot(DAY2, 0);
    expect(snap.supportSentToday).toBe(0);
    expect(snap.deliveryErrorsToday).toBe(0);
    expect(snap.heartbeatSentToday).toBe(false);
    expect(snap.lastSupportSentAt).toBe(DAY1);
  });

  it('фиксирует отправку ежедневного сигнала', () => {
    const stats = new DeliveryStats();
    stats.recordHeartbeat(DAY1);
    expect(stats.snapshot(DAY1, 0).heartbeatSentToday).toBe(true);
  });
});
