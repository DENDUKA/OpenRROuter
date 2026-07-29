// reasoning_effort:'none' обязателен: gemini-3.5-flash имеет default_effort 'high',
// и ReasoningRequestPatcher подставит его, если поле отсутствует → лишняя latency/cost.
const BASE = (modelId) => ({
    model: modelId,
    messages: [{ role: 'user', content: 'hi' }],
    max_tokens: 1,
    reasoning_effort: 'none',
});

export function nonStreamBody(modelId) {
    return JSON.stringify({ ...BASE(modelId), stream: false });
}

// Поле stream НЕ добавляем: путь /v1/chat/completions/stream сам инжектит stream:true
// + stream_options.include_usage:true через InjectStreamOptions на шлюзе.
export function streamBody(modelId) {
    return JSON.stringify(BASE(modelId));
}
