# Подключение Robokassa (Зарубежные карты и СБП) — План реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Завершить подключение платежной системы Robokassa на сайт Rawrter.com для приёма зарубежных карт (Visa/Mastercard) и СБП, синхронизировать настройки личного кабинета Robokassa с продакшен-сервером и открыть приём платежей для всех пользователей.

**Architecture:** ASP.NET Core Razor Pages (WebApp) выполняет чекаут через `PaymentCheckoutService` и адаптер `RobokassaPaymentProvider` (`IPaymentProvider`), формируя подписанные ссылки на оплату с чеком 54-ФЗ («Робочеки») и обратным уведомлением `ResultUrl2`. Подтверждение платежа проверяется через JWS (RS256) с использованием зашитого сертификата Robokassa. BillingService конвертирует валюты (RUB <-> USD) и ведёт баланс.

**Tech Stack:** .NET 8, C#, Razor Pages, Robokassa Merchant API / ResultUrl2 (JWS RS256), xUnit, FluentAssertions.

## Global Constraints

- **Строго без Entity Framework** — только Dapper/Postgres в сервисах с БД; WebApp доменной БД не имеет.
- **Любой warning ломает сборку** — `TreatWarningsAsErrors=true`. Не глушить `#pragma`.
- **Флаги сборки обязательны**: `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`.
- **Инварианты денег**: BillingService — единственный владелец денег. Списание/начисление через trusted FX котировку ЦБ РФ.
- **Безопасность Robokassa**: `ResultUrl2` проверяется по зашитому сертификату Robokassa RS256. В Production `TestMode` строго `false` (проверяется `PaymentStartupGuard`).

---

### Task 1: Настройка переадресации и параметров в Личном Кабинете Robokassa

