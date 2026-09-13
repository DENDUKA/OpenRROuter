// Форматирование уведомлений для Telegram:
// 1) Обращение Support: email, тема и краткий текст обращения (сокращение помечается явно).
// 2) Регистрация пользователя: краткое сообщение с email.
// 3) Успешное пополнение: email, сумма RUB и USD, способ оплаты.
// 4) Первый API-запрос: email, модель.
// 5) Создание платежа: email, сумма RUB и USD, способ оплаты.
// 6) Утренняя сводка (daily digest): статистика за 24 часа.
// Отправляется обычным текстом: parse_mode не используется, предпросмотр ссылок отключается в транспорте.

import type {
  BotEvent,
  FirstApiRequestEvent,
  PaymentCreatedEvent,
  SupportTicketEvent,
  TopUpCompletedEvent,
  UserRegisteredEvent,
} from './events.js';

export const TELEGRAM_MESSAGE_LIMIT = 4096;
export const TRUNCATION_NOTE = '… (текст обращения сокращён)';

function truncateBody(message: string, budget: number): string {
  if (message.length <= budget) return message;
  const usable = Math.max(0, budget - TRUNCATION_NOTE.length - 1);
  return message.slice(0, usable) + '\n' + TRUNCATION_NOTE;
}

function formatRub(amount: number): string {
  return amount.toLocaleString('ru-RU', { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

function formatUsd(amount: number): string {
  return amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

export function formatSupportNotification(event: SupportTicketEvent): string {
  const header = `Новое обращение Support\nОт: ${event.payload.email}\nТема: ${event.payload.subject}\n\n`;
  const body = truncateBody(event.payload.message, TELEGRAM_MESSAGE_LIMIT - header.length);
  return header + body;
}

export function formatRegistrationNotification(event: UserRegisteredEvent): string {
  return `🎉 Новая регистрация\nEmail: ${event.payload.email}`;
}

export function formatTopUpNotification(event: TopUpCompletedEvent): string {
  const rub = formatRub(event.payload.amountRub);
  const usd = formatUsd(event.payload.creditedUsd);
  return [
    '💳 Успешное пополнение баланса',
    `Пользователь: ${event.payload.email}`,
    `Сумма: ${rub} ₽ ($${usd})`,
    `Способ: ${event.payload.provider}`,
  ].join('\n');
}

export function formatFirstApiRequestNotification(event: FirstApiRequestEvent): string {
  return [
    '⚡️ Первый API-запрос пользователя',
    `Пользователь: ${event.payload.email}`,
    `Модель: ${event.payload.model}`,
  ].join('\n');
}

export function formatPaymentCreatedNotification(event: PaymentCreatedEvent): string {
  const rub = formatRub(event.payload.amountRub);
  const usd = formatUsd(event.payload.creditedUsd);
  return [
    '🧾 Создан платёж',
    `Пользователь: ${event.payload.email}`,
    `Сумма: ${rub} ₽ ($${usd})`,
    `Способ: ${event.payload.provider}`,
  ].join('\n');
}

export interface DailySummaryData {
  periodHours?: number;
  newRegistrations: number;
  topupsCount: number;
  topupsTotalRub: number;
  topupsTotalUsd: number;
  uniqueUsers: number;
  activeApiUsers?: number;
}

export function formatDailySummaryMessage(summary: DailySummaryData): string {
  const rub = formatRub(summary.topupsTotalRub);
  const usd = formatUsd(summary.topupsTotalUsd);
  return [
    '📊 Утренняя сводка Rawrter',
    'Период: за последние 24 часа',
    '',
    `👥 Новых регистраций: ${summary.newRegistrations}`,
    `💳 Пополнений: ${summary.topupsCount} на сумму ${rub} ₽ ($${usd})`,
    `🌐 Уникальных пользователей: ${summary.uniqueUsers}`,
    `⚡️ Пользовались API: ${summary.activeApiUsers ?? 0}`,
  ].join('\n');
}

export function formatEventNotification(event: BotEvent): string {
  switch (event.type) {
    case 'user_registered':
      return formatRegistrationNotification(event);
    case 'topup_completed':
      return formatTopUpNotification(event);
    case 'first_api_request':
      return formatFirstApiRequestNotification(event);
    case 'payment_created':
      return formatPaymentCreatedNotification(event);
    case 'support_ticket':
    default:
      return formatSupportNotification(event);
  }
}
