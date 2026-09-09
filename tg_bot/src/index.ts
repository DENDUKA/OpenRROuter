// Точка входа бота: HTTP API + воркер доставки + ежедневный сигнал.
// Best-effort: незавершённые события теряются при рестарте (устойчивость — BOT-002).

import { loadConfig } from './config.js';
import { formatEventNotification, type DailySummaryData } from './format.js';
import { HeartbeatScheduler, type DailySummaryFetcher } from './heartbeat.js';
import { BoundedEventQueue } from './queue.js';
import { createSafeLogger, sanitizeError, type SafeLogger } from './safelog.js';
import { buildApp } from './server.js';
import { DeliveryStats } from './stats.js';
import { createTelegramTransport, type SendOutcome, type TelegramTransport } from './telegram.js';

async function runWorker(
  queue: BoundedEventQueue,
  transport: TelegramTransport,
  stats: DeliveryStats,
  logger: SafeLogger,
): Promise<void> {
  for (;;) {
    const item = await queue.take();
    if (item === null) return;
    try {
      const text = formatEventNotification(item.event);
      const outcome = await transport.sendMessage(text);
      const now = Date.now();
      if (outcome === 'sent') {
        stats.recordSupportSent(now);
        logger.info(`${item.event.type}_notified`, { eventId: item.event.id });
      } else {
        stats.recordDeliveryFailure(now);
        logger.warn('notify_failed', { eventId: item.event.id, outcome });
      }
    } catch (err) {
      stats.recordDeliveryFailure(Date.now());
      logger.error('worker_unexpected_error', { eventId: item.event.id, reason: sanitizeError(err, '') });
    }
  }
}

async function main(): Promise<void> {
  const { config, errors } = loadConfig();
  const logger = createSafeLogger();
  if (errors.length > 0) logger.error('bot_misconfigured', { fields: errors });

  const stats = new DeliveryStats();
  const queue = new BoundedEventQueue(config.queueMaxItems, config.dedupMemory);
  const transport = createTelegramTransport(
    { botToken: config.telegramBotToken, chatId: config.telegramChatId },
    { logger },
  );
  const app = buildApp({ config, configErrors: errors, queue, stats });

  void runWorker(queue, transport, stats, logger);

  let heartbeat: HeartbeatScheduler | null = null;
  if (config.heartbeatEnabled) {
    if (errors.length > 0) {
      logger.warn('heartbeat_skipped_misconfigured', {});
    } else {
      const sendHeartbeat = async (text: string): Promise<void> => {
        const outcome: SendOutcome = await transport.sendMessage(text);
        stats.recordHeartbeat(Date.now());
        if (outcome !== 'sent') logger.error('heartbeat_delivery_failed', { outcome });
      };

      const summaryFetcher: DailySummaryFetcher = {
        fetchSummary: async () => {
          try {
            const res = await fetch(`${config.webappBaseUrl}/internal/bot/daily-summary`, {
              headers: {
                authorization: `Bearer ${config.inboundAuthToken}`,
              },
              signal: AbortSignal.timeout(10_000),
            });
            if (!res.ok) return null;
            return (await res.json()) as DailySummaryData;
          } catch {
            return null;
          }
        },
      };

      heartbeat = new HeartbeatScheduler(
        sendHeartbeat,
        () => stats.snapshot(Date.now(), queue.size()),
        { summaryFetcher },
      );
    }
  }

  await app.listen({ port: config.port, host: '0.0.0.0' });
  logger.info('bot_started', { port: config.port, misconfigured: errors.length > 0 });
  heartbeat?.start();

  let shuttingDown = false;
  const shutdown = (signal: string): void => {
    if (shuttingDown) return;
    shuttingDown = true;
    logger.info('bot_stopping', { signal });
    heartbeat?.stop();
    queue.stop();
    void app.close().finally(() => process.exit(0));
  };
  process.on('SIGTERM', () => shutdown('SIGTERM'));
  process.on('SIGINT', () => shutdown('SIGINT'));
}

main().catch((err: unknown) => {
  const { config } = loadConfig();
  const line = JSON.stringify({
    t: new Date().toISOString(),
    level: 'error',
    msg: 'bot_fatal',
    reason: sanitizeError(err, config.telegramBotToken),
  });
  process.stderr.write(line + '\n');
  process.exit(1);
});
