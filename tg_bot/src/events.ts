// Контракт входящих событий и защитная валидация.
// Источник обязан передавать уже проверенные поля; бот не доверяет входу.

export const EVENT_LIMITS = {
  idMax: 64,
  occurredAtMax: 40,
  emailMax: 254,
  subjectMax: 200,
  messageMax: 5000,
  modelMax: 128,
  providerMax: 64,
} as const;

export interface SupportTicketPayload {
  email: string;
  subject: string;
  message: string;
}

export interface SupportTicketEvent {
  id: string;
  type: 'support_ticket';
  occurredAt: string;
  payload: SupportTicketPayload;
}

export interface UserRegisteredPayload {
  email: string;
}

export interface UserRegisteredEvent {
  id: string;
  type: 'user_registered';
  occurredAt: string;
  payload: UserRegisteredPayload;
}

export interface TopUpCompletedPayload {
  email: string;
  amountRub: number;
  creditedUsd: number;
  provider: string;
}

export interface TopUpCompletedEvent {
  id: string;
  type: 'topup_completed';
  occurredAt: string;
  payload: TopUpCompletedPayload;
}

export interface FirstApiRequestPayload {
  email: string;
  model: string;
}

export interface FirstApiRequestEvent {
  id: string;
  type: 'first_api_request';
  occurredAt: string;
  payload: FirstApiRequestPayload;
}

export type BotEvent =
  | SupportTicketEvent
  | UserRegisteredEvent
  | TopUpCompletedEvent
  | FirstApiRequestEvent;

export interface FieldError {
  field: string;
  reason: string;
}

export type ParseResult<T> =
  | { ok: true; event: T }
  | { ok: false; errors: FieldError[] };

function readString(value: unknown): string | null {
  return typeof value === 'string' ? value.trim() : null;
}

function readNumber(value: unknown): number | null {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  if (typeof value === 'string') {
    const parsed = Number.parseFloat(value);
    if (Number.isFinite(parsed)) return parsed;
  }
  return null;
}

export function parseSupportTicketEvent(input: unknown): ParseResult<SupportTicketEvent> {
  if (typeof input !== 'object' || input === null || Array.isArray(input)) {
    return { ok: false, errors: [{ field: 'body', reason: 'ожидается объект события' }] };
  }
  const obj = input as Record<string, unknown>;
  const errors: FieldError[] = [];

  const id = readString(obj.id);
  if (id === null || id.length === 0) errors.push({ field: 'id', reason: 'обязательная строка' });
  else if (id.length > EVENT_LIMITS.idMax) errors.push({ field: 'id', reason: `длина больше ${EVENT_LIMITS.idMax}` });

  if (obj.type !== 'support_ticket') errors.push({ field: 'type', reason: "допустимо только 'support_ticket'" });

  const occurredAt = readString(obj.occurredAt);
  if (occurredAt === null || occurredAt.length === 0) {
    errors.push({ field: 'occurredAt', reason: 'обязательная строка' });
  } else if (occurredAt.length > EVENT_LIMITS.occurredAtMax || Number.isNaN(Date.parse(occurredAt))) {
    errors.push({ field: 'occurredAt', reason: 'ожидается корректная дата в ISO 8601' });
  }

  if (typeof obj.payload !== 'object' || obj.payload === null || Array.isArray(obj.payload)) {
    errors.push({ field: 'payload', reason: 'ожидается объект payload' });
  } else {
    const payloadObj = obj.payload as Record<string, unknown>;
    const email = readString(payloadObj.email) ?? '';
    const subject = readString(payloadObj.subject) ?? '';
    const message = readString(payloadObj.message) ?? '';

    if (email.length === 0) errors.push({ field: 'payload.email', reason: 'обязательная строка' });
    else if (email.length > EVENT_LIMITS.emailMax || !email.includes('@') || /\s/.test(email)) {
      errors.push({ field: 'payload.email', reason: 'некорректный адрес' });
    }
    if (subject.length === 0) errors.push({ field: 'payload.subject', reason: 'обязательная строка' });
    else if (subject.length > EVENT_LIMITS.subjectMax) {
      errors.push({ field: 'payload.subject', reason: `длина больше ${EVENT_LIMITS.subjectMax}` });
    }
    if (message.length === 0) errors.push({ field: 'payload.message', reason: 'обязательная строка' });
    else if (message.length > EVENT_LIMITS.messageMax) {
      errors.push({ field: 'payload.message', reason: `длина больше ${EVENT_LIMITS.messageMax}` });
    }

    if (errors.length === 0 && id !== null && occurredAt !== null) {
      return { ok: true, event: { id, type: 'support_ticket', occurredAt, payload: { email, subject, message } } };
    }
  }

  return { ok: false, errors };
}

