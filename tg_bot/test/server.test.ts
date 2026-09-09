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
});

describe('POST /v1/events/registration', () => {
  const regPayload = (id: string) => ({
    id,
    type: 'user_registered',
    occurredAt: '2026-09-07T09:00:00Z',
    payload: { email: 'user@example.com' },
  });

  it('принимает валидное событие регистрации (202)', async () => {
    const app = makeApp();
    const res = await app.inject({
      method: 'POST',
      url: '/v1/events/registration',
      headers: authHeaders,
      payload: regPayload('reg-1'),
    });
    expect(res.statusCode).toBe(202);
    expect(JSON.parse(res.body)).toEqual({ status: 'queued' });
  });
});

describe('POST /v1/events/topup', () => {
  const topupPayload = (id: string) => ({
    id,
    type: 'topup_completed',
    occurredAt: '2026-09-08T10:00:00Z',
    payload: {
      email: 'payer@example.com',
      amountRub: 1500,
      creditedUsd: 16.25,
      provider: 'Rollypay (СБП)',
    },
  });

  it('принимает валидное событие пополнения (202)', async () => {
    const app = makeApp();
    const res = await app.inject({
      method: 'POST',
      url: '/v1/events/topup',
      headers: authHeaders,
      payload: topupPayload('top-1'),
    });
    expect(res.statusCode).toBe(202);
    expect(JSON.parse(res.body)).toEqual({ status: 'queued' });
  });
});

describe('POST /v1/events/first-api-request', () => {
  const firstReqPayload = (id: string) => ({
    id,
    type: 'first_api_request',
    occurredAt: '2026-09-08T10:00:00Z',
    payload: {
      email: 'user@example.com',
      model: 'anthropic/claude-3-5-sonnet',
    },
  });

  it('принимает валидное событие первого запроса (202)', async () => {
    const app = makeApp();
    const res = await app.inject({
      method: 'POST',
      url: '/v1/events/first-api-request',
      headers: authHeaders,
      payload: firstReqPayload('first-1'),
    });
    expect(res.statusCode).toBe(202);
    expect(JSON.parse(res.body)).toEqual({ status: 'queued' });
  });
});
