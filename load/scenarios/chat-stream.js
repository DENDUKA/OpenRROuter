import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';
import { config, apiKeyForVU } from '../lib/config.js';
import { streamBody } from '../lib/chat-body.js';

// Кастомная метрика time-to-first-byte. res.timings.waiting = TTFB (мс, float).
const ttfb = new Trend('ttfb_ms');

// Стриминг на отдельном пути /v1/chat/completions/stream (другой хендлер HandleStreamAsync).
// 20 VU держат долгоживущие SSE-соединения. max_tokens:1 -> ответ быстрый, но путь реальный.
export const options = {
    scenarios: {
        chat_stream: {
            executor: 'constant-vus',
            vus: 20,
            duration: '5m',
        },
    },
    thresholds: {
        http_req_failed: [{ threshold: 'rate<0.05', abortOnFail: true }],
        http_req_duration: ['p(95)<15000'],
        ttfb_ms: ['p(95)<3000'],
    },
};

export default function () {
    const res = http.post(
        `${config.baseUrl}/v1/chat/completions/stream`,
        streamBody(config.modelId),
        {
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${apiKeyForVU()}`,
                Accept: 'text/event-stream',
            },
            timeout: '120s',
        }
    );

    ttfb.add(res.timings.waiting);

    check(res, {
        'status is 200': (r) => r.status === 200,
        'is event-stream': (r) =>
            (r.headers['Content-Type'] || r.headers['content-type'] || '').includes('text/event-stream'),
    });
}
