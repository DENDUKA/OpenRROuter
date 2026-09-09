import { afterAll, describe, expect, it } from 'vitest';
import type { FastifyInstance } from 'fastify';
import { loadConfig } from '../src/config.js';
import { BoundedEventQueue } from '../src/queue.js';
import { buildApp } from '../src/server.js';
import { DeliveryStats } from '../src/stats.js';

const apps: FastifyInstance[] = [];
afterAll(async () => {
  for (const app of apps) await app.close();
});

interface Overrides {
  configErrors?: string[];
  queueMaxItems?: number;
  supportNotifyEnabled?: boolean;
}

function makeApp(overrides: Overrides = {}) {
  const { config } = loadConfig({
    TELEGRAM_BOT_TOKEN: 'synthetic-token',
    TELEGRAM_CHAT_ID: '1',
    INBOUND_AUTH_TOKEN: 'inbound-secret',
  });
  const effective = {
    ...config,
    queueMaxItems: overrides.queueMaxItems ?? config.queueMaxItems,
    supportNotifyEnabled: overrides.supportNotifyEnabled ?? true,
  };
  const queue = new BoundedEventQueue(effective.queueMaxItems, 100);
  const stats = new DeliveryStats();
  const app = buildApp({ config: effective, configErrors: overrides.configErrors ?? [], queue, stats });
  apps.push(app);
  return app;
}

const event = (id: string) => ({
  id,
  type: 'support_ticket',
  occurredAt: '2026-09-07T09:00:00Z',
  payload: { email: 'user@example.com', subject: 'Тема', message: 'Текст' },
});

const authHeaders = { authorization: 'Bearer inbound-secret', 'content-type': 'application/json' };

describe('POST /v1/events/support', () => {
  it('отклоняет запрос без авторизации и с неверным токеном', async () => {
    const app = makeApp();
    const noAuth = await app.inject({ method: 'POST', url: '/v1/events/support', payload: event('a') });
    expect(noAuth.statusCode).toBe(401);
    const badAuth = await app.inject({
      method: 'POST',
      url: '/v1/events/support',
      headers: { authorization: 'Bearer wrong', 'content-type': 'application/json' },
      payload: event('a'),
    });
    expect(badAuth.statusCode).toBe(401);
    expect(badAuth.body.includes('inbound-secret')).toBe(false);
  });

  it('принимает валидное событие (202) и дедуплицирует повтор (200)', async () => {
    const app = makeApp();
    const first = await app.inject({ method: 'POST', url: '/v1/events/support', headers: authHeaders, payload: event('a') });
    expect(first.statusCode).toBe(202);
    expect(first.json()).toEqual({ status: 'queued' });
    const second = await app.inject({ method: 'POST', url: '/v1/events/support', headers: authHeaders, payload: event('a') });
    expect(second.statusCode).toBe(200);
    expect(second.json()).toEqual({ status: 'duplicate' });
  });

  it('отклоняет неверный payload с перечнем полей', async () => {
    const app = makeApp();
    const bad = await app.inject({
      method: 'POST',
      url: '/v1/events/support',
      headers: authHeaders,
      payload: { ...event('a'), payload: { email: 'user@example.com' } },
    });
    expect(bad.statusCode).toBe(400);
    const body = bad.json();
    expect(body.error).toBe('invalid_event');
    expect(Array.isArray(body.fields)).toBe(true);
  });

  it('отклоняет чужой тип события', async () => {
    const app = makeApp();
    const bad = await app.inject({
      method: 'POST',
      url: '/v1/events/support',
      headers: authHeaders,
      payload: { ...event('a'), type: 'ad_hoc_message' },
    });
    expect(bad.statusCode).toBe(400);
  });

  it('возвращает 503 при переполнении очереди', async () => {
    const app = makeApp({ queueMaxItems: 1 });
    await app.inject({ method: 'POST', url: '/v1/events/support', headers: authHeaders, payload: event('a') });
    const overflow = await app.inject({ method: 'POST', url: '/v1/events/support', headers: authHeaders, payload: event('b') });
    expect(overflow.statusCode).toBe(503);
    expect(overflow.json()).toEqual({ status: 'queue_full' });
  });

  it('возвращает 503 при выключенных уведомлениях', async () => {
    const app = makeApp({ supportNotifyEnabled: false });
    const res = await app.inject({ method: 'POST', url: '/v1/events/support', headers: authHeaders, payload: event('a') });
    expect(res.statusCode).toBe(503);
    expect(res.json()).toEqual({ error: 'notifications_disabled' });
  });

  it('возвращает 503 при ошибках конфигурации', async () => {
    const app = makeApp({ configErrors: ['TELEGRAM_BOT_TOKEN не задан'] });
    const res = await app.inject({ method: 'POST', url: '/v1/events/support', headers: authHeaders, payload: event('a') });
    expect(res.statusCode).toBe(503);
    expect(res.json()).toEqual({ error: 'bot_misconfigured' });
    expect(res.body.includes('TELEGRAM_BOT_TOKEN')).toBe(false);
  });

  it('отклоняет слишком большое тело', async () => {
    const app = makeApp();
    const res = await app.inject({
      method: 'POST',
      url: '/v1/events/support',
      headers: authHeaders,
      payload: { ...event('a'), payload: { email: 'user@example.com', subject: 'Тема', message: 'м'.repeat(70_000) } },
    });
    expect(res.statusCode).toBe(413);
  });
});

describe('GET /healthz', () => {
  it('сообщает ok с глубиной очереди и без секретов', async () => {
    const app = makeApp();
    await app.inject({ method: 'POST', url: '/v1/events/support', headers: authHeaders, payload: event('a') });
    const res = await app.inject({ method: 'GET', url: '/healthz' });
    expect(res.statusCode).toBe(200);
    const body = res.json();
    expect(body.status).toBe('ok');
    expect(body.queueDepth).toBe(1);
    expect(res.body.includes('synthetic-token')).toBe(false);
    expect(res.body.includes('inbound-secret')).toBe(false);
  });

  it('сообщает degraded при ошибках конфигурации или выключенных уведомлениях', async () => {
    const degradedApp = makeApp({ configErrors: ['TELEGRAM_BOT_TOKEN не задан'] });
    const res = await degradedApp.inject({ method: 'GET', url: '/healthz' });
    expect(res.json().status).toBe('degraded');
    const disabledApp = makeApp({ supportNotifyEnabled: false });
    const res2 = await disabledApp.inject({ method: 'GET', url: '/healthz' });
    expect(res2.json().status).toBe('degraded');
  });
});
