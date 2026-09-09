// Безопасное логирование: JSON-строки в stdout, без токенов и персональных данных.
// Транспорт Telegram самостоятельно вырезает секрет из текстов ошибок (см. telegram.ts).

export type LogLevel = 'debug' | 'info' | 'warn' | 'error';

const LEVEL_ORDER: Record<LogLevel, number> = { debug: 0, info: 1, warn: 2, error: 3 };

export interface SafeLogger {
  debug(message: string, fields?: Record<string, unknown>): void;
  info(message: string, fields?: Record<string, unknown>): void;
  warn(message: string, fields?: Record<string, unknown>): void;
  error(message: string, fields?: Record<string, unknown>): void;
}

export function createSafeLogger(minLevel: LogLevel = 'info'): SafeLogger {
  const write = (level: LogLevel, message: string, fields?: Record<string, unknown>): void => {
    if (LEVEL_ORDER[level] < LEVEL_ORDER[minLevel]) return;
    const line = JSON.stringify({ t: new Date().toISOString(), level, msg: message, ...fields });
    process.stdout.write(line + '\n');
  };
  return {
    debug: (m, f) => write('debug', m, f),
    info: (m, f) => write('info', m, f),
    warn: (m, f) => write('warn', m, f),
    error: (m, f) => write('error', m, f),
  };
}

/** Готовит текст ошибки к логированию: вырезает секрет и ограничивает длину. */
export function sanitizeError(err: unknown, secret: string): string {
  let text = err instanceof Error ? `${err.name}: ${err.message}` : String(err);
  if (err && typeof err === 'object' && 'cause' in err && (err as { cause?: unknown }).cause) {
    const cause = (err as { cause?: unknown }).cause;
    const causeText = cause instanceof Error ? `${cause.name}: ${cause.message}` : String(cause);
    text += ` (cause: ${causeText})`;
  }
  if (secret.length > 0) text = text.split(secret).join('***');
  if (text.length > 400) text = text.slice(0, 400) + '…';
  return text;
}
