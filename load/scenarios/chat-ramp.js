import http from 'k6/http';
import { check } from 'k6';
import { config, apiKeyForVU } from '../lib/config.js';
import { nonStreamBody } from '../lib/chat-body.js';

// Основной сценарий: плавный подъём 1 -> 25 RPS, steady 5 мин, спуск до 0.
// Показывает, при каком RPS latency начинает расти и появляются первые 5xx/429.
export const options = {
    scenarios: {
        chat_ramp: {
            executor: 'ramping-arrival-rate',
            startRate: 1,
            timeUnit: '1s',
            preAllocatedVUs: 60,
            maxVUs: 200,
            stages: [
                { duration: '5m', target: 25 },
                { duration: '5m', target: 25 },
                { duration: '2m', target: 0 },
            ],
        },
    },
    thresholds: {
        http_req_failed: [{ threshold: 'rate<0.05', abortOnFail: true }],
        http_req_duration: [
            { threshold: 'p(99)<30000', abortOnFail: true },
            'p(95)<15000',
        ],
        checks: ['rate>0.95'],
    },
};

export default function () {
    const res = http.post(
        `${config.baseUrl}/v1/chat/completions`,
        nonStreamBody(config.modelId),
        {
            headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${apiKeyForVU()}`,
            },
            timeout: '60s',
        }
    );

    check(res, {
        'status is 200': (r) => r.status === 200,
    });
}