export function parseUserRegisteredEvent(input: unknown): ParseResult<UserRegisteredEvent> {
  if (typeof input !== 'object' || input === null || Array.isArray(input)) {
    return { ok: false, errors: [{ field: 'body', reason: 'ожидается объект события' }] };
  }
  const obj = input as Record<string, unknown>;
  const errors: FieldError[] = [];

  const id = readString(obj.id);
  if (id === null || id.length === 0) errors.push({ field: 'id', reason: 'обязательная строка' });
  else if (id.length > EVENT_LIMITS.idMax) errors.push({ field: 'id', reason: `длина больше ${EVENT_LIMITS.idMax}` });

  if (obj.type !== 'user_registered') errors.push({ field: 'type', reason: "допустимо только 'user_registered'" });

  const occurredAt = readString(obj.occurredAt);
  if (occurredAt === null || occurredAt.length === 0) {
    errors.push({ field: 'occurredAt', reason: 'обязательная строка' });
  } else if (occurredAt.length > EVENT_LIMITS.occurredAtMax || Number.isNaN(Date.parse(occurredAt))) {
    errors.push({ field: 'occurredAt', reason: 'ожидается корректная дата в ISO 8601' });
  }

  if (typeof obj.payload !== 'object' || obj.payload === null || Array.isArray(obj.payload)) {
    errors.push({ field: 'payload', reason: 'ожидается объект payload' });
  } else {
    const payloadObj = obj.payload as Record<string, unknown>;
    const email = readString(payloadObj.email) ?? '';
    if (email.length === 0) errors.push({ field: 'payload.email', reason: 'обязательная строка' });
    else if (email.length > EVENT_LIMITS.emailMax || !email.includes('@') || /\s/.test(email)) {
      errors.push({ field: 'payload.email', reason: 'некорректный адрес' });
    }
    if (errors.length === 0 && id !== null && occurredAt !== null) {
      return { ok: true, event: { id, type: 'user_registered', occurredAt, payload: { email } } };
    }
  }

  return { ok: false, errors };
}

