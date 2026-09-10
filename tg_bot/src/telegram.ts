// Транспорт Telegram Bot API.
// Правила повторов (BOT-001):
//   - 429: ждём retry_after (с потолком), повторяем; всего не больше maxAttempts;
//   - 5xx: нарастающая пауза, повторяем в пределах maxAttempts;
//   - прочие 4xx: постоянная ошибка, без повтора;
//   - сбой подключения (connect error): запрос гарантированно не ушёл в Telegram,
//     повторяем до maxAttempts с нарастающей паузой;
//   - таймаут ответа/неизвестный сбой: результат «unknown» (запрос мог дойти), без повтора,
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

const DEFAULT_REQUEST_TIMEOUT_MS = 30_000;
const DEFAULT_MAX_ATTEMPTS = 3;
const DEFAULT_MAX_RETRY_AFTER_MS = 30_000;

export function isConnectError(err: unknown): boolean {
  if (!err || typeof err !== 'object') return false;

  const checkSingle = (e: unknown): boolean => {
    if (!e || typeof e !== 'object') return false;
    const rec = e as Record<string, unknown>;
    if (rec.name === 'ConnectTimeoutError') return true;
    if (rec.syscall === 'connect' || rec.syscall === 'getaddrinfo') return true;
    if (typeof rec.code === 'string') {
      const connectCodes = [
        'ECONNREFUSED',
        'ENETUNREACH',
        'EHOSTUNREACH',
        'EADDRNOTAVAIL',
        'ENETDOWN',
        'ENOTFOUND',
        'ETIMEDOUT',
        'UND_ERR_CONNECT_TIMEOUT',
        'UND_ERR_SOCKET',
      ];
      if (connectCodes.includes(rec.code)) return true;
    }
    return false;
  };

  const anyErr = err as { cause?: unknown };
  if (checkSingle(anyErr)) return true;
  if (anyErr.cause && checkSingle(anyErr.cause)) return true;

  const cause = (anyErr.cause ?? anyErr) as { errors?: unknown[] };
  if (Array.isArray(cause.errors) && cause.errors.length > 0) {
    if (cause.errors.every(checkSingle)) return true;
  }

  return false;
}

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
  let activeChatId = options.chatId;
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
            chat_id: activeChatId,
            text,
            link_preview_options: { is_disabled: true },
          }),
          signal: controller.signal,
        });
      } catch (err) {
        clearTimeout(timer);
        if (isConnectError(err)) {
          if (attempt < maxAttempts) {
            logger?.warn('telegram_send_connect_retry', {
              attempt,
              reason: sanitizeError(err, options.botToken),
            });
            await sleep(attempt * 1000);
            continue;
          }
          logger?.error('telegram_send_connect_failed', {
            attempt,
            reason: sanitizeError(err, options.botToken),
          });
          return 'failed';
        }
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

      let description: string | undefined;
      let migrateToChatId: number | undefined;
      try {
        const body = (await res.json()) as {
          description?: string;
          parameters?: { migrate_to_chat_id?: number };
        };
        if (typeof body.description === 'string') {
          description = body.description.slice(0, 200);
        }
        if (typeof body.parameters?.migrate_to_chat_id === 'number') {
          migrateToChatId = body.parameters.migrate_to_chat_id;
        }
      } catch {
        // ignore parse error
      }

      if (migrateToChatId !== undefined) {
        logger?.warn('telegram_chat_migrated', {
          oldChatId: activeChatId,
          newChatId: migrateToChatId,
        });
        activeChatId = String(migrateToChatId);
        if (attempt < maxAttempts) {
          continue;
        }
      }

      logger?.error('telegram_send_rejected', {
        attempt,
        status: res.status,
        ...(description ? { description: sanitizeError(description, options.botToken) } : {}),
        ...(migrateToChatId !== undefined ? { migrateToChatId } : {}),
      });
      return 'failed';
    }
    return 'failed';
  }

  return { sendMessage };
}
