// Конфигурация из переменных окружения.
// Ошибки конфигурации не мешают запуску: бот стартует в деградированном режиме,
// /healthz сообщает об этом, приём событий отключается (503).

export interface BotConfig {
  port: number;
  telegramBotToken: string;
  telegramChatId: string;
  inboundAuthToken: string;
  webappBaseUrl: string;
  supportNotifyEnabled: boolean;
  heartbeatEnabled: boolean;
  queueMaxItems: number;
  dedupMemory: number;
  telegramRequestTimeoutMs: number;
}

export interface ConfigResult {
  config: BotConfig;
  /** Безопасные описания ошибок: только имена переменных, без значений. */
  errors: string[];
}

function parsePort(value: string | undefined): number | null {
  const port = Number.parseInt(value ?? '', 10);
  if (!Number.isInteger(port) || port < 1 || port > 65535) return null;
  return port;
}

function parseBool(value: string | undefined, defaultBool: boolean): boolean {
  if (value === undefined) return defaultBool;
  return !['0', 'false', 'no', 'off'].includes(value.trim().toLowerCase());
}

function clampInt(value: string | undefined, min: number, max: number, fallback: number): number {
  const parsed = Number.parseInt(value ?? '', 10);
  if (!Number.isInteger(parsed) || parsed < min || parsed > max) return fallback;
  return parsed;
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): ConfigResult {
  const errors: string[] = [];
  const telegramBotToken = (env.TELEGRAM_BOT_TOKEN ?? '').trim();
  const telegramChatId = (env.TELEGRAM_CHAT_ID ?? '').trim();
  const inboundAuthToken = (env.INBOUND_AUTH_TOKEN ?? '').trim();
  const webappBaseUrl = (env.WEBAPP_BASE_URL ?? 'http://webapp:8080').trim().replace(/\/+$/, '');

  if (telegramBotToken.length === 0) errors.push('TELEGRAM_BOT_TOKEN не задан');
  if (telegramChatId.length === 0) errors.push('TELEGRAM_CHAT_ID не задан');
  if (inboundAuthToken.length === 0) errors.push('INBOUND_AUTH_TOKEN не задан');

  return {
    config: {
      port: parsePort(env.PORT) ?? 8080,
      telegramBotToken,
      telegramChatId,
      inboundAuthToken,
      webappBaseUrl,
      supportNotifyEnabled: parseBool(env.SUPPORT_NOTIFY_ENABLED, true),
      heartbeatEnabled: parseBool(env.HEARTBEAT_ENABLED, true),
      queueMaxItems: clampInt(env.QUEUE_MAX_ITEMS, 1, 10_000, 100),
      dedupMemory: clampInt(env.DEDUP_MEMORY, 1, 100_000, 1000),
      telegramRequestTimeoutMs: clampInt(env.TELEGRAM_REQUEST_TIMEOUT_MS, 1000, 120_000, 30_000),
    },
    errors,
  };
}