export function parseTopUpCompletedEvent(input: unknown): ParseResult<TopUpCompletedEvent> {
  if (typeof input !== 'object' || input === null || Array.isArray(input)) {
    return { ok: false, errors: [{ field: 'body', reason: 'ожидается объект события' }] };
  }
  const obj = input as Record<string, unknown>;
  const errors: FieldError[] = [];

  const id = readString(obj.id);
  if (id === null || id.length === 0) errors.push({ field: 'id', reason: 'обязательная строка' });
  else if (id.length > EVENT_LIMITS.idMax) errors.push({ field: 'id', reason: `длина больше ${EVENT_LIMITS.idMax}` });

  if (obj.type !== 'topup_completed') errors.push({ field: 'type', reason: "допустимо только 'topup_completed'" });

  const occurredAt = readString(obj.occurredAt);
  if (occurredAt === null || occurredAt.length === 0) {
    errors.push({ field: 'occurredAt', reason: 'обязательная строка' });
  } else if (occurredAt.length > EVENT_LIMITS.occurredAtMax || Number.isNaN(Date.parse(occurredAt))) {
    errors.push({ field: 'occurredAt', reason: 'ожидается корректная дата в ISO 8601' });
  }

  if (typeof obj.payload !== 'object' || obj.payload === null || Array.isArray(obj.payload)) {
    errors.push({ field: 'payload', reason: 'ожидается объект payload' });
  } else {
    const payloadObj = obj.payload as Record<string, unknown>;
    const email = readString(payloadObj.email) ?? '';
    const amountRub = readNumber(payloadObj.amountRub);
    const creditedUsd = readNumber(payloadObj.creditedUsd);
    const provider = readString(payloadObj.provider) ?? 'СБП';

    if (email.length === 0) errors.push({ field: 'payload.email', reason: 'обязательная строка' });
    else if (email.length > EVENT_LIMITS.emailMax || !email.includes('@') || /\s/.test(email)) {
      errors.push({ field: 'payload.email', reason: 'некорректный адрес' });
    }

    if (amountRub === null || amountRub <= 0) {
      errors.push({ field: 'payload.amountRub', reason: 'ожидается положительное число' });
    }
    if (creditedUsd === null || creditedUsd <= 0) {
      errors.push({ field: 'payload.creditedUsd', reason: 'ожидается положительное число' });
    }
    if (provider.length > EVENT_LIMITS.providerMax) {
      errors.push({ field: 'payload.provider', reason: `длина больше ${EVENT_LIMITS.providerMax}` });
    }

    if (errors.length === 0 && id !== null && occurredAt !== null && amountRub !== null && creditedUsd !== null) {
      return {
        ok: true,
        event: {
          id,
          type: 'topup_completed',
          occurredAt,
          payload: { email, amountRub, creditedUsd, provider },
        },
      };
    }
  }

  return { ok: false, errors };
}

export function parseFirstApiRequestEvent(input: unknown): ParseResult<FirstApiRequestEvent> {
  if (typeof input !== 'object' || input === null || Array.isArray(input)) {
    return { ok: false, errors: [{ field: 'body', reason: 'ожидается объект события' }] };
  }
  const obj = input as Record<string, unknown>;
  const errors: FieldError[] = [];

  const id = readString(obj.id);
  if (id === null || id.length === 0) errors.push({ field: 'id', reason: 'обязательная строка' });
  else if (id.length > EVENT_LIMITS.idMax) errors.push({ field: 'id', reason: `длина больше ${EVENT_LIMITS.idMax}` });

  if (obj.type !== 'first_api_request') errors.push({ field: 'type', reason: "допустимо только 'first_api_request'" });

  const occurredAt = readString(obj.occurredAt);
  if (occurredAt === null || occurredAt.length === 0) {
    errors.push({ field: 'occurredAt', reason: 'обязательная строка' });
  } else if (occurredAt.length > EVENT_LIMITS.occurredAtMax || Number.isNaN(Date.parse(occurredAt))) {
    errors.push({ field: 'occurredAt', reason: 'ожидается корректная дата в ISO 8601' });
  }

  if (typeof obj.payload !== 'object' || obj.payload === null || Array.isArray(obj.payload)) {
    errors.push({ field: 'payload', reason: 'ожидается объект payload' });
  } else {
    const payloadObj = obj.payload as Record<string, unknown>;
    const email = readString(payloadObj.email) ?? '';
    const model = readString(payloadObj.model) ?? '';

    if (email.length === 0) errors.push({ field: 'payload.email', reason: 'обязательная строка' });
    else if (email.length > EVENT_LIMITS.emailMax || !email.includes('@') || /\s/.test(email)) {
      errors.push({ field: 'payload.email', reason: 'некорректный адрес' });
    }

    if (model.length === 0) errors.push({ field: 'payload.model', reason: 'обязательная строка' });
    else if (model.length > EVENT_LIMITS.modelMax) {
      errors.push({ field: 'payload.model', reason: `длина больше ${EVENT_LIMITS.modelMax}` });
    }

    if (errors.length === 0 && id !== null && occurredAt !== null) {
      return {
        ok: true,
        event: {
          id,
          type: 'first_api_request',
          occurredAt,
          payload: { email, model },
        },
      };
    }
  }

  return { ok: false, errors };
}
