import { describe, expect, it } from 'vitest';
import { loadConfig } from '../src/config.js';
import { buildApp } from '../src/server.js';
import { BoundedEventQueue } from '../src/queue.js';
import { DeliveryStats } from '../src/stats.js';
import { formatEventNotification } from '../src/format.js';

const cases = [
  ['support', 'support_ticket', { email: 'user@example.com', subject: 'Help', message: 'Test' }],
  ['registration', 'user_registered', { email: 'user@example.com' }],
  ['topup', 'topup_completed', { email: 'user@example.com', amountRub: 100, creditedUsd: 1, provider: 'test' }],
  ['first-api-request', 'first_api_request', { email: 'user@example.com', model: 'test-model' }],
  ['payment-created', 'payment_created', { email: 'user@example.com', amountRub: 100, creditedUsd: 1, provider: 'test' }],
] as const;

describe.each(cases)('%s route', (route, type, payload) => {
  it('auth, validation, deduplication, overflow, queue → formatting', async () => {
    const { config } = loadConfig({ TELEGRAM_BOT_TOKEN: 'test', TELEGRAM_CHAT_ID: '1', INBOUND_AUTH_TOKEN: 'test' });
    const queue = new BoundedEventQueue(1, 10);
    const app = buildApp({ config, configErrors: [], queue, stats: new DeliveryStats() });
    const url = `/v1/events/${route}`;
    const event = { id: 'a', type, occurredAt: '2026-09-09T07:00:00Z', payload };
    const headers = { authorization: 'Bearer test' };
    try {
      expect((await app.inject({ method: 'POST', url, payload: event })).statusCode).toBe(401);
      expect((await app.inject({ method: 'POST', url, headers: { authorization: 'Bearer wrong' }, payload: event })).statusCode).toBe(401);
      expect((await app.inject({ method: 'POST', url, headers, payload: { ...event, payload: {} } })).statusCode).toBe(400);
      expect((await app.inject({ method: 'POST', url, headers, payload: event })).statusCode).toBe(202);
      expect((await app.inject({ method: 'POST', url, headers, payload: event })).statusCode).toBe(200);
      expect((await app.inject({ method: 'POST', url, headers, payload: { ...event, id: 'b' } })).statusCode).toBe(503);
      const item = await queue.take();
      expect(item?.event.type).toBe(type);
      expect(formatEventNotification(item!.event)).toContain('user@example.com');
    } finally { queue.stop(); await app.close(); }
  });
});
