# OpenRussiaRouter — обязательный контекст для pi

- **Источник истины** по сборке, тестам, архитектуре, инвариантам и деплою — корневой `CLAUDE.md`
  (pi автоматически грузит только `AGENTS.md`, который является коротким указателем на него).
  **Прочитай `CLAUDE.md` полностью в начале первой задачи в этом репозитории, до любых правок.**
- Перед правкой любого файла под `src/OpenRussiaRouter.<Service>/` прочитай `CLAUDE.md` этого
  сервиса; общие библиотеки → `src/OpenRussiaRouter.BuildingBlocks/CLAUDE.md`; `.proto`/DTO →
  `src/OpenRussiaRouter.Contracts/CLAUDE.md`. (Правило «Agent workflow» в корневом `CLAUDE.md`;
  быстрый путь — команда `/svc <Service>`.)
- Операции git (commit, push, checkout, stash и т.п.) — **только по явной команде пользователя**.
  Read-only команды (`status`, `diff`, `log`, `rev-parse`) — можно.
- Отвечай на русском.
