# Интеграция с UI-клиентами чатов

Вы можете использовать Rawrter в любых веб- и десктопных чат-интерфейсах, поддерживающих подключение собственных OpenAI-совместимых провайдеров.

---

## LibreChat

[LibreChat](https://www.librechat.ai/) — продвинутый open-source веб-интерфейс для работы с LLM.

Для подключения добавьте кастомную конечную точку в файл `librechat.yaml`:

```yaml
endpoints:
  custom:
    - name: "Rawrter"
      apiKey: "${RAWRTER_API_KEY}"
      baseURL: "https://api.rawrter.com/v1"
      models:
        default: ["gpt-4o", "claude-3-5-sonnet", "deepseek/deepseek-r1"]
        fetch: true # Автоматически получить каталог через GET /v1/models
      titleConvo: true
      titleModel: "gpt-4o-mini"
      modelDisplayLabel: "Rawrter"
```

---

## Open-WebUI

1. Откройте **Панель администратора** (Admin Panel) → **Настройки** (Settings) → **Подключения** (Connections).
2. В разделе **OpenAI API** добавьте новое подключение:
   * **URL:** `https://api.rawrter.com/v1`
   * **Ключ API:** `sk-or-ваш-ключ`
3. Нажмите кнопку проверки связи и синхронизации моделей.

---

## NextChat (ChatGPT-Next-Web)

1. Откройте настройки (иконка шестеренки в левом нижнем углу).
2. В секции **Служба модели** (Model Service):
   * **Поставщик модели (Model Provider):** `OpenAI`
   * **Интерфейс сервиса (Endpoint):** `https://api.rawrter.com` (или `https://api.rawrter.com/v1`)
   * **API-ключ:** `sk-or-ваш-ключ`
3. В поле **Пользовательские модели** (Custom Models) добавьте нужные модели (например, `+claude-3-5-sonnet,+deepseek/deepseek-r1`).

---

## Chatbox (Desktop & Mobile)

1. Откройте **Настройки** → **Модель**.
2. **Провайдер:** `OpenAI API`.
3. **API Host:** `https://api.rawrter.com`
4. **API Key:** `sk-or-ваш-ключ`
5. Выберите модель из выпадающего списка или введите имя вручную.

---

## TypingMind

1. Откройте **Settings** → **AI Model Providers** → **Add custom provider (OpenAI Compatible)**.
2. **Provider Name:** `Rawrter`
3. **Endpoint URL:** `https://api.rawrter.com/v1`
4. **API Key:** `sk-or-ваш-ключ`
5. Нажмите **Connect & Fetch Models** для автоматической загрузки списка моделей.
