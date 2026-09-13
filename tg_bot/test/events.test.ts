import { describe, expect, it } from 'vitest';
import {
  EVENT_LIMITS,
  parseFirstApiRequestEvent,
  parsePaymentCreatedEvent,
  parseSupportTicketEvent,
  parseTopUpCompletedEvent,
  parseUserRegisteredEvent,
} from '../src/events.js';

const validEvent = {
  id: 'evt-1',
  type: 'support_ticket',
  occurredAt: '2026-09-07T09:00:00Z',
  payload: { email: 'user@example.com', subject: 'Тема', message: 'Текст' },
};

describe('parseSupportTicketEvent', () => {
  it('принимает синтетическое валидное событие', () => {
    const result = parseSupportTicketEvent(validEvent);
    expect(result.ok).toBe(true);
  });

  it('обрезает пробелы в полях', () => {
    const result = parseSupportTicketEvent({
      ...validEvent,
      payload: { email: ' user@example.com ', subject: ' Тема ', message: ' Текст ' },
    });
    expect(result.ok && result.event.payload.email).toBe('user@example.com');
  });

  it('отклоняет не-объект', () => {
    for (const input of ['x', 1, null, [validEvent]]) {
      expect(parseSupportTicketEvent(input).ok).toBe(false);
    }
  });

  it('отклоняет чужой тип события', () => {
    const result = parseSupportTicketEvent({ ...validEvent, type: 'ad_hoc_message' });
    expect(result.ok).toBe(false);
    expect(result.ok === false && result.errors.some((e) => e.field === 'type')).toBe(true);
  });

  it('отклоняет пустой и слишком длинный id', () => {
    expect(parseSupportTicketEvent({ ...validEvent, id: '' }).ok).toBe(false);
    expect(parseSupportTicketEvent({ ...validEvent, id: 'x'.repeat(EVENT_LIMITS.idMax + 1) }).ok).toBe(false);
  });

  it('отклоняет некорректную дату', () => {
    expect(parseSupportTicketEvent({ ...validEvent, occurredAt: 'не-дата' }).ok).toBe(false);
    expect(parseSupportTicketEvent({ ...validEvent, occurredAt: '' }).ok).toBe(false);
  });

  it('отклоняет некорректный payload', () => {
    expect(parseSupportTicketEvent({ ...validEvent, payload: null }).ok).toBe(false);
    expect(
      parseSupportTicketEvent({ ...validEvent, payload: { email: 'user@example.com', subject: 'Тема' } }).ok,
    ).toBe(false);
    expect(parseSupportTicketEvent({ ...validEvent, payload: { ...validEvent.payload, email: 'без-собаки' } }).ok).toBe(
      false,
    );
    expect(
      parseSupportTicketEvent({ ...validEvent, payload: { ...validEvent.payload, email: 'user @example.com' } }).ok,
    ).toBe(false);
    expect(
      parseSupportTicketEvent({ ...validEvent, payload: { ...validEvent.payload, subject: '' } }).ok,
    ).toBe(false);
  });

  it('игнорирует посторонние поля вроде chat_id', () => {
    const result = parseSupportTicketEvent({ ...validEvent, chat_id: '-100999', token: 'nope' });
    expect(result.ok).toBe(true);
    expect(result.ok && (result.event as unknown as Record<string, unknown>)['chat_id']).toBeUndefined();
  });
});

describe('parseUserRegisteredEvent', () => {
  const validReg = {
    id: 'reg-1',
    type: 'user_registered',
    occurredAt: '2026-09-07T10:00:00Z',
    payload: { email: 'newuser@example.com' },
  };

  it('принимает корректное событие регистрации', () => {
    const res = parseUserRegisteredEvent(validReg);
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.event.type).toBe('user_registered');
      expect(res.event.payload.email).toBe('newuser@example.com');
    }
  });

  it('отклоняет некорректный email', () => {
    expect(parseUserRegisteredEvent({ ...validReg, payload: { email: '' } }).ok).toBe(false);
    expect(parseUserRegisteredEvent({ ...validReg, payload: { email: 'invalid' } }).ok).toBe(false);
    expect(parseUserRegisteredEvent({ ...validReg, payload: null }).ok).toBe(false);
  });
});

describe('parseTopUpCompletedEvent', () => {
  const validTopup = {
    id: 'top-1',
    type: 'topup_completed',
    occurredAt: '2026-09-08T10:00:00Z',
    payload: {
      email: 'payer@example.com',
      amountRub: 1500,
      creditedUsd: 16.25,
      provider: 'Rollypay (СБП)',
    },
  };

  it('принимает корректное событие пополнения', () => {
    const res = parseTopUpCompletedEvent(validTopup);
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.event.payload.amountRub).toBe(1500);
      expect(res.event.payload.creditedUsd).toBe(16.25);
      expect(res.event.payload.provider).toBe('Rollypay (СБП)');
    }
  });

  it('отклоняет нулевую или отрицательную сумму', () => {
    expect(parseTopUpCompletedEvent({ ...validTopup, payload: { ...validTopup.payload, amountRub: 0 } }).ok).toBe(false);
    expect(parseTopUpCompletedEvent({ ...validTopup, payload: { ...validTopup.payload, creditedUsd: -1 } }).ok).toBe(false);
  });
});

describe('parseFirstApiRequestEvent', () => {
  const validReq = {
    id: 'first-1',
    type: 'first_api_request',
    occurredAt: '2026-09-08T10:00:00Z',
    payload: {
      email: 'apiuser@example.com',
      model: 'anthropic/claude-3-5-sonnet',
    },
  };

  it('принимает корректное событие первого запроса', () => {
    const res = parseFirstApiRequestEvent(validReq);
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.event.payload.email).toBe('apiuser@example.com');
      expect(res.event.payload.model).toBe('anthropic/claude-3-5-sonnet');
    }
  });

  it('отклоняет событие без модели', () => {
    expect(parseFirstApiRequestEvent({ ...validReq, payload: { ...validReq.payload, model: '' } }).ok).toBe(false);
  });
});

describe('parsePaymentCreatedEvent', () => {
  const validPayment = {
    id: 'pay-1',
    type: 'payment_created',
    occurredAt: '2026-09-08T10:00:00Z',
    payload: {
      email: 'payer@example.com',
      amountRub: 1500,
      creditedUsd: 16.25,
      provider: 'Rollypay (СБП)',
    },
  };

  it('принимает корректное событие создания платежа', () => {
    const res = parsePaymentCreatedEvent(validPayment);
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.event.type).toBe('payment_created');
      expect(res.event.payload.amountRub).toBe(1500);
      expect(res.event.payload.creditedUsd).toBe(16.25);
      expect(res.event.payload.provider).toBe('Rollypay (СБП)');
    }
  });

  it('отклоняет нулевую или отрицательную сумму', () => {
    expect(parsePaymentCreatedEvent({ ...validPayment, payload: { ...validPayment.payload, amountRub: 0 } }).ok).toBe(false);
    expect(parsePaymentCreatedEvent({ ...validPayment, payload: { ...validPayment.payload, creditedUsd: -1 } }).ok).toBe(false);
  });

  it('отклоняет некорректный email', () => {
    expect(parsePaymentCreatedEvent({ ...validPayment, payload: { ...validPayment.payload, email: 'bad' } }).ok).toBe(false);
  });
});
