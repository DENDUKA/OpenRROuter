# Models List

To get the up-to-date list of models, use the API endpoint:

`GET /v1/models`

The response will return a list of models available for your account, in an OpenAI-compatible format.

## API Response Format

```json
{
  "object": "list",
  "data": [
    {
      "id": "gpt-4o",
      "object": "model",
      "created": 1715367049,
      "owned_by": "openai"
    },
    {
      "id": "claude-3-5-sonnet",
      "object": "model",
      "created": 1718841600,
      "owned_by": "anthropic"
    }
  ]
}
```

*Note: The `id` identifier from the response is the value you must pass in the `model` parameter when creating a request to `/v1/chat/completions`.*