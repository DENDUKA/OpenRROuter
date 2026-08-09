---
description: Взять свободную задачу из .opencode/tasks.md и выполнить её в отдельном агенте (task-worker).
---

# Запуск задачи в отдельном агенте

`$1` (опционально) — ID задачи (`<PREFIX>-NNN`). Пусто → первая `status: todo` по порядку.

Задачи берутся из `.opencode/tasks.md` (активный бэклог). Выполненные там не лежат — они
переехали в `.opencode/tasks-done.md`, и оттуда задачи в работу не берутся.

## Действие

Запусти отдельный агент через **task tool**:

- `subagent_type`: `task-worker`
- `prompt`: передай селектор задачи:
  > Селектор задачи: `$1`. Пусто = первая свободная `status: todo` в `.opencode/tasks.md`.
  > Выполни полный цикл по своему промпту: выбери → захвати (status=in_progress) → реализуй по
  > правилам репо → собери (`dotnet build ... -nr:false -m:1`) → заверши: `done` = перенести блок
  > из `.opencode/tasks.md` в конец `.opencode/tasks-done.md`, `blocked` = оставить в `tasks.md`
  > с `blocked_reason`. Верни observation-отчёт.

task-worker работает автономно и сам владеет циклом (чтение `tasks.md`, мутация статуса, правки,
сборка, перенос выполненной задачи в архив). Не дублируй его шаги здесь — только запуск и приём
результата.

## После возврата

- Если task-worker вернул `status: done` — кратко перескажи его summary + artifacts пользователю.
- Если `blocked` — покажи причину и предложи next_actions (починить вручную, переоткрыть задачу).
- Файлы задач уже обновлены агентом: `done` вырезан из `.opencode/tasks.md` и дописан
  в `.opencode/tasks-done.md`; `blocked`/`in_progress` остались в `tasks.md`.
- Быстрая проверка целостности — ID задачи встречается ровно в одном файле:
  ```powershell
  Select-String -Path .opencode/tasks.md, .opencode/tasks-done.md -Pattern '^## <ID> —'
  ```

## Error recovery

- **root cause**: `task tool` падает, если `subagent_type` не существует или `tasks.md` пуст.
- **safe retry**: убедись что `.opencode/agent/task-worker.md` есть и в `tasks.md` есть хоть одна
  `status: todo` задача (иначе добавь через `/add-task`).
- **stop condition**: нет свободных задач → не запускай агент, сообщи пользователю.
- **уже выполнена**: указанный ID нашёлся в `.opencode/tasks-done.md` — не перезапускай задачу
  и не возвращай её в бэклог; нужна доработка → заведи новую через `/add-task`.
