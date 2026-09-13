// HTTP API бота: закрытый приём событий от WebApp и BillingService.
// Роуты:
//   POST /v1/events/support           — обращение Support (Bearer INBOUND_AUTH_TOKEN);
//   POST /v1/events/registration      — регистрация пользователя (Bearer INBOUND_AUTH_TOKEN);
//   POST /v1/events/topup             — успешное пополнение (Bearer INBOUND_AUTH_TOKEN);
//   POST /v1/events/first-api-request — первый API-запрос (Bearer INBOUND_AUTH_TOKEN);
//   POST /v1/events/payment-created   — создание платежа (Bearer INBOUND_AUTH_TOKEN);
//   GET  /healthz                     — безопасная диагностика без секретов.
// Принятие в очередь (202) не означает доставку в Telegram.

import { createHash, timingSafeEqual } from 'node:crypto';
import Fastify, { type FastifyInstance } from 'fastify';
import type { BotConfig } from './config.js';
import {
  parseFirstApiRequestEvent,
  parsePaymentCreatedEvent,
  parseSupportTicketEvent,
  parseTopUpCompletedEvent,
  parseUserRegisteredEvent,
} from './events.js';
import type { BoundedEventQueue } from './queue.js';
import type { DeliveryStats } from './stats.js';

export interface AppDeps {
  config: BotConfig;
  configErrors: string[];
  queue: BoundedEventQueue;
  stats: DeliveryStats;
  now?: () => number;
}

const BODY_LIMIT_BYTES = 64 * 1024;

function constantTimeEqual(a: string, b: string): boolean {
  const da = createHash('sha256').update(a).digest();
  const db = createHash('sha256').update(b).digest();
  return timingSafeEqual(da, db);
}

export function buildApp(deps: AppDeps): FastifyInstance {
  const app = Fastify({ logger: false, bodyLimit: BODY_LIMIT_BYTES });
  const now = deps.now ?? Date.now;

  const authOk = (header: unknown): boolean => {
    if (typeof header !== 'string') return false;
    return constantTimeEqual(header, `Bearer ${deps.config.inboundAuthToken}`);
  };

  app.post('/v1/events/support', async (request, reply) => {
    if (!authOk(request.headers.authorization)) {
      return reply.code(401).send({ error: 'unauthorized' });
    }
    if (deps.configErrors.length > 0) {
      return reply.code(503).send({ error: 'bot_misconfigured' });
    }
    if (!deps.config.supportNotifyEnabled) {
      return reply.code(503).send({ error: 'notifications_disabled' });
    }
    const body = request.body as Record<string, unknown> | null;
    let parsed;
    if (body?.type === 'user_registered') {
      parsed = parseUserRegisteredEvent(request.body);
    } else if (body?.type === 'topup_completed') {
      parsed = parseTopUpCompletedEvent(request.body);
    } else if (body?.type === 'first_api_request') {
      parsed = parseFirstApiRequestEvent(request.body);
    } else if (body?.type === 'payment_created') {
      parsed = parsePaymentCreatedEvent(request.body);
    } else {
      parsed = parseSupportTicketEvent(request.body);
    }
    if (!parsed.ok) {
      return reply.code(400).send({ error: 'invalid_event', fields: parsed.errors });
    }
    const result = deps.queue.push(parsed.event, now());
    if (result === 'accepted') return reply.code(202).send({ status: 'queued' });
    if (result === 'duplicate') return reply.code(200).send({ status: 'duplicate' });
    return reply.code(503).send({ status: 'queue_full' });
  });

  app.post('/v1/events/registration', async (request, reply) => {
    if (!authOk(request.headers.authorization)) {
      return reply.code(401).send({ error: 'unauthorized' });
    }
    if (deps.configErrors.length > 0) {
      return reply.code(503).send({ error: 'bot_misconfigured' });
    }
    if (!deps.config.supportNotifyEnabled) {
      return reply.code(503).send({ error: 'notifications_disabled' });
    }
    const parsed = parseUserRegisteredEvent(request.body);
    if (!parsed.ok) {
      return reply.code(400).send({ error: 'invalid_event', fields: parsed.errors });
    }
    const result = deps.queue.push(parsed.event, now());
    if (result === 'accepted') return reply.code(202).send({ status: 'queued' });
    if (result === 'duplicate') return reply.code(200).send({ status: 'duplicate' });
    return reply.code(503).send({ status: 'queue_full' });
  });

  app.post('/v1/events/topup', async (request, reply) => {
    if (!authOk(request.headers.authorization)) {
      return reply.code(401).send({ error: 'unauthorized' });
    }
    if (deps.configErrors.length > 0) {
      return reply.code(503).send({ error: 'bot_misconfigured' });
    }
    if (!deps.config.supportNotifyEnabled) {
      return reply.code(503).send({ error: 'notifications_disabled' });
    }
    const parsed = parseTopUpCompletedEvent(request.body);
    if (!parsed.ok) {
      return reply.code(400).send({ error: 'invalid_event', fields: parsed.errors });
    }
    const result = deps.queue.push(parsed.event, now());
    if (result === 'accepted') return reply.code(202).send({ status: 'queued' });
    if (result === 'duplicate') return reply.code(200).send({ status: 'duplicate' });
    return reply.code(503).send({ status: 'queue_full' });
  });

  app.post('/v1/events/first-api-request', async (request, reply) => {
    if (!authOk(request.headers.authorization)) {
      return reply.code(401).send({ error: 'unauthorized' });
    }
    if (deps.configErrors.length > 0) {
      return reply.code(503).send({ error: 'bot_misconfigured' });
    }
    if (!deps.config.supportNotifyEnabled) {
      return reply.code(503).send({ error: 'notifications_disabled' });
    }
    const parsed = parseFirstApiRequestEvent(request.body);
    if (!parsed.ok) {
      return reply.code(400).send({ error: 'invalid_event', fields: parsed.errors });
    }
    const result = deps.queue.push(parsed.event, now());
    if (result === 'accepted') return reply.code(202).send({ status: 'queued' });
    if (result === 'duplicate') return reply.code(200).send({ status: 'duplicate' });
    return reply.code(503).send({ status: 'queue_full' });
  });

  app.post('/v1/events/payment-created', async (request, reply) => {
    if (!authOk(request.headers.authorization)) {
      return reply.code(401).send({ error: 'unauthorized' });
    }
    if (deps.configErrors.length > 0) {
      return reply.code(503).send({ error: 'bot_misconfigured' });
    }
    if (!deps.config.supportNotifyEnabled) {
      return reply.code(503).send({ error: 'notifications_disabled' });
    }
    const parsed = parsePaymentCreatedEvent(request.body);
    if (!parsed.ok) {
      return reply.code(400).send({ error: 'invalid_event', fields: parsed.errors });
    }
    const result = deps.queue.push(parsed.event, now());
    if (result === 'accepted') return reply.code(202).send({ status: 'queued' });
    if (result === 'duplicate') return reply.code(200).send({ status: 'duplicate' });
    return reply.code(503).send({ status: 'queue_full' });
  });

  app.get('/healthz', async (_request, reply) => {
    const snapshot = deps.stats.snapshot(now(), deps.queue.size());
    const degraded = deps.configErrors.length > 0 || !deps.config.supportNotifyEnabled;
    return reply.send({
      status: degraded ? 'degraded' : 'ok',
      supportNotifyEnabled: deps.config.supportNotifyEnabled,
      queueDepth: snapshot.queueDepth,
      supportSentToday: snapshot.supportSentToday,
      deliveryErrorsToday: snapshot.deliveryErrorsToday,
    });
  });

  return app;
}
