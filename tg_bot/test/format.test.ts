import { describe, expect, it } from 'vitest';
import {
  TELEGRAM_MESSAGE_LIMIT,
  TRUNCATION_NOTE,
  formatDailySummaryMessage,
  formatDailySummaryMessages,
  formatEventNotification,
  formatFirstApiRequestNotification,
  formatPaymentCreatedNotification,
  formatRegistrationNotification,
  formatSupportNotification,
  formatTopUpNotification,
} from '../src/format.js';
import type { SupportTicketEvent } from '../src/events.js';

function makeEvent(overrides: Partial<SupportTicketEvent['payload']>): SupportTicketEvent {
  return {
    id: 'evt-1',
    type: 'support_ticket',
    occurredAt: '2026-09-07T09:00:00Z',
    payload: { email: 'user@example.com', subject: 'Не проходит оплата', message: 'Текст', ...overrides },
  };
}

describe('formatSupportNotification', () => {
  it('формирует сообщение согласованного формата', () => {
    const text = formatSupportNotification(makeEvent({}));
    expect(text).toBe('Новое обращение Support\nОт: user@example.com\nТема: Не проходит оплата\n\nТекст');
  });

  it('сокращает длинный текст с явной отметкой', () => {
    const text = formatSupportNotification(makeEvent({ message: 'д'.repeat(5000) }));
    expect(text.length).toBeLessThanOrEqual(TELEGRAM_MESSAGE_LIMIT);
    expect(text.endsWith(TRUNCATION_NOTE)).toBe(true);
    expect(text).toContain('…');
  });
});

describe('formatRegistrationNotification', () => {
  it('формирует сообщение о регистрации пользователя', () => {
    const text = formatRegistrationNotification({
      id: 'reg-1',
      type: 'user_registered',
      occurredAt: '2026-09-07T09:00:00Z',
      payload: { email: 'user@example.com' },
    });
    expect(text).toBe('🎉 Новая регистрация\nEmail: user@example.com');
  });

  it('формирует предупреждение о дубле с балансами связанных аккаунтов', () => {
    const text = formatRegistrationNotification({
      id: 'reg-2',
      type: 'user_registered',
      occurredAt: '2026-09-07T09:00:00Z',
      payload: {
        email: 'user2@example.com',
        isDuplicate: true,
        duplicates: [
          { email: 'user1@example.com', balanceUsd: 0.15 },
          { email: 'user0@example.com', balanceUsd: 0 },
        ],
      },
    });
    expect(text).toContain('🎉 Новая регистрация');
    expect(text).toContain('Email: user2@example.com');
    expect(text).toContain('⚠️ Подозрение на дубль (совпадение по Яндекс.Метрике)');
    expect(text).toContain('• user1@example.com — баланс: $0.15');
    expect(text).toContain('• user0@example.com — баланс: $0.00');
  });
});

describe('formatTopUpNotification', () => {
  it('формирует сообщение об успешном пополнении', () => {
    const text = formatTopUpNotification({
      id: 'top-1',
      type: 'topup_completed',
      occurredAt: '2026-09-08T10:00:00Z',
      payload: {
        email: 'payer@example.com',
        amountRub: 1500,
        creditedUsd: 16.25,
        provider: 'Rollypay (СБП)',
      },
    });
    expect(text).toContain('💳 Успешное пополнение баланса');
    expect(text).toContain('Пользователь: payer@example.com');
    expect(text).toContain('16.25');
  });
});

describe('formatFirstApiRequestNotification', () => {
  it('формирует сообщение о первом запросе', () => {
    const text = formatFirstApiRequestNotification({
      id: 'req-1',
      type: 'first_api_request',
      occurredAt: '2026-09-08T10:00:00Z',
      payload: {
        email: 'user@example.com',
        model: 'anthropic/claude-3-5-sonnet',
      },
    });
    expect(text).toContain('⚡️ Первый API-запрос пользователя');
    expect(text).toContain('Пользователь: user@example.com');
    expect(text).toContain('Модель: anthropic/claude-3-5-sonnet');
  });
});

describe('formatPaymentCreatedNotification', () => {
  it('формирует сообщение о создании платежа', () => {
    const text = formatPaymentCreatedNotification({
      id: 'pay-1',
      type: 'payment_created',
      occurredAt: '2026-09-08T10:00:00Z',
      payload: {
        email: 'payer@example.com',
        amountRub: 1500,
        creditedUsd: 16.25,
        provider: 'Rollypay (СБП)',
      },
    });
    expect(text).toContain('🧾 Создан платёж');
    expect(text).toContain('Пользователь: payer@example.com');
    expect(text).toContain('16.25');

    const dispatched = formatEventNotification({
      id: 'pay-1',
      type: 'payment_created',
      occurredAt: '2026-09-08T10:00:00Z',
      payload: {
        email: 'payer@example.com',
        amountRub: 1500,
        creditedUsd: 16.25,
        provider: 'Rollypay (СБП)',
      },
    });
    expect(dispatched).toBe(text);
  });
});

