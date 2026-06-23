# Аутентификация

API OpenRussiaRouter использует аутентификацию на основе токенов (Bearer Token).

Передавайте ваш API-ключ в заголовке `Authorization` каждого HTTP-запроса:

```http
Authorization: Bearer sk-or-ваш-ключ
```

Пример с использованием cURL:

```bash
curl https://api.openrussiarouter.com/v1/models \
  -H "Authorization: Bearer sk-or-ваш-ключ"
```

В случае, если токен не передан или он недействителен, API вернет ошибку с кодом:
- `authentication_required`
- `invalid_api_key`
