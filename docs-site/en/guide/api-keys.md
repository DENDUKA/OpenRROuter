# API Keys Management

The API key is your identifier for accessing the Rawrter API. One key works for all models.

## Creating a Key

1. Go to the dashboard.
2. In the "API Keys" section, click create.
3. Copy the generated key (format `sk-or-<36 random characters>`).

::: warning Save your key
The full raw key is only shown once upon creation. Only its secure SHA-256 hash is saved in our database. If you lose the key, you will have to revoke it and create a new one.
:::

## Key Format

All Rawrter keys start with the `sk-or-` prefix. Use the key in the `Authorization` header when sending HTTP requests:

```http
Authorization: Bearer sk-or-xxx
```

## Security

- **Do not commit keys to code.** Use environment variables (`.env`).
- **Promptly revoke keys** if you suspect they have been compromised.
- You can create multiple keys for different projects or development environments.

---

## Spend Limits

To protect against accidental overspending (for example, while debugging infinite loops in scripts or providing a key to third-party tools), you can configure individual spending limits for each API key:

- **Daily Spend Limit:** the maximum USD amount this key can spend in the current day.
- **Weekly Spend Limit:** the maximum USD spending allowed over a rolling 7-day window.

Limits can be configured in the dashboard: navigate to **API Keys** → click **Limits** next to the desired key.

Once a limit is reached, the gateway will reject subsequent requests with the error code `api_key_limit_exceeded` (HTTP 402).
