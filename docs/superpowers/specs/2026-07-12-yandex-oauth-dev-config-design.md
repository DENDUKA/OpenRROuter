# Yandex OAuth в dev-кластере

## Цель

Показывать кнопку входа через Яндекс на `http://localhost/login` в локальном
кластере `kind-orr-dev`.

## Решение

Добавить в dev Kustomize отдельный патч для deployment `webapp`. Патч напрямую
задаёт тестовые значения `Yandex__ClientId` и `Yandex__ClientSecret`. Открытое
хранение этих тестовых ключей в Git явно разрешено владельцем проекта.

Патч подключается в
`infra/kustomize/overlays/dev/services/kustomization.yaml`. После синхронизации
Argo CD WebApp получает обе настройки, регистрирует Yandex OAuth-схему, а Razor
Page отображает кнопку.

## Проверка

1. `kubectl kustomize infra/kustomize/overlays/dev/services` успешно собирается
   и содержит обе переменные Yandex в deployment `webapp`.
2. После применения конфигурации и перезапуска WebApp на `/login` отображается
   кнопка «Войти через Яндекс».
3. Нажатие кнопки начинает OAuth-переход с callback-путём `/OAuth-yandex`.

## Вне объёма

- менеджер секретов, SOPS и Sealed Secrets;
- ротация тестовых ключей;
- изменение существующей OAuth-логики приложения.
