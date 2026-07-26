---
description: Добавить новую задачу в .opencode/tasks.md со следующим ID и status=todo.
---

# Добавить задачу

`$ARGUMENTS` — заголовок (и, через перенос/дополнительно, описание). Если пусто — **стоп**,
спроси заголовок.

## 1. Следующий ID

Прочитай `.opencode/tasks.md`, найди max числовой суффикс в `## T-NNN —`:

```powershell
$m = Select-String -Path .opencode/tasks.md -Pattern '^## T-(\d+) —' |
  ForEach-Object { [int]$_.Matches[0].Groups[1].Value } |
  Measure-Object -Maximum
$next = if ($m.Maximum) { $m.Maximum + 1 } else { 1 }
$id = 'T-{0:D3}' -f $next
```

## 2. Записать блок

Удали строку-плейсхолдер `_Нет задач..._`, если она есть, и допиши в конец файла:

```
## T-<NNN> — <заголовок>
- status: todo
- priority: medium
- created:  <ISO8601>   # Get-Date -Format o
- started:
- completed:
- desc: <описание или заголовок>
```

`priority` — `medium` по умолчанию; если в `$ARGUMENTS` есть маркер `(high)`/`(low)` — возьми его
и убери из заголовка.

## 3. Отчёт (observation-контракт)

- **status**: `created`
- **summary**: заголовок задачи одной строкой
- **artifacts**: `.opencode/tasks.md`, ID `T-NNN`
- **next_actions**: «в работе через `/start-task [T-NNN]` (без аргумента — первая свободная)»
