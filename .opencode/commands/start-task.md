---
description: Взять свободную задачу из .opencode/tasks.md и выполнить её в отдельном агенте (task-worker).
---

# Запуск задачи в отдельном агенте

`$1` (опционально) — ID задачи (`T-NNN`). Пусто → первая `status: todo` по порядку.

## Действие

Запусти отдельный агент через **task tool**:

- `subagent_type`: `task-worker`
- `prompt`: передай селектор задачи:
  > Селектор задачи: `$1`. Пусто = первая свободная `status: todo` в `.opencode/tasks.md`.
  > Выполни полный цикл по своему промпту: выбери → захвати (status=in_progress) → реализуй по
  > правилам репо → собери (`dotnet build ... -nr:false -m:1`) → пометь done/blocked → верни
  > observation-отчёт.

task-worker работает автономно и сам владеет циклом (чтение `tasks.md`, мутация статуса, правки,
сборка). Не дублируй его шаги здесь — только запуск и приём результата.

## После возврата

- Если task-worker вернул `status: done` — кратко перескажи его summary + artifacts пользователю.
- Если `blocked` — покажи причину и предложи next_actions (починить вручную, переоткрыть задачу).
- Файл `.opencode/tasks.md` уже обновлён агентом (статус, `started`, `completed`).

## Error recovery

- **root cause**: `task tool` падает, если `subagent_type` не существует или `tasks.md` пуст.
- **safe retry**: убедись что `.opencode/agent/task-worker.md` есть и в `tasks.md` есть хоть одна
  `status: todo` задача (иначе добавь через `/add-task`).
- **stop condition**: нет свободных задач → не запускай агент, сообщи пользователю.
