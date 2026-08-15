# Function Calling & Tools

The `tools` parameter allows you to connect external functions and services to models in Rawrter. The model does not execute code directly — it generates structured arguments to call your local functions, and then synthesizes a final answer based on the returned results.

---

## Key Parameters

| Parameter | Type | Description |
|---|---|---|
| `tools` | array | A list of available tools. Each item has type `function` and contains the function signature description in JSON Schema format. |
| `tool_choice` | string \| object | Controls tool selection: `"auto"` (default), `"none"` (disallow tool calls), `"required"` (force calling at least one tool), or `{ "type": "function", "function": { "name": "..." } }`. |

::: tip Automatic Schema Compatibility
The Rawrter gateway automatically inlines recursive and referenced definitions (`$ref`, `$defs`) when routing requests to models that do not natively support external schema references (such as the Google Gemini family).
:::

---

## Execution Workflow

1. **Request:** The client sends the user message along with the `tools` array.
2. **Model Response:** The model returns a message with `finish_reason: "tool_calls"` and a `tool_calls` array containing JSON arguments.
3. **Local Execution:** Your application executes the requested function locally.
4. **Final Response:** The client appends the function result to the message history with `role: "tool"` and the matching `tool_call_id`. The model generates the final answer for the user.

---

## Examples

::: code-group

```python [Python (OpenAI SDK)]
import json
from openai import OpenAI

client = OpenAI(
    api_key="sk-or-your-key",
    base_url="https://api.rawrter.com/v1",
)

# 1. Tool definition
tools = [
    {
        "type": "function",
        "function": {
            "name": "get_current_weather",
            "description": "Get current weather in a given city",
            "parameters": {
                "type": "object",
                "properties": {
                    "location": {
                        "type": "string",
                        "description": "City name, e.g. London, Tokyo",
                    },
                    "unit": {"type": "string", "enum": ["celsius", "fahrenheit"]},
                },
                "required": ["location"],
            },
        },
    }
]

messages = [{"role": "user", "content": "What is the weather in London?"}]

# 2. First request: model decides to call tool
response = client.chat.completions.create(
    model="gpt-4o",
    messages=messages,
    tools=tools,
    tool_choice="auto",
)

response_message = response.choices[0].message
messages.append(response_message)

# 3. Handle tool_calls
if response_message.tool_calls:
    for tool_call in response_message.tool_calls:
        function_name = tool_call.function.name
        function_args = json.loads(tool_call.function.arguments)
        
        # Simulate function execution
        if function_name == "get_current_weather":
            function_response = json.dumps({
                "location": function_args.get("location"),
                "temperature": "+18",
                "condition": "Sunny"
            })
            
            # 4. Return function result back to model
            messages.append({
                "tool_call_id": tool_call.id,
                "role": "tool",
                "name": function_name,
                "content": function_response,
            })

    # Final response from model
    final_response = client.chat.completions.create(
        model="gpt-4o",
        messages=messages,
    )
    print(final_response.choices[0].message.content)
```

```javascript [Node.js]
import OpenAI from "openai";

const client = new OpenAI({
  apiKey: "sk-or-your-key",
  baseURL: "https://api.rawrter.com/v1",
});

const tools = [
  {
    type: "function",
    function: {
      name: "get_current_weather",
      description: "Get weather in a city",
      parameters: {
        type: "object",
        properties: {
          location: { type: "string" },
        },
        required: ["location"],
      },
    },
  },
];

async function main() {
  const messages = [{ role: "user", content: "What is the weather in London?" }];

  const response = await client.chat.completions.create({
    model: "gpt-4o",
    messages,
    tools,
  });

  const message = response.choices[0].message;
  messages.push(message);

  if (message.tool_calls) {
    for (const toolCall of message.tool_calls) {
      if (toolCall.function.name === "get_current_weather") {
        const result = JSON.stringify({ location: "London", temperature: "+18°C" });
        messages.push({
          role: "tool",
          tool_call_id: toolCall.id,
          content: result,
        });
      }
    }

    const finalResponse = await client.chat.completions.create({
      model: "gpt-4o",
      messages,
    });

    console.log(finalResponse.choices[0].message.content);
  }
}

main();
```

:::
