// Ограниченная очередь событий в памяти (best-effort, BOT-001).
// Не переживает рестарт: устойчивое хранение — отдельная задача BOT-002.
// Дедупликация по id тоже в памяти и ограничена объёмом.

import type { BotEvent } from './events.js';

export interface QueueItem {
  event: BotEvent;
  receivedAt: number;
}

export type PushResult = 'accepted' | 'duplicate' | 'full';

export class BoundedEventQueue {
  private items: QueueItem[] = [];
  private seen = new Map<string, true>();
  private waiters: Array<(item: QueueItem | null) => void> = [];
  private stopped = false;
  readonly stats = { accepted: 0, duplicates: 0, overflowed: 0 };

  constructor(
    private readonly maxItems: number,
    private readonly dedupCapacity: number,
  ) {}

  push(event: BotEvent, receivedAt: number = Date.now()): PushResult {
    if (this.stopped) return 'full';
    if (this.seen.has(event.id)) {
      this.stats.duplicates++;
      return 'duplicate';
    }
    if (this.items.length >= this.maxItems) {
      this.stats.overflowed++;
      return 'full';
    }
    this.seen.set(event.id, true);
    if (this.seen.size > this.dedupCapacity) {
      const oldest = this.seen.keys().next().value;
      if (oldest !== undefined) this.seen.delete(oldest);
    }
    const item: QueueItem = { event, receivedAt };
    this.stats.accepted++;
    const waiter = this.waiters.shift();
    if (waiter) {
      waiter(item);
    } else {
      this.items.push(item);
    }
    return 'accepted';
  }

  /** Резолвится пришедшим элементом или null после stop(). */
  take(): Promise<QueueItem | null> {
    const item = this.items.shift();
    if (item !== undefined) return Promise.resolve(item);
    if (this.stopped) return Promise.resolve(null);
    return new Promise((resolve) => {
      this.waiters.push(resolve);
    });
  }

  size(): number {
    return this.items.length;
  }

  stop(): void {
    this.stopped = true;
    for (const waiter of this.waiters) waiter(null);
    this.waiters = [];
  }
}
