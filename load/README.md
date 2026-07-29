# Нагрузочное тестирование ApiGateway (k6)

Сценарии k6 для проверки стабильности OpenRussiaRouter под пиковой нагрузкой.
Спецификация: `docs/superpowers/specs/2026-07-29-load-testing-design.md`.

## Установка k6

```powershell
winget install grafana.k6
```

## Подготовка

1. Создать тестового пользователя с положительным балансом.
2. Создать ~50 API-ключей под ним (`/workspace/apikeys`).
3. `cp load/.env.example load/.env`, заполнить `K6_API_KEYS`.
4. Проверить модель: `GET https://api.openrussiarouter.com/v1/models` → `K6_MODEL_ID`.

## Запуск

(Финализируется в Task 8.)
