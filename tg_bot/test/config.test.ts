import { describe, expect, it } from 'vitest';
import { loadConfig } from '../src/config.js';

const baseEnv = { TELEGRAM_BOT_TOKEN: 'synthetic-token', TELEGRAM_CHAT_ID: '123', INBOUND_AUTH_TOKEN: 'inbound-secret' };

describe('loadConfig', () => {
  it('заполняет значения по умолчанию', () => {
    const { config, errors } = loadConfig({ ...baseEnv });
    expect(errors).toEqual([]);
    expect(config.port).toBe(8080);
    expect(config.supportNotifyEnabled).toBe(true);
    expect(config.heartbeatEnabled).toBe(true);
    expect(config.queueMaxItems).toBe(100);
    expect(config.dedupMemory).toBe(1000);
  });

  it('сообщает об отсутствующих секретах только именами переменных', () => {
    const { config, errors } = loadConfig({});
    expect(errors).toContain('TELEGRAM_BOT_TOKEN не задан');
    expect(errors).toContain('TELEGRAM_CHAT_ID не задан');
    expect(errors).toContain('INBOUND_AUTH_TOKEN не задан');
    expect(config.telegramBotToken).toBe('');
  });

  it('читает флаги отключения и параметры', () => {
    const { config } = loadConfig({
      ...baseEnv,
      SUPPORT_NOTIFY_ENABLED: 'false',
      HEARTBEAT_ENABLED: '0',
      PORT: '9090',
      QUEUE_MAX_ITEMS: '5',
      DEDUP_MEMORY: '10',
    });
    expect(config.supportNotifyEnabled).toBe(false);
    expect(config.heartbeatEnabled).toBe(false);
    expect(config.port).toBe(9090);
    expect(config.queueMaxItems).toBe(5);
    expect(config.dedupMemory).toBe(10);
  });

  it('некорректный порт откатывается к значению по умолчанию', () => {
    const { config } = loadConfig({ ...baseEnv, PORT: 'not-a-port' });
    expect(config.port).toBe(8080);
  });
});
