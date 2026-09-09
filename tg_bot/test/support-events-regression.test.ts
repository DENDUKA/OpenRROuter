import { describe, expect, it } from 'vitest';
import { EVENT_LIMITS, parseSupportTicketEvent } from '../src/events.js';

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
    expect(
      parseSupportTicketEvent({
        ...validEvent,
        payload: { ...validEvent.payload, subject: 'т'.repeat(EVENT_LIMITS.subjectMax + 1) },
      }).ok,
    ).toBe(false);
    expect(
      parseSupportTicketEvent({
        ...validEvent,
        payload: { ...validEvent.payload, message: 'т'.repeat(EVENT_LIMITS.messageMax + 1) },
      }).ok,
    ).toBe(false);
  });

  it('игнорирует посторонние поля вроде chat_id', () => {
    const result = parseSupportTicketEvent({ ...validEvent, chat_id: '-100999', token: 'nope' });
    expect(result.ok).toBe(true);
    expect(result.ok && (result.event as unknown as Record<string, unknown>)['chat_id']).toBeUndefined();
  });
});
