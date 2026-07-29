import http from 'k6/http';
import { check } from 'k6';
import { config, apiKeyForVU } from '../lib/config.js';
import { nonStreamBody } from '../lib/chat-body.js';

// 15 RPS steady в течение 15 минут. Выявляет утечки памяти, дрейф outbox-диспетчера,
// рост latency со временем. Запускать ОДИН, не одновременно с другими тяжёлыми сценариями.
export const options = {
    scenarios: {
        soak: {
            executor: 'constant-arrival-rate',
            rate: 15,
            timeUnit: '1s',
            duration: '15m',
            preAllocatedVUs: 50,
            maxVUs: 150,
        },
    },
    thresholds: {
        http_req_failed: [{ threshold: 'rate<0.05', abortOnFail: true }],
        http_req_duration: [
            { threshold: 'p(99)<60000', abortOnFail: true },
            'p(95)<30000',
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
