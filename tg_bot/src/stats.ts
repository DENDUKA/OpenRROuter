// Статистика доставки за текущие сутки UTC и время последнего уведомления.
// Используется ежедневным сигналом «бот жив». Счётчики обнуляются при смене суток UTC.

import type { HeartbeatSnapshot } from './heartbeat.js';

export class DeliveryStats {
  private dayKey = '';
  supportSentToday = 0;
  deliveryErrorsToday = 0;
  heartbeatSentToday = false;
  lastSupportSentAt: number | null = null;

  private roll(now: number): void {
    const key = new Date(now).toISOString().slice(0, 10);
    if (key !== this.dayKey) {
      this.dayKey = key;
      this.supportSentToday = 0;
      this.deliveryErrorsToday = 0;
      this.heartbeatSentToday = false;
    }
  }

  recordSupportSent(now: number): void {
    this.roll(now);
    this.supportSentToday++;
    this.lastSupportSentAt = now;
  }

  recordDeliveryFailure(now: number): void {
    this.roll(now);
    this.deliveryErrorsToday++;
  }

  recordHeartbeat(now: number): void {
    this.roll(now);
    this.heartbeatSentToday = true;
  }

  snapshot(now: number, queueDepth: number): HeartbeatSnapshot {
    this.roll(now);
    return {
      queueDepth,
      supportSentToday: this.supportSentToday,
      lastSupportSentAt: this.lastSupportSentAt,
      deliveryErrorsToday: this.deliveryErrorsToday,
      heartbeatSentToday: this.heartbeatSentToday,
    };
  }
}
