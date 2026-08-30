# IDE and Code Editors Integration

Thanks to Rawrter's full compatibility with the OpenAI API format, you can connect any modern code editor and AI coding assistant without running extra local proxy servers.

---

## Cursor / Windsurf

1. Open editor **Settings** → **Models** (or **AI** section).
2. Enable **OpenAI API Key** or choose **Custom OpenAI Provider**.
3. Configure connection details:
   * **API Base URL:** `https://api.rawrter.com/v1`
   * **API Key:** your key in format `sk-or-...`
4. Add desired models to the models list (e.g. `claude-3-5-sonnet`, `gpt-4o`, `deepseek/deepseek-r1`).
5. Click **Save** / **Verify**.

---

## VS Code: Cline / Roo Code / Kilo Code

Popular autonomous coding agents support Rawrter natively through OpenAI-Compatible or OpenRouter provider modes:

1. Open the **Cline** or **Roo Code** extension in the sidebar.
2. Go to settings (Gear icon → **API Configuration**).
3. Select **API Provider:** `OpenAI Compatible` (or `OpenRouter`).
4. Fill in the fields:
   * **Base URL:** `https://api.rawrter.com/v1`
   * **API Key:** `sk-or-...`
   * **Model ID:** model ID from Rawrter catalog (e.g. `anthropic/claude-3.5-sonnet` or `deepseek/deepseek-r1`).
5. (Optional) Enable Reasoning support if using reasoning models.

---

## VS Code: Continue.dev

1. Open Continue configuration file: `~/.continue/config.json`.
2. Add models to the `models` array:

```json
{
  "models": [
    {
      "title": "Claude 3.5 Sonnet (Rawrter)",
      "provider": "openai",
      "model": "claude-3-5-sonnet",
      "apiKey": "sk-or-your-key",
      "apiBase": "https://api.rawrter.com/v1"
    },
    {
      "title": "DeepSeek R1 (Rawrter)",
      "provider": "openai",
      "model": "deepseek/deepseek-r1",
      "apiKey": "sk-or-your-key",
      "apiBase": "https://api.rawrter.com/v1"
    }
  ]
}
```

---

## CLI Tools

### Aider

Run terminal assistant Aider with Rawrter:

```bash
export OPENAI_API_BASE="https://api.rawrter.com/v1"
export OPENAI_API_KEY="sk-or-your-key"

aider --model openai/claude-3-5-sonnet
```

### Claude Code

Claude Code connects directly via the standard Anthropic LLM Gateway protocol:

```bash
# Linux / macOS / WSL
export ANTHROPIC_BASE_URL="https://api.rawrter.com"
export ANTHROPIC_AUTH_TOKEN="sk-or-your-key"
export ANTHROPIC_MODEL="ORR/Claude-3-7-Sonnet" # or any other model from the catalog
```

In PowerShell (Windows):
```powershell
$env:ANTHROPIC_BASE_URL = "https://api.rawrter.com"
$env:ANTHROPIC_AUTH_TOKEN = "sk-or-your-key"
$env:ANTHROPIC_MODEL = "ORR/Claude-3-7-Sonnet"
```

Or in Claude Code configuration file (`~/.claude.json` / `%USERPROFILE%\.claude.json` or `.claude/settings.json`):

```json
{
  "env": {
    "ANTHROPIC_BASE_URL": "https://api.rawrter.com",
    "ANTHROPIC_AUTH_TOKEN": "sk-or-your-key",
    "ANTHROPIC_MODEL": "ORR/Claude-3-7-Sonnet"
  }
}
```

> **Note:** `ANTHROPIC_BASE_URL` must be configured **without the `/v1` suffix** (the client appends `/v1/messages` automatically).

### OpenCode

```bash
export OPENAI_BASE_URL="https://api.rawrter.com/v1"
export OPENAI_API_KEY="sk-or-your-key"
```
