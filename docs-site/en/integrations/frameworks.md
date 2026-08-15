# Frameworks and SDKs

Examples of integrating Rawrter with popular AI development libraries and frameworks.

---

## Vercel AI SDK (TypeScript / Next.js)

Use the `@ai-sdk/openai` package with a custom provider instance:

```typescript
import { createOpenAI } from '@ai-sdk/openai';
import { generateText } from 'ai';

const rawrter = createOpenAI({
  baseURL: 'https://api.rawrter.com/v1',
  apiKey: process.env.RAWRTER_API_KEY,
});

async function main() {
  const { text } = await generateText({
    model: rawrter('claude-3-5-sonnet'),
    prompt: 'Provide a brief explanation of quantum computing.',
  });

  console.log(text);
}

main();
```

---

## LangChain (Python)

```python
import os
from langchain_openai import ChatOpenAI

llm = ChatOpenAI(
    model="gpt-4o",
    openai_api_base="https://api.rawrter.com/v1",
    openai_api_key=os.environ.get("RAWRTER_API_KEY"),
    temperature=0.7,
)

response = llm.invoke("Explain the Transformer architecture in 3 sentences.")
print(response.content)
```

---

## LlamaIndex (Python)

```python
import os
from llama_index.llms.openai import OpenAI

llm = OpenAI(
    model="gpt-4o-mini",
    api_base="https://api.rawrter.com/v1",
    api_key=os.environ.get("RAWRTER_API_KEY"),
)

response = llm.complete("Name 5 key principles of clean code.")
print(response.text)
```

---

## C# / .NET

### 1. Official `OpenAI` Package (v2.x)

```csharp
using System;
using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

var apiKey = Environment.GetEnvironmentVariable("RAWRTER_API_KEY") ?? "sk-or-...";
var endpoint = new Uri("https://api.rawrter.com/v1");

var client = new OpenAIClient(
    new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Endpoint = endpoint });

var chatClient = client.GetChatClient("gpt-4o");

ChatCompletion completion = await chatClient.CompleteChatAsync([
    new UserChatMessage("Hello! Give an example function in C#.")
]);

Console.WriteLine(completion.Content[0].Text);
```

### 2. Microsoft Semantic Kernel

```csharp
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

var builder = Kernel.CreateBuilder();

builder.AddOpenAIChatCompletion(
    modelId: "claude-3-5-sonnet",
    apiKey: "sk-or-your-key",
    endpoint: new Uri("https://api.rawrter.com/v1"));

var kernel = builder.Build();
var chatService = kernel.GetRequiredService<IChatCompletionService>();

var history = new ChatHistory();
history.AddUserMessage("What are the main advantages of microservice architecture?");

var result = await chatService.GetChatMessageContentAsync(history);
Console.WriteLine(result.Content);
```
