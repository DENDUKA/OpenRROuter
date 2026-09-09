import { describe, expect, it } from 'vitest';
import { BoundedEventQueue } from '../src/queue.js';
import type { SupportTicketEvent } from '../src/events.js';

function makeEvent(id: string): SupportTicketEvent {
  return {
    id,
    type: 'support_ticket',
    occurredAt: '2026-09-07T09:00:00Z',
    payload: { email: 'user@example.com', subject: 'Тема', message: 'Текст' },
  };
}

describe('BoundedEventQueue', () => {
  it('принимает событие и отдаёт его воркеру в порядке FIFO', async () => {
    const queue = new BoundedEventQueue(3, 10);
    expect(queue.push(makeEvent('a'), 1)).toBe('accepted');
    expect(queue.push(makeEvent('b'), 2)).toBe('accepted');
    const first = await queue.take();
    const second = await queue.take();
    expect(first?.event.id).toBe('a');
    expect(second?.event.id).toBe('b');
  });

  it('дедуплицирует повторный id', () => {
    const queue = new BoundedEventQueue(3, 10);
    expect(queue.push(makeEvent('a'))).toBe('accepted');
    expect(queue.push(makeEvent('a'))).toBe('duplicate');
    expect(queue.stats.duplicates).toBe(1);
  });

  it('отклоняет при переполнении', () => {
    const queue = new BoundedEventQueue(1, 10);
    expect(queue.push(makeEvent('a'))).toBe('accepted');
    expect(queue.push(makeEvent('b'))).toBe('full');
    expect(queue.stats.overflowed).toBe(1);
    expect(queue.size()).toBe(1);
  });

  it('вытесняет старейший id из памяти дедупликации', () => {
    const queue = new BoundedEventQueue(10, 2);
    queue.push(makeEvent('a'));
    queue.push(makeEvent('b'));
    queue.push(makeEvent('c'));
    expect(queue.push(makeEvent('a'))).toBe('accepted');
  });

  it('take разрешается пришедшим элементом и не дублирует его при следующем take', async () => {
    const queue = new BoundedEventQueue(2, 10);
    const pending = queue.take();
    queue.push(makeEvent('a'));
    const item = await pending;
    expect(item?.event.id).toBe('a');
    expect(queue.size()).toBe(0);

    // Следующий take должен ждать новый элемент, а не возвращать старый
    let resolved = false;
    void queue.take().then(() => {
      resolved = true;
    });
    // Даём микротаске отработать
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(resolved).toBe(false);
  });

  it('stop завершает ожидающий take значением null', async () => {
    const queue = new BoundedEventQueue(2, 10);
    const pending = queue.take();
    queue.stop();
    expect(await pending).toBeNull();
  });

  it('после stop push отклоняется как full', () => {
    const queue = new BoundedEventQueue(2, 10);
    queue.stop();
    expect(queue.push(makeEvent('a'))).toBe('full');
  });
});
