import { describe, expect, it } from 'vitest';
import {
  TELEGRAM_MESSAGE_LIMIT,
  TRUNCATION_NOTE,
  formatDailySummaryMessage,
  formatEventNotification,
  formatFirstApiRequestNotification,
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

describe('formatDailySummaryMessage', () => {
  it('формирует утреннюю сводку', () => {
    const text = formatDailySummaryMessage({
      newRegistrations: 10,
      topupsCount: 3,
      topupsTotalRub: 4500,
      topupsTotalUsd: 48.5,
      uniqueUsers: 72,
    });
    expect(text).toContain('📊 Утренняя сводка Rawrter');
    expect(text).toContain('👥 Новых регистраций: 10');
    expect(text).toContain('💳 Пополнений: 3');
    expect(text).toContain('🌐 Уникальных пользователей: 72');
  });
});
