# API Keys Management

The API key is your identifier for accessing the OpenRussiaRouter API. One key works for all models.

## Creating a Key

1. Go to the dashboard.
2. In the "API Keys" section, click create.
3. Copy the generated key (format `sk-or-<36 random characters>`).

::: warning Save your key
The full raw key is only shown once upon creation. Only its secure SHA-256 hash is saved in our database. If you lose the key, you will have to revoke it and create a new one.
:::

## Key Format

All OpenRussiaRouter keys start with the `sk-or-` prefix. Use the key in the `Authorization` header when sending HTTP requests:

```http
Authorization: Bearer sk-or-xxx
```

## Security

- **Do not commit keys to code.** Use environment variables (`.env`).
- **Promptly revoke keys** if you suspect they have been compromised.
- You can create multiple keys for different projects or development environments.