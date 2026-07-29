// reasoning_effort НЕ добавляем: gemini-3.5-flash не поддерживает reasoning, и наличие этого
// поля вызывает 400 unsupported_parameter (ReasoningRequestPatcher). Поле отсутствует = норма.
const BASE = (modelId) => ({
    model: modelId,
    messages: [{ role: 'user', content: 'hi' }],
    max_tokens: 1,
});

export function nonStreamBody(modelId) {
    return JSON.stringify({ ...BASE(modelId), stream: false });
}

// Поле stream НЕ добавляем: путь /v1/chat/completions/stream сам инжектит stream:true
// + stream_options.include_usage:true через InjectStreamOptions на шлюзе.
export function streamBody(modelId) {
    return JSON.stringify(BASE(modelId));
}
