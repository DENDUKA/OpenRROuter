// Транспорт Telegram Bot API.
// Правила повторов (BOT-001):
//   - 429: ждём retry_after (с потолком), повторяем; всего не больше maxAttempts;
//   - 5xx: нарастающая пауза, повторяем в пределах maxAttempts;
//   - прочие 4xx: постоянная ошибка, без повтора;
//   - сетевой сбой/таймаут: результат «unknown» (запрос мог дойти), без повтора,
//     чтобы не плодить дубли; absolute exactly-once не обещается.
// Токен не попадает в логи: ошибки проходят через sanitizeError.

import { sanitizeError, type SafeLogger } from './safelog.js';

export type SendOutcome = 'sent' | 'failed' | 'unknown';

export interface TelegramTransport {
  sendMessage(text: string): Promise<SendOutcome>;
}

export interface TelegramTransportDeps {
  fetchFn?: typeof fetch;
  sleep?: (ms: number) => Promise<void>;
  logger?: SafeLogger;
  requestTimeoutMs?: number;
  maxAttempts?: number;
  maxRetryAfterMs?: number;
}

export interface TelegramOptions {
  botToken: string;
  chatId: string;
}

const DEFAULT_REQUEST_TIMEOUT_MS = 10_000;
const DEFAULT_MAX_ATTEMPTS = 3;
const DEFAULT_MAX_RETRY_AFTER_MS = 30_000;

async function readRetryAfterMs(res: Response): Promise<number> {
  try {
    const body = (await res.json()) as { parameters?: { retry_after?: number } };
    const seconds = body.parameters?.retry_after;
    return typeof seconds === 'number' && Number.isFinite(seconds) && seconds > 0 ? seconds * 1000 : 0;
  } catch {
    return 0;
  }
}

export function createTelegramTransport(options: TelegramOptions, deps: TelegramTransportDeps = {}): TelegramTransport {
  const fetchFn = deps.fetchFn ?? fetch;
  const sleep =
    deps.sleep ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)));
  const logger = deps.logger;
  const requestTimeoutMs = deps.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;
  const maxAttempts = deps.maxAttempts ?? DEFAULT_MAX_ATTEMPTS;
  const maxRetryAfterMs = deps.maxRetryAfterMs ?? DEFAULT_MAX_RETRY_AFTER_MS;

  async function sendMessage(text: string): Promise<SendOutcome> {
    const url = `https://api.telegram.org/bot${options.botToken}/sendMessage`;
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), requestTimeoutMs);
      let res: Response;
      try {
        res = await fetchFn(url, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({
            chat_id: options.chatId,
            text,
            link_preview_options: { is_disabled: true },
          }),
          signal: controller.signal,
        });
      } catch (err) {
        clearTimeout(timer);
        logger?.warn('telegram_send_unknown', { attempt, reason: sanitizeError(err, options.botToken) });
        return 'unknown';
      }
      clearTimeout(timer);

      if (res.ok) return 'sent';

      if (res.status === 429 || res.status >= 500) {
        const waitMs =
          res.status === 429
            ? Math.min(await readRetryAfterMs(res), maxRetryAfterMs) || attempt * 1000
            : attempt * 1000;
        if (attempt < maxAttempts) {
          await sleep(waitMs);
          continue;
        }
        logger?.warn('telegram_send_retries_exhausted', { attempt, status: res.status });
        return 'failed';
      }

      logger?.error('telegram_send_rejected', { attempt, status: res.status });
      return 'failed';
    }
    return 'failed';
  }

  return { sendMessage };
}