describe('formatDailySummaryMessage', () => {
  it('формирует утреннюю сводку', () => {
    const text = formatDailySummaryMessage({
      newRegistrations: 10,
      topupsCount: 3,
      topupsTotalRub: 4500,
      topupsTotalUsd: 48.5,
      uniqueUsers: 72,
      activeApiUsers: 15,
    });
    expect(text).toContain('📊 Утренняя сводка Rawrter');
    expect(text).toContain('👥 Новых регистраций: 10');
    expect(text).toContain('💳 Пополнений: 3');
    expect(text).toContain('🌐 Уникальных пользователей: 72');
    expect(text).toContain('⚡️ Пользовались API: 15');
  });

  it('подставляет 0 если activeApiUsers не передан', () => {
    const text = formatDailySummaryMessage({
      newRegistrations: 1,
      topupsCount: 0,
      topupsTotalRub: 0,
      topupsTotalUsd: 0,
      uniqueUsers: 5,
    });
    expect(text).toContain('⚡️ Пользовались API: 0');
  });

  it('включает список клиентов, пометку об оплате и их модели в сводку', () => {
    const text = formatDailySummaryMessage({
      newRegistrations: 2,
      registeredUsers: [
        { email: 'new1@example.com', balanceUsd: 10, hasPaidTopup: true },
        { email: 'new2@example.com', balanceUsd: 1, hasPaidTopup: false },
      ],
      topupsCount: 1,
      topupsTotalRub: 1000,
      topupsTotalUsd: 11.2,
      uniqueUsers: 10,
      activeApiUsers: 2,
      clientsUsage: [
        {
          email: 'payer@example.com',
          totalTokens: 45200,
          totalCostUsd: 0.15,
          hasPaidTopup: true,
          balanceUsd: 12.5,
          models: [
            { modelId: 'gpt-4o', tokens: 35000, costUsd: 0.12 },
            { modelId: 'claude-3-5-sonnet', tokens: 10200, costUsd: 0.03 },
          ],
        },
        {
          email: 'free@example.com',
          totalTokens: 1000,
          totalCostUsd: 0.005,
          hasPaidTopup: false,
          balanceUsd: 0.99,
          models: [
            { modelId: 'gpt-4o-mini', tokens: 1000, costUsd: 0.005 },
          ],
        },
      ],
    });
    expect(text).toContain('👥 Новых регистраций: 2');
    expect(text).toContain('  • new1@example.com (пополнил, баланс: $10.00)');
    expect(text).toContain('  • new2@example.com (не пополнял, баланс: $1.00)');
    expect(text).toContain('Клиенты API:');
    expect(text).toContain('• payer@example.com (купленные $, баланс: $12.50) — $0.15 (45 200 токенов)');
    expect(text).toContain('  └ gpt-4o: 35 000 токенов ($0.12)');
    expect(text).toContain('  └ claude-3-5-sonnet: 10 200 токенов ($0.03)');
    expect(text).toContain('• free@example.com (бесплатные $, баланс: $0.99) — $0.005 (1 000 токенов)');
    expect(text).toContain('  └ gpt-4o-mini: 1 000 токенов ($0.005)');
  });

  it('разбивает длинный список клиентов на несколько сообщений (пагинация)', () => {
    const manyClients = Array.from({ length: 80 }, (_, i) => ({
      email: `client${i}@very-long-domain-name-example-test.org`,
      totalTokens: 100000,
      totalCostUsd: 1.5,
      hasPaidTopup: i % 2 === 0,
      models: [
        { modelId: 'openai/gpt-4o-latest-2026', tokens: 50000, costUsd: 1.0 },
        { modelId: 'anthropic/claude-3-5-sonnet', tokens: 50000, costUsd: 0.5 },
      ],
    }));

    const messages = formatDailySummaryMessages({
      newRegistrations: 5,
      topupsCount: 10,
      topupsTotalRub: 15000,
      topupsTotalUsd: 160,
      uniqueUsers: 200,
      activeApiUsers: 80,
      clientsUsage: manyClients,
    });

    expect(messages.length).toBeGreaterThan(1);
    for (const msg of messages) {
      expect(msg.length).toBeLessThanOrEqual(4096);
    }
    expect(messages[0]).toContain(`Клиенты API (часть 1/${messages.length}):`);
    expect(messages[1]).toContain(`Клиенты API (часть 2/${messages.length}):`);
  });
});
