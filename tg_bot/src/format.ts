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
  const lines = [
    '🎉 Новая регистрация',
    `Email: ${event.payload.email}`,
  ];

  if (event.payload.isDuplicate || (event.payload.duplicates && event.payload.duplicates.length > 0)) {
    lines.push('');
    lines.push('⚠️ Подозрение на дубль (совпадение по Яндекс.Метрике)');
    if (event.payload.duplicates && event.payload.duplicates.length > 0) {
      lines.push('Связанные аккаунты:');
      for (const dup of event.payload.duplicates) {
        lines.push(`• ${dup.email} — баланс: $${formatUsd(dup.balanceUsd)}`);
      }
    }
  }

  return lines.join('\n');
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

export interface ModelUsageSummary {
  modelId: string;
  tokens: number;
  costUsd: number;
}

export interface ClientUsageSummary {
  email: string;
  totalTokens: number;
  totalCostUsd: number;
  models: ModelUsageSummary[];
}

export interface DailySummaryData {
  periodHours?: number;
  newRegistrations: number;
  topupsCount: number;
  topupsTotalRub: number;
  topupsTotalUsd: number;
  uniqueUsers: number;
  activeApiUsers?: number;
  clientsUsage?: ClientUsageSummary[];
}

function formatTokens(count: number): string {
  return count.toLocaleString('ru-RU').replace(/[\u00A0\u202F]/g, ' ');
}

function formatCostUsd(amount: number): string {
  if (amount === 0) return '0.00';
  if (amount < 0.0001) return '<0.0001';
  return amount.toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: amount < 0.01 ? 4 : 2,
  });
}

function pluralClients(n: number): string {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod100 >= 11 && mod100 <= 19) return 'клиентов';
  if (mod10 === 1) return 'клиент';
  if (mod10 >= 2 && mod10 <= 4) return 'клиента';
  return 'клиентов';
}

export function formatDailySummaryMessage(summary: DailySummaryData): string {
  const rub = formatRub(summary.topupsTotalRub);
  const usd = formatUsd(summary.topupsTotalUsd);
  const lines = [
    '📊 Утренняя сводка Rawrter',
    'Период: за последние 24 часа',
    '',
    `👥 Новых регистраций: ${summary.newRegistrations}`,
    `💳 Пополнений: ${summary.topupsCount} на сумму ${rub} ₽ ($${usd})`,
    `🌐 Уникальных пользователей: ${summary.uniqueUsers}`,
    `⚡️ Пользовались API: ${summary.activeApiUsers ?? 0}`,
  ];

  if (summary.clientsUsage && summary.clientsUsage.length > 0) {
    lines.push('');
    lines.push('Клиенты API:');
    let currentLength = lines.join('\n').length;
    const clients = summary.clientsUsage;

    for (let i = 0; i < clients.length; i++) {
      const client = clients[i]!;
      const clientLines: string[] = [
        `• ${client.email} — $${formatCostUsd(client.totalCostUsd)} (${formatTokens(client.totalTokens)} токенов)`,
      ];
      for (const m of client.models) {
        clientLines.push(`  └ ${m.modelId}: ${formatTokens(m.tokens)} токенов ($${formatCostUsd(m.costUsd)})`);
      }
      const clientBlock = clientLines.join('\n');

      const remainingClients = clients.length - i;
      const notice = `… и ещё ${remainingClients} ${pluralClients(remainingClients)}`;
      const noticeReserve = remainingClients > 1 ? `\n${notice}`.length : 0;

      if (currentLength + 1 + clientBlock.length + noticeReserve > TELEGRAM_MESSAGE_LIMIT) {
        if (currentLength + 1 + notice.length <= TELEGRAM_MESSAGE_LIMIT) {
          lines.push(notice);
        }
        break;
      }

      lines.push(...clientLines);
      currentLength += 1 + clientBlock.length;
    }
  }

  return lines.join('\n');
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
