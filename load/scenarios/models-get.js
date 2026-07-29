import http from 'k6/http';
import { check } from 'k6';
import { config } from '../lib/config.js';

// GET /v1/models публичный (без Bearer), не биллингится, не ходит к upstream.
// Показывает потолок инфраструктуры шлюза без LLM.
export const options = {
    scenarios: {
        models_get: {
            executor: 'constant-arrival-rate',
            rate: 50,
            timeUnit: '1s',
            duration: '3m',
            preAllocatedVUs: 20,
            maxVUs: 100,
        },
    },
    thresholds: {
        http_req_failed: [{ threshold: 'rate<0.01', abortOnFail: true }],
        http_req_duration: ['p(95)<2000'],
    },
};

export default function () {
    const res = http.get(`${config.baseUrl}/v1/models`, { timeout: '30s' });
    check(res, { 'status is 200': (r) => r.status === 200 });
}
