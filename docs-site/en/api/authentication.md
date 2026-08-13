# Authentication

The Rawrter API uses token-based authentication (Bearer Token).

Pass your API key in the `Authorization` header of every HTTP request:

```http
Authorization: Bearer sk-or-your-key
```

Example using cURL:

```bash
curl https://api.openrussiarouter.com/v1/models \
  -H "Authorization: Bearer sk-or-your-key"
```

If the token is not provided or is invalid, the API will return an error with the code:
- `authentication_required`
- `invalid_api_key`