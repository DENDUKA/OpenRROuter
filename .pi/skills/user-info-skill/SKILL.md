---
name: user-info-skill
description: Extract complete user journey, actions, and lifecycle for OpenRussiaRouter by email (DB registrations, API keys, balance/top-ups, LLM requests, Yandex Metrika visits, traffic source, and goals like copy_api_key).
metadata:
  project: OpenRussiaRouter
---

# User Info Skill: Полный аудит жизненного цикла пользователя по Email

Скилл предназначен для сквозного восстановления пути пользователя в OpenRussiaRouter: от первого визита с рекламы до регистрации, генерации и копирования API-ключа, оплат и каждого запроса к моделям LLM.

## Почему не нужен отдельный MCP сервер?
Скрипт скилла (`user_info.py`) обращается к **официальному REST API Яндекс Метрики и Директа напрямую** через стандартную библиотеку Python (`urllib`).
* **0 внешних зависимостей** — не нужно ставить pip-пакеты, venv или поднимать фоновый MCP-демон.
* Работает из коробки на любом компьютере с Python 3.

---

## Что нужно коллеге для запуска на другом компьютере?

1. **Доступ к серверу БД:**
   * **Вариант А (по SSH-ключу):** Если публичный ключ коллеги добавлен в `~/.ssh/authorized_keys` на сервере `201.51.6.246`, скрипт определит это автоматически и сработает **без пароля**.
   * **Вариант Б (по паролю):** Передать пароль через переменную окружения `SSH_PASSWORD` в памяти, либо скрипт запросит его интерактивно.

2. **Токен Яндекс Метрики:**
   Скрипт автоматически ищет токен в следующем порядке:
   1. Переменная окружения `YANDEX_OAUTH_TOKEN` (или `YANDEX_METRIKA_TOKEN`, `YD_OAUTH_TOKEN`).
   2. Аргумент `--metrika-token <TOKEN>`.
   3. Файл `../yandex_direct/.pi/secrets/yandex-ads-token` (если репозиторий `yandex_direct` склонирован рядом).
   4. Файл `~/.config/orr/yandex-ads-token`.

---

## Примеры запуска

### 1. Самый простой запуск (по SSH-ключу или с запросом пароля):
```bash
python3 .pi/skills/user-info-skill/user_info.py --email user@example.com
```

### 2. С передачей пароля и токена через переменные окружения (для CI или автоматизации):
```bash
SSH_PASSWORD="<пароль>" YANDEX_OAUTH_TOKEN="<токен>" python3 .pi/skills/user-info-skill/user_info.py --email user@example.com
```

### 3. Получение сырого JSON для интеграций:
```bash
SSH_PASSWORD="<пароль>" python3 .pi/skills/user-info-skill/user_info.py --email user@example.com --json
```

---

## Безопасность
* **Пароль от SSH НИКОГДА не сохраняется на диск** — только рантайм-переменная или ввод через консоль.
