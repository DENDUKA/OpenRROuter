# Интеграция с IDE и редакторами кода

Благодаря полной совместимости Rawrter с форматом OpenAI API, вы можете использовать любые современные редакторы и кодинг-ассистенты без установки дополнительных прокси-серверов.

---

## Cursor / Windsurf

1. Откройте **Settings** (Настройки) редактора → раздел **Models** (или **AI**).
2. Включите опцию **OpenAI API Key** или выберите **Custom OpenAI Provider**.
3. Укажите параметры подключения:
   * **API Base URL:** `https://api.rawrter.com/v1`
   * **API Key:** ваш ключ в формате `sk-or-...`
4. Добавьте нужные модели в список доступных (например, `claude-3-5-sonnet`, `gpt-4o`, `deepseek/deepseek-r1`).
5. Нажмите **Save** / **Verify**.

---

## VS Code: Cline / Roo Code / Kilo Code

Популярные автономные агенты кодинга поддерживают Rawrter через режим OpenAI-Compatible или OpenRouter:

1. Откройте расширение **Cline** или **Roo Code** в боковой панели.
2. Перейдите в настройки (иконка шестеренки → **API Configuration**).
3. Выберите **API Provider:** `OpenAI Compatible` (или `OpenRouter`).
4. Заполните поля:
   * **Base URL:** `https://api.rawrter.com/v1`
   * **API Key:** `sk-or-...`
   * **Model ID:** идентификатор модели из каталога Rawrter (например, `anthropic/claude-3.5-sonnet` или `deepseek/deepseek-r1`).
5. (Опционально) Включите поддержку рассуждений (Reasoning), если используете reasoning-модели.

---

## VS Code: Continue.dev

1. Откройте файл конфигурации Continue: `~/.continue/config.json`.
2. Добавьте модели в массив `models`:

```json
{
  "models": [
    {
      "title": "Claude 3.5 Sonnet (Rawrter)",
      "provider": "openai",
      "model": "claude-3-5-sonnet",
      "apiKey": "sk-or-ваш-ключ",
      "apiBase": "https://api.rawrter.com/v1"
    },
    {
      "title": "DeepSeek R1 (Rawrter)",
      "provider": "openai",
      "model": "deepseek/deepseek-r1",
      "apiKey": "sk-or-ваш-ключ",
      "apiBase": "https://api.rawrter.com/v1"
    }
  ]
}
```

---

## CLI-инструменты

### Aider

Запуск терминального ассистента Aider с Rawrter:

```bash
export OPENAI_API_BASE="https://api.rawrter.com/v1"
export OPENAI_API_KEY="sk-or-ваш-ключ"

aider --model openai/claude-3-5-sonnet
```

### OpenCode / Claude Code

При использовании CLI-агентов передайте переменные окружения:

```bash
export ANTHROPIC_BASE_URL="https://api.rawrter.com/v1" # если поддерживается OpenAI-адаптер
# или
export OPENAI_BASE_URL="https://api.rawrter.com/v1"
export OPENAI_API_KEY="sk-or-ваш-ключ"
```