**Files:**
- Внешняя система: [Личный Кабинет Robokassa](https://partner.robokassa.ru/Shops/Edit/7ca9fcf4-b36b-477e-bb01-8098f4aa7833#Shop_Settings)

**Interfaces:**
- Consumes: URL сайта `https://rawrter.com`
- Produces: Корректные `Success URL`, `Fail URL`, `Result URL` в кабинете магазина `rawrtercom`.

- [ ] **Step 1: Обновить Success URL и Fail URL в Личном кабинете**
  1. Перейти в ЛК Robokassa: `https://partner.robokassa.ru/Shops/Edit/7ca9fcf4-b36b-477e-bb01-8098f4aa7833#Shop_Settings`.
  2. Установить:
     - **Success URL**: `https://rawrter.com/workspace/credits?topup=success` (Метод отсылки: `GET`)
     - **Fail URL**: `https://rawrter.com/workspace/credits?topup=fail` (Метод отсылки: `GET`)
     - **Result URL**: `https://rawrter.com/webhook/robokassa` (Метод отсылки: `POST`)
  3. Сохранить настройки кнопкой «Сохранить».

- [ ] **Step 2: Зафиксировать Пароль #1 для продакшена**
  1. Если текущий Пароль #1 известен — использовать его. Если нет — нажать «Сгенерировать» напротив Пароль #1 и скопировать его в безопасное место.
  2. Нажать «Сохранить».

---

### Task 2: Активация зарубежных карт и оформление договора на вывод средств

**Files:**
- Внешняя система: ЛК Robokassa (`/Support/Requests`, `/Contract`) и почта менеджера `y.pevneva@robokassa.ru`.

**Interfaces:**
- Consumes: Реквизиты ИП Зоткин Д.В. (ИНН 645293322359), сайт `https://rawrter.com`.
- Produces: Включенный шлюз зарубежных карт в Robokassa и подписанный договор на вывод средств.

- [ ] **Step 1: Направить запрос на включение зарубежных карт («Инокарты»)**
  1. Направить письмо менеджеру Юлии Певневой (`y.pevneva@robokassa.ru`) или создать тикет в ЛК Robokassa («Поддержка» -> «Создать запрос»):
     ```text
     Тема: Подключение приёма зарубежных карт для магазина rawrtercom
     Текст:
     Здравствуйте!
     Просим подключить приём платежей с зарубежных банковских карт (Visa / Mastercard) для магазина rawrtercom (ИНН 645293322359).
     Магазин активен, сайт https://rawrter.com полностью готов:
     - Публичная оферта: https://rawrter.com/legal/offer (содержит реквизиты ИП, правила возврата, описание услуг)
     - Каталог услуг и цены: https://rawrter.com/models
     - Контакты поддержки: https://rawrter.com/support
     ```
  2. Дождаться подтверждения от Robokassa о включении приёма международных карт.

- [ ] **Step 2: Активировать вывод денежных средств (заключение договора)**
  1. В ЛК перейти в «Документы и шаблоны» (`https://partner.robokassa.ru/Contract`).
  2. Оплатить идентификационный платёж по реквизитам ООО «РОБОКАССА» с расчётного счёта ИП (электронный акцепт) и прикрепить платёжное поручение, либо отправить бумажное соглашение в 2 экз.

---

### Task 3: Поддержка выбора способов оплаты (СБП и Зарубежные карты) в RobokassaPaymentProvider

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Services/Payments/IPaymentProvider.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Services/Payments/PaymentRequest.cs`
- Modify: `src/OpenRussiaRouter.WebApp/Services/Payments/Robokassa/RobokassaPaymentProvider.cs`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/RobokassaPaymentProviderTests.cs`

**Interfaces:**
- Consumes: `PaymentRequest.PaymentMethodHint` (опциональный параметр, например `"SBP"`, `"BankCard"`).
- Produces: Параметр `IncCurrLabel` в запросе к `https://auth.robokassa.ru/Merchant/Index.aspx` (если передан hint `"SBP"`, в URL передается `IncCurrLabel=SBP`, открывая сразу экран QR-кода СБП; если hint не задан — открывается стандартный выбор способов оплаты).

- [ ] **Step 1: Добавить тест на передачу IncCurrLabel в RobokassaPaymentProviderTests**

Добавить в `tests/OpenRussiaRouter.WebApp.Tests/RobokassaPaymentProviderTests.cs`:
```csharp
[Fact]
public async Task CreateAsync_attaches_IncCurrLabel_when_hint_provided()
{
    var provider = CreateProvider();

    var created = await provider.CreateAsync(
        new PaymentRequest(OrderId, 1000m, "RUB", "Top-up", PaymentMethodHint: "SBP"),
        CancellationToken.None);

    var query = QueryHelpers.ParseQuery(new Uri(created.PayUrl).Query);
    query["IncCurrLabel"].ToString().Should().Be("SBP");
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он не компилируется или падает**

Команда: `dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-build -filter "FullyQualifiedName~CreateAsync_attaches_IncCurrLabel" -nr:false -m:1`
Ожидается: Ошибка компиляции (отсутствие `PaymentMethodHint` в `PaymentRequest`).

- [ ] **Step 3: Добавить PaymentMethodHint в PaymentRequest и обработку в RobokassaPaymentProvider**

В `src/OpenRussiaRouter.WebApp/Services/Payments/IPaymentProvider.cs` (или `PaymentRequest.cs`):
```csharp
public sealed record PaymentRequest(
    string OrderId,
    decimal Amount,
    string Currency,
    string Description,
    string? UserId = null,
    string? PaymentMethodHint = null);
```

В `src/OpenRussiaRouter.WebApp/Services/Payments/Robokassa/RobokassaPaymentProvider.cs` в `CreateAsync`:
```csharp
if (!string.IsNullOrWhiteSpace(request.PaymentMethodHint))
{
    query["IncCurrLabel"] = request.PaymentMethodHint;
}
```
*(Примечание: `IncCurrLabel` не входит в расчет `SignatureValue` при стандартной подписи Robokassa MD5 `MerchantLogin:OutSum:InvId:Receipt:ResultUrl2:Password1`, поэтому подпись не нарушается).*

- [ ] **Step 4: Запустить тесты и убедиться в успехе**

Команда: `dotnet test tests/OpenRussiaRouter.WebApp.Tests --no-restore -nr:false -m:1`
Ожидается: Все тесты проходят успешно (PASS).

- [ ] **Step 5: Зафиксировать коммит**

```bash
git add src/OpenRussiaRouter.WebApp/Services/Payments/ tests/OpenRussiaRouter.WebApp.Tests/
git commit -m "feat(payments): support IncCurrLabel payment method hint in Robokassa provider"
```

---

### Task 4: Открытие чекаута Robokassa для всех пользователей в Credits.cshtml

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml:70-87`
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml.cs:105-117`
- Modify: `src/OpenRussiaRouter.WebApp/Resources/Pages/Workspace/Credits.ru.resx`
- Modify: `src/OpenRussiaRouter.WebApp/Resources/Pages/Workspace/Credits.en.resx`
- Test: `tests/OpenRussiaRouter.WebApp.Tests/Pages/CreditsPageTests.cs` (или аналогичные тесты страницы)

**Interfaces:**
- Consumes: `RobokassaEnabled` из конфигурации.
- Produces: Доступность кнопки «Зарубежные карты (Visa/Mastercard)» и/или «СБП» для всех авторизованных пользователей без ограничения `IsAdmin`.

- [ ] **Step 1: Написать падающий тест на доступность TopUpRobokassa обычному пользователю**

Проверить, что при обычном пользователе без роли `Admin` вызов `OnPostTopUpRobokassaAsync` не возвращает `ForbidResult`, а инициирует чекаут и возвращает редирект на платёжную страницу.

- [ ] **Step 2: Запустить тест и убедиться, что он падает (возвращается Forbid)**

- [ ] **Step 3: Обновить Credits.cshtml.cs и Credits.cshtml**

В `src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.cshtml.cs`:
Убрать ограничение `IsAdmin` из `OnPostTopUpRobokassaAsync`:
```csharp
public Task<IActionResult> OnPostTopUpRobokassaAsync(string? method = null) =>
    RobokassaEnabled
        ? TopUpAsync(providerName: "robokassa", methodHint: method)
        : Task.FromResult<IActionResult>(NotFound());
```

В `Credits.cshtml`:
Заменить админский гвард на проверку `Model.RobokassaEnabled`:
```html
@if (Model.RobokassaEnabled)
{
    <button type="submit" formaction="?handler=TopUpRobokassa" class="btn btn-outline-primary"
            data-goal="checkout_started" title="@Localizer["Credits_TopUpForeignCardsTitle"]">
        <i class="bi bi-globe me-1"></i> @Localizer["Credits_TopUpForeignCards"]
    </button>
}
```

Обновить локализацию в `Credits.ru.resx`:
- `Credits_TopUpForeignCards`: "Зарубежные карты (Visa / Mastercard)"
- `Credits_TopUpForeignCardsTitle`: "Оплата картами зарубежных банков через Robokassa"

В `Credits.en.resx`:
- `Credits_TopUpForeignCards`: "Foreign cards (Visa / Mastercard)"
- `Credits_TopUpForeignCardsTitle`: "Pay with international bank cards via Robokassa"

- [ ] **Step 4: Прогнать тесты и проверить сборку**

Команда: `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`
Ожидается: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 5: Зафиксировать коммит**

```bash
git add src/OpenRussiaRouter.WebApp/Pages/Workspace/Credits.* src/OpenRussiaRouter.WebApp/Resources/Pages/Workspace/Credits.*
git commit -m "feat(credits): enable Robokassa foreign cards top-up for all users"
```

---

### Task 5: Настройка переменных окружения на продакшен-сервере и проверка сквозного платежа

**Files:**
- Сервер: `/opt/orr/.env` на хосте `89.191.225.228`
- Deploy: `deploy/compose/compose.prod.yml`

**Interfaces:**
- Consumes: Секретный Пароль #1 из ЛК Robokassa.
- Produces: Работающая боевая интеграция на `https://rawrter.com`.

- [ ] **Step 1: Настроить переменные окружения на сервере**
  В `/opt/orr/.env` прописать:
  ```env
  ROBOKASSA_MERCHANT_LOGIN=rawrtercom
  ROBOKASSA_PASSWORD1=<актуальный Пароль #1 из ЛК>
  ROBOKASSA_HASH_ALGORITHM=MD5
  ROBOKASSA_TAXATION_SYSTEM=usn_income
  ROBOKASSA_TEST_MODE=false
  ```
  *(Если Robokassa также должна обслуживать стандартную оплату по СБП и картам РФ: установить `PAYMENTS_ACTIVE_PROVIDER=robokassa`)*.

- [ ] **Step 2: Выполнить деплой через update-prod.sh**
  Запустить обновление на сервере:
  ```bash
  cd /opt/orr && bash update-prod.sh
  ```
  Убедиться, что все сервисы прошли health-gate (`webapp`, `billing`, `gateway` healthy).

- [ ] **Step 3: Провести тестовый реальный платёж на 100 ₽**
  1. Авторизоваться на `https://rawrter.com/workspace/credits`.
  2. Выбрать сумму 100 ₽ и нажать кнопку оплаты.
  3. Проверить переход на страницу Robokassa, оплатить картой или по СБП.
  4. Убедиться, что:
     - Произошёл редирект на `/workspace/credits?topup=success`;
     - Баланс пользователя в USD успешно пополнился;
     - Вебхук `/webhook/robokassa` ответил `OK{invId}`;
     - В ЛК Robokassa операция отображается в статусе «Зачислено», сформирован электронный чек («Робочеки»).
