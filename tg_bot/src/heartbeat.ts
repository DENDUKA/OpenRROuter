// Ежедневный сигнал «утренняя сводка / бот жив» в 10:00 UTC+3 = 07:00 UTC, фиксированный пояс,
// независимо от часового пояса контейнера.
// В 10:00 бот опрашивает WebApp (GET /internal/bot/daily-summary) для отправки утренней бизнес-сводки
// (новые регистрации, пополнения, уникальные посетители). При недоступности WebApp
// отправляется технический сигнал «Бот жив».

import { formatDailySummaryMessage, type DailySummaryData } from './format.js';

export const HEARTBEAT_UTC_HOUR = 7;
export const DEFAULT_CATCHUP_DELAY_MS = 5_000;

export interface HeartbeatSnapshot {
  queueDepth: number;
  supportSentToday: number;
  lastSupportSentAt: number | null;
  deliveryErrorsToday: number;
  heartbeatSentToday: boolean;
}

export type HeartbeatSendFn = (text: string) => Promise<unknown>;
export type HeartbeatGetState = () => HeartbeatSnapshot;

export interface DailySummaryFetcher {
  fetchSummary(): Promise<DailySummaryData | null>;
}

export interface HeartbeatSchedulerDeps {
  now?: () => number;
  setTimer?: (callback: () => void, delayMs: number) => () => void;
  catchUpDelayMs?: number;
  summaryFetcher?: DailySummaryFetcher;
}

export function nextHeartbeatUtc(now: Date): Date {
  const candidate = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), HEARTBEAT_UTC_HOUR, 0, 0, 0),
  );
  if (candidate.getTime() <= now.getTime()) candidate.setUTCDate(candidate.getUTCDate() + 1);
  return candidate;
}

function todayHeartbeatMs(nowMs: number): number {
  const now = new Date(nowMs);
  return Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), HEARTBEAT_UTC_HOUR, 0, 0, 0);
}

export function formatUtc3(ts: number): string {
  const d = new Date(ts + 3 * 3_600_000);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getUTCDate())}.${p(d.getUTCMonth() + 1)}.${d.getUTCFullYear()}, ${p(d.getUTCHours())}:${p(d.getUTCMinutes())} (UTC+3)`;
}

export function formatHeartbeatMessage(state: HeartbeatSnapshot, nowMs: number): string {
  return [
    '🟢 Бот жив — Rawrter Support',
    `Время: ${formatUtc3(nowMs)}`,
    `Уведомлений об обращениях за сутки: ${state.supportSentToday}`,
    `Последнее уведомление об обращении: ${state.lastSupportSentAt !== null ? formatUtc3(state.lastSupportSentAt) : 'ещё не было'}`,
    `Ошибок доставки за сутки: ${state.deliveryErrorsToday}`,
    `В очереди сейчас: ${state.queueDepth}`,
  ].join('\n');
}

export class HeartbeatScheduler {
  private cancelTimer: (() => void) | null = null;
  private stopped = false;

  constructor(
    private readonly send: HeartbeatSendFn,
    private readonly getState: HeartbeatGetState,
    private readonly deps: HeartbeatSchedulerDeps = {},
  ) {}

  start(): void {
    if (this.stopped) return;
    const nowMs = this.now();
    if (nowMs >= todayHeartbeatMs(nowMs) && !this.getState().heartbeatSentToday) {
      this.arm(this.deps.catchUpDelayMs ?? DEFAULT_CATCHUP_DELAY_MS);
    } else {
      this.scheduleNext();
    }
  }

  stop(): void {
    this.stopped = true;
    this.cancelTimer?.();
    this.cancelTimer = null;
  }

  private now(): number {
    return this.deps.now?.() ?? Date.now();
  }

  private scheduleNext(): void {
    const nowMs = this.now();
    const delay = nextHeartbeatUtc(new Date(nowMs)).getTime() - nowMs;
    this.arm(delay);
  }

  private arm(delayMs: number): void {
    this.cancelTimer?.();
    const setTimer =
      this.deps.setTimer ??
      ((cb: () => void, ms: number) => {
        const t = setTimeout(cb, ms);
        return () => clearTimeout(t);
      });
    this.cancelTimer = setTimer(() => void this.fire(), delayMs);
  }

  private async fire(): Promise<void> {
    this.cancelTimer = null;
    if (this.stopped) return;
    const nowMs = this.now();
    let text: string;

    if (this.deps.summaryFetcher) {
      try {
        const summary = await this.deps.summaryFetcher.fetchSummary();
        if (summary) {
          text = formatDailySummaryMessage(summary);
        } else {
          text = formatHeartbeatMessage(this.getState(), nowMs);
        }
      } catch {
        text = formatHeartbeatMessage(this.getState(), nowMs);
      }
    } else {
      text = formatHeartbeatMessage(this.getState(), nowMs);
    }

    try {
      await this.send(text);
    } catch {
      // Транспорт сам логирует ошибки; сигнал повторится на следующий день.
    }
    if (!this.stopped) this.scheduleNext();
  }
}
