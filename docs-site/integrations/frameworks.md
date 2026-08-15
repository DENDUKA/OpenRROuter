# Фреймворки и SDK

Примеры подключения Rawrter к популярным библиотекам разработки AI-приложений.

---

## Vercel AI SDK (TypeScript / Next.js)

Используйте пакет `@ai-sdk/openai` с созданием кастомного инстанса:

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
    prompt: 'Напиши краткое объяснение квантовых вычислений.',
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

response = llm.invoke("Объясни архитектуру трансформеров за 3 предложения.")
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

response = llm.complete("Назови 5 ключевых принципов чистого кода.")
print(response.text)
```

---

## C# / .NET

### 1. Официальный пакет `OpenAI` (v2.x)

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
    new UserChatMessage("Привет! Напиши пример функции на C#.")
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
    apiKey: "sk-or-ваш-ключ",
    endpoint: new Uri("https://api.rawrter.com/v1"));

var kernel = builder.Build();
var chatService = kernel.GetRequiredService<IChatCompletionService>();

var history = new ChatHistory();
history.AddUserMessage("Расскажи о преимуществах микросервисной архитектуры.");

var result = await chatService.GetChatMessageContentAsync(history);
Console.WriteLine(result.Content);
```
