# Chat UI Clients Integration

You can use Rawrter with any web or desktop chat UI that supports connecting custom OpenAI-compatible providers.

---

## LibreChat

[LibreChat](https://www.librechat.ai/) is an advanced open-source web interface for LLMs.

To connect, add a custom endpoint in your `librechat.yaml` file:

```yaml
endpoints:
  custom:
    - name: "Rawrter"
      apiKey: "${RAWRTER_API_KEY}"
      baseURL: "https://api.rawrter.com/v1"
      models:
        default: ["gpt-4o", "claude-3-5-sonnet", "deepseek/deepseek-r1"]
        fetch: true # Automatically fetch catalog via GET /v1/models
      titleConvo: true
      titleModel: "gpt-4o-mini"
      modelDisplayLabel: "Rawrter"
```

---

## Open-WebUI

1. Open **Admin Panel** $\rightarrow$ **Settings** $\rightarrow$ **Connections**.
2. In the **OpenAI API** section, add a new connection:
   * **URL:** `https://api.rawrter.com/v1`
   * **API Key:** `sk-or-your-key`
3. Click the verify connection button and sync models.

---

## NextChat (ChatGPT-Next-Web)

1. Open Settings (gear icon in the bottom-left corner).
2. In the **Model Service** section:
   * **Model Provider:** `OpenAI`
   * **Endpoint:** `https://api.rawrter.com` (or `https://api.rawrter.com/v1`)
   * **API Key:** `sk-or-your-key`
3. In **Custom Models**, add desired models (e.g. `+claude-3-5-sonnet,+deepseek/deepseek-r1`).

---

## Chatbox (Desktop & Mobile)

1. Open **Settings** $\rightarrow$ **Model**.
2. **Provider:** `OpenAI API`.
3. **API Host:** `https://api.rawrter.com`
4. **API Key:** `sk-or-your-key`
5. Select a model from the dropdown or enter an ID manually.

---

## TypingMind

1. Open **Settings** $\rightarrow$ **AI Model Providers** $\rightarrow$ **Add custom provider (OpenAI Compatible)**.
2. **Provider Name:** `Rawrter`
3. **Endpoint URL:** `https://api.rawrter.com/v1`
4. **API Key:** `sk-or-your-key`
5. Click **Connect & Fetch Models** to auto-load available models.
