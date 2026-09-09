import { describe, expect, it } from 'vitest';
import { createTelegramTransport } from '../src/telegram.js';
import type { SafeLogger } from '../src/safelog.js';

interface MockStep {
  status: number;
  body?: Record<string, unknown>;
}

function mockTransport(steps: Array<MockStep | Error>, token = 'synthetic-token') {
  const calls: Array<{ url: string; body: Record<string, unknown> }> = [];
  const waits: number[] = [];
  const warn: Array<[string, Record<string, unknown> | undefined]> = [];
  const errors: Array<[string, Record<string, unknown> | undefined]> = [];
  const logger: SafeLogger = {
    debug: () => {},
    info: () => {},
    warn: (m, f) => {
      warn.push([m, f]);
    },
    error: (m, f) => {
      errors.push([m, f]);
    },
  };
  const fetchFn = (async (url: string, init?: RequestInit) => {
    calls.push({ url, body: JSON.parse(String(init?.body)) as Record<string, unknown> });
    const step = steps.shift();
    if (step === undefined) throw new Error('мок исчерпан');
    if (step instanceof Error) throw step;
    return new Response(JSON.stringify(step.body ?? { ok: true }), { status: step.status });
  }) as typeof fetch;
  const transport = createTelegramTransport(
    { botToken: token, chatId: '123456' },
    { fetchFn, sleep: async (ms) => void waits.push(ms), logger },
  );
  return { transport, calls, waits, warn, errors };
}

describe('createTelegramTransport', () => {
  it('доставляет сообщение и отключает предпросмотр ссылок', async () => {
    const { transport, calls } = mockTransport([{ status: 200 }]);
    expect(await transport.sendMessage('текст')).toBe('sent');
    expect(calls.length).toBe(1);
    expect(calls[0]?.url.startsWith('https://api.telegram.org/bot')).toBe(true);
    expect(calls[0]?.body.chat_id).toBe('123456');
    expect(calls[0]?.body.text).toBe('текст');
    expect(calls[0]?.body.link_preview_options).toEqual({ is_disabled: true });
    expect(calls[0]?.body).not.toHaveProperty('parse_mode');
  });

  it('уважает retry_after при 429', async () => {
    const { transport, calls, waits } = mockTransport([
      { status: 429, body: { parameters: { retry_after: 7 } } },
      { status: 200 },
    ]);
    expect(await transport.sendMessage('текст')).toBe('sent');
    expect(waits).toEqual([7000]);
    expect(calls.length).toBe(2);
  });

  it('ограничивает число попыток при постоянном 429', async () => {
    const { transport, calls, waits, warn } = mockTransport([
      { status: 429, body: { parameters: { retry_after: 1 } } },
      { status: 429, body: { parameters: { retry_after: 1 } } },
      { status: 429, body: { parameters: { retry_after: 1 } } },
    ]);
    expect(await transport.sendMessage('текст')).toBe('failed');
    expect(calls.length).toBe(3);
    expect(waits.length).toBe(2);
    expect(warn.some(([m]) => m === 'telegram_send_retries_exhausted')).toBe(true);
  });

  it('повторяет 5xx с нарастающей паузой', async () => {
    const { transport, calls, waits } = mockTransport([{ status: 500 }, { status: 503 }, { status: 200 }]);
    expect(await transport.sendMessage('текст')).toBe('sent');
    expect(waits).toEqual([1000, 2000]);
    expect(calls.length).toBe(3);
  });

  it('не повторяет постоянную ошибку 4xx', async () => {
    const { transport, calls, waits, errors } = mockTransport([{ status: 400 }]);
    expect(await transport.sendMessage('текст')).toBe('failed');
    expect(calls.length).toBe(1);
    expect(waits).toEqual([]);
    expect(errors.some(([m]) => m === 'telegram_send_rejected')).toBe(true);
  });

  it('сетевой сбой даёт unknown без повторов', async () => {
    const { transport, calls, waits, warn } = mockTransport([new Error('fetch failed')]);
    expect(await transport.sendMessage('текст')).toBe('unknown');
    expect(calls.length).toBe(1);
    expect(waits).toEqual([]);
    expect(warn.some(([m]) => m === 'telegram_send_unknown')).toBe(true);
  });

  it('не пишет токен в логи', async () => {
    const token = 'leak-check-token';
    const { transport, warn } = mockTransport([new Error(`fetch failed at ${token}`)], token);
    await transport.sendMessage('текст');
    const serialized = JSON.stringify(warn);
    expect(serialized.includes(token)).toBe(false);
    expect(serialized.includes('***')).toBe(true);
  });
});
