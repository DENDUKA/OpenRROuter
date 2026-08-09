---
description: Добавить новую задачу в .opencode/tasks.md со следующим ID и status=todo.
---

# Добавить задачу

`$ARGUMENTS` — заголовок (и, через перенос/дополнительно, описание). Если пусто — **стоп**,
спроси заголовок.

## Файлы

- `.opencode/tasks.md` — **активный бэклог**, новая задача всегда пишется сюда.
- `.opencode/tasks-done.md` — архив выполненных. **Ничего сюда не добавляй** — это делает
  только `task-worker` в момент завершения. Читаем его лишь ради нумерации.

## 1. Префикс

Идентификатор — `<PREFIX>-NNN`. Префикс по умолчанию `T-` (разовые задачи по репозиторию);
если в `$ARGUMENTS` явно указан другой (`SEO`, `VS`, …) или задача — часть уже существующей
пачки, бери её префикс.

## 2. Следующий ID

Считай максимум по **обоим** файлам — ID выполненных задач переиспользовать нельзя:

```powershell
$prefix = 'T'   # или указанный префикс
$m = Select-String -Path .opencode/tasks.md, .opencode/tasks-done.md -Pattern "^## $prefix-(\d+) —" |
  ForEach-Object { [int]$_.Matches[0].Groups[1].Value } |
  Measure-Object -Maximum
$next = if ($m.Maximum) { $m.Maximum + 1 } else { 1 }
$id = '{0}-{1:D3}' -f $prefix, $next
```

`Select-String` не падает, если `tasks-done.md` ещё нет — но если файла нет, создай его с шапкой
(см. существующий формат), чтобы архив был там, куда его ждёт `task-worker`.

## 3. Записать блок

Удали строку-плейсхолдер `_Нет задач..._`, если она есть, и допиши в конец `.opencode/tasks.md`:

```
## <PREFIX>-<NNN> — <заголовок>
- status: todo
- priority: medium
- created:  <ISO8601>   # Get-Date -Format o
- started:
- completed:
- desc: <описание или заголовок>
```

`priority` — `medium` по умолчанию; если в `$ARGUMENTS` есть маркер `(high)`/`(low)` — возьми его
и убери из заголовка.

## 4. Отчёт (observation-контракт)

- **status**: `created`
- **summary**: заголовок задачи одной строкой
- **artifacts**: `.opencode/tasks.md`, ID `<PREFIX>-NNN`
- **next_actions**: «в работе через `/start-task [<PREFIX>-NNN]` (без аргумента — первая свободная)»
