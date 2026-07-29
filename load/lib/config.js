import exec from 'k6/execution';

const baseUrl = (__ENV.K6_BASE_URL || 'https://api.openrussiarouter.com').replace(/\/+$/, '');
const modelId = __ENV.K6_MODEL_ID || 'gemini-3.5-flash';

const apiKeys = (__ENV.K6_API_KEYS || '')
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);

if (apiKeys.length === 0) {
    throw new Error(
        'K6_API_KEYS не задан. Заполните load/.env (см. .env.example) — список sk-or-* ключей через запятую.'
    );
}

export const config = {
    baseUrl,
    modelId,
    apiKeys,
};

// Раздаёт ключи round-robin по VU: каждый виртуальный пользователь всегда использует один ключ.
export function apiKeyForVU() {
    const vu = exec.vu.idInTest; // 1-based
    return apiKeys[(vu - 1) % apiKeys.length];
}
