import http from 'k6/http';
import { check } from 'k6';
import { config, apiKeyForVU } from '../lib/config.js';
import { nonStreamBody } from '../lib/chat-body.js';

export const options = {
    vus: 1,
    iterations: 1,
    thresholds: {
        // Без abortOnFail: smoke просто репортит. Человек смотрит результат руками.
        checks: ['rate==1'],
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
        'has choices': (r) => {
            try {
                const body = JSON.parse(r.body);
                return Array.isArray(body.choices) && body.choices.length > 0;
            } catch {
                return false;
            }
        },
    });

    console.log(`smoke: status=${res.status} ttfb_ms=${res.timings.waiting.toFixed(0)} body=${res.body?.slice(0, 300)}`);
}
