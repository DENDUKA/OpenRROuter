# AuthService — CLAUDE.md

Учётные записи, выдача сессионных токенов, жизненный цикл API-ключей, email-верификация,
сброс/смена пароля, инвайты, внешний логин (Google/Yandex). **gRPC-сервер (порт 5001, в контейнере
8080)** + HTTP `/health`. Layered: Domain → Application → Infrastructure. Своей HTTP-API,
кроме health, нет.

## Keyset pagination

`ListUsersAsync` and `ListApiKeysAsync` accept an id-only cursor, but PostgreSQL resolves it to
`(created_at_utc, id)` and compares that tuple lexicographically. This key must stay aligned with
`order by created_at_utc desc, id desc`; the API-key cursor is also scoped to `user_id`.

## Bootstrap

[Program.cs](Program.cs): `AddSeqLogging("auth-service")` → `AddOpenTelemetryMetrics(..., "Npgsql")` →
`Startup.ConfigureServices` → **`ApplySqlMigrationsAsync("AuthDb", "auth", RepositoryMigrationPath.ForService("auth"))`**
(миграции применяются до конфигурации pipeline) → `Startup.ConfigurePipeline` → `MapOpenTelemetryEndpoints`.

`public partial class Program;` в конце файла нужен для `WebApplicationFactory<Program>`
в `GrpcAuthServiceIntegrationTests` — **не удалять**.

[Startup.cs](Infrastructure/Startup/Startup.cs): `AddGrpc` + `AddControllers` + `AddHealthChecks` +
`AddAuthInfrastructure(ConnectionStrings:AuthDb)` + `AddAuthApplicationServices` +
`AddProductAnalytics(configuration)`. Pipeline: `UseLoggedExceptionHandler("/error")`,
`MapGrpcService<GrpcAuthService>`, `MapControllers`, `MapHealthChecks("/health")`.

DI-расширения — [AuthServiceExtensions.cs](Application/Extensions/AuthServiceExtensions.cs)
(нетипично: лежат в `Application/Extensions`, а не в `Infrastructure/Startup`). **Пустая строка
`AuthDb` → `InMemoryAuthRepository`** — именно так работает Development (`appsettings.Development.json`
задаёт `""`). Валидатор ключей в обеих ветках один — `DapperAuthApiKeyValidator`; он ходит через
`IAuthRepository`, поэтому корректно работает и поверх in-memory.

## Слои

| Слой | Где | Что |
|---|---|---|
| Domain | `Domain/Entities` | `UserAccount`, `UserSession`, `ApiKey`, `Invite`, `EmailVerification`, `ExternalLogin` — инварианты в сущностях (фабричные методы, мутаторы, `Restore` для гидрации из БД) |
| Domain | `Domain/Enums` | `UserRole` (User/Admin), `UserStatus` (Active / Disabled / **PendingVerification**) |
| Application | `Application/Abstractions` | `IPasswordHasher`, `ISecretHasher` |
| Application | `Application/Services` | `AuthAccountApplicationService` (фасад), `LoginService`, `ExternalLoginService`, `InviteRegistrationService`, `EmailVerificationService`, `ApiKeyFactory`, `IAuthRepository`, `IAuthApiKeyValidator`, `AuthErrorCodes` |
| Infrastructure | `Infrastructure/Grpc` | `GrpcAuthService` — тонкий адаптер gRPC → фасад/сервисы/репозиторий |
| Infrastructure | `Infrastructure/Http` | `HealthController` |
| Infrastructure | `Infrastructure/Persistence` | `*Entity` (Dapper-строки) + `AuthPersistenceMapper`, `AuthDbConnectionFactory` (поверх `NpgsqlDataSource`) |
| Infrastructure | `Infrastructure/Repository` | `AuthRepository` (PG) + `InMemoryAuthRepository`, `DapperAuthApiKeyValidator` |
| Infrastructure | `Infrastructure/Security` | `Pbkdf2PasswordHasher` (пароли), `Sha256SecretHasher` (ключи/токены/коды), `SecretGenerator` |

## Фасад — `AuthAccountApplicationService` ([файл](Application/Services/AuthAccountApplicationService.cs))

Все методы возвращают result-record'ы (`RegisterResult`, `VerifyEmailResult`, `PasswordChangeResult`, …)
с `ErrorCode` из `AuthErrorCodes`; исключения бросают только API-ключевые мутаторы и `UpdateUserAsync`.

- **Self-serve signup** (`RegisterUserAsync`): пароль ≥ `MinPasswordLength` (8) иначе `WeakPassword`;
  email через `EmailValidator.Normalize` (иначе `InvalidEmail`); дубль → `EmailAlreadyRegistered`;
  создаётся `UserAccount.CreateForEmailSignup` в статусе **`PendingVerification`** + `EmailVerification`
  одной транзакцией. Возвращает **сырой 6-значный код** (`RawCode`) — рассылает его WebApp, в БД только хеш.
- **`VerifyEmailAsync`**: код через `EmailVerificationService.Check`; `Confirmed` →
  `ActivateUserAndDeleteVerificationAsync` + `user.Activate()`. Ошибки: `CodeExpired`,
  `TooManyAttempts`, `InvalidCode`, `NoPendingVerification`.
- **`ResendVerificationAsync`**: `Reissue` с кулдауном → `ResendCooldown`, если рано.
- **Инвайты** (admin-путь): `CreateInviteAsync` (в БД хеш токена), `CompleteInviteAsync`
  (резолв по `tokenHash`, `AddUserAndMarkInviteUsedAsync` — создание пользователя и пометка
  инвайта одной транзакцией). Пользователь из инвайта создаётся сразу `Active`.
- **Пароли**: `RequestPasswordResetAsync` (6-значный код в `auth.password_resets`, та же механика
  `EmailVerificationService`; неизвестный email / не-`Active` / аккаунт без пароля →
  `PasswordResetNotAvailable`, WebApp не раскрывает существование аккаунта),
  `ResetPasswordAsync` (`UpdatePasswordAndDeleteResetAsync` — новый хеш + сброс lockout +
  удаление reset-строки атомарно), `ChangePasswordAsync` (проверка текущего пароля).
- **API-ключи**: `CreateApiKeyAsync` требует `UserStatus.Active` (иначе `UserDisabled`) и проверяет
  `currentKeysCount >= user.MaxApiKeys` (иначе `ApiKeyLimitReached`) — **возвращает
  `CreateApiKeyResult.Failed`, не бросает**. `Rename`/`Disable`/`Enable`/`Delete` бросают
  `KeyNotFoundException`, если репозиторий не обновил строку; `GrpcAuthService` ловит и отдаёт
  `ErrorCodes.ValidationFailed`.
- **Продуктовая аналитика** (`IProductAnalytics?`, опционален): `UserVerified` и `ApiKeyCreated`
  эмитятся **fire-and-forget через `Task.Run`, fail-open** — падение аналитики не ломает операцию.

## Логин и сессии

[LoginService.cs](Application/Services/LoginService.cs): `MaxFailedAttempts = 5`,
`LockoutDuration = 15 мин`. Порядок проверок: `Disabled` → `UserDisabled`, `PendingVerification` →
`EmailNotVerified`, `IsLockedOut` → `AccountLocked`, нет `PasswordHash` (внешний логин) →
`InvalidCredentials`. `UserAccount.RegisterFailedLogin` при достижении лимита ставит
`LockedUntilUtc` и **обнуляет счётчик**, так что после снятия блокировки даётся новый набор попыток.

**Счётчики мутируются на сущности, персистит вызывающий**: `GrpcAuthService.Login` сравнивает
`(FailedLoginCount, LockedUntilUtc)` до/после и вызывает `UpdateLoginThrottleAsync` только
при реальном изменении. `IssueSession` минтит сессию без пароля (используется после `VerifyEmail`).

Внешний логин — [ExternalLoginService.cs](Application/Services/ExternalLoginService.cs):
резолв по `(provider, providerSub)`; если нет — линковка к существующему аккаунту по email
(`AddExternalLoginAsync`), иначе `UserAccount.CreateForExternalLogin` + `ExternalLogin`
одной транзакцией. Аналитика `UserVerified` эмитится только на ветке нового аккаунта.

## Секреты и хеши

- **Формат API-ключа**: `sk-or-<base64url(36 random bytes)>` (`SecretGenerator`, separator `-`).
  В БД: видимый `prefix` (первые 12 символов) + SHA-256 `secret_hash` + **`raw_key`**
  (нужен для пуша ключа на ноды 9Router). Plaintext отдаётся вызывающему один раз.
- Сессионный токен: `orr_session_<base64url(32)>`, инвайт-токен: `orr_invite_<base64url(32)>`.
- `Pbkdf2PasswordHasher`: `pbkdf2-sha256.210000.<salt>.<hash>`, `FixedTimeEquals`,
  **`HashPassword` требует ≥ 12 символов** (см. подводные камни).
- `Sha256SecretHasher`: SHA-256 → base64, сравнение `FixedTimeEquals`. Одним и тем же хешером
  хешируются API-ключи, инвайт-токены и 6-значные коды.
- `EmailVerificationService`: код = `RandomNumberGenerator.GetInt32(100000, 1000000)`,
  `CodeTtl` 15 мин, `ResendCooldown` 60 с, `MaxAttempts` 5. `Check` мутирует счётчик попыток
  на сущности — persist делает фасад.
- `ApiKeyFactory.VerifyAndMarkUsed` (`IsActive` + хеш → `MarkUsed`) в проде не используется:
  валидация идёт через `DapperAuthApiKeyValidator` (поиск по `secret_hash` + `MarkApiKeyUsedAsync`).

## Жизненный цикл API-ключа

`Disable` → `Enable` → `Delete` (**не** `Revoke` — переименовано, см. миграцию 009).
`disabled_at_utc is null` = активен; `deleted_at_utc` = soft-delete, ключ скрыт из списка.
`ApiKey.Delete` бросает, если ключ не отключён — **удалить можно только уже disabled-ключ**.
`MarkUsed` на отключённом ключе бросает.

## gRPC-поверхность ([GrpcAuthService.cs](Infrastructure/Grpc/GrpcAuthService.cs))

Контракт — `src/OpenRussiaRouter.Contracts/Protos/auth.proto`, подключён в csproj с
`GrpcServices="Both"`. Группы RPC:

| Группа | RPC |
|---|---|
| Ключи (для gateway) | `ValidateApiKey` |
| Логин | `Login`, `VerifyEmail` (возвращает `LoginResponse`), `LoginOrRegisterWithGoogle`, `LoginOrRegisterWithExternalProvider` |
| Регистрация | `RegisterUser`, `ResendVerification`, `CreateInvite`, `CompleteInvite` |
| Пароли | `ChangePassword`, `RequestPasswordReset`, `ResetPassword` |
| API-ключи | `CreateApiKey`, `RenameApiKey`, `DisableApiKey`, `EnableApiKey`, `DeleteApiKey`, `ListApiKeys` |
| Пользователи | `GetUser`, `UpdateUser`, `ListUsers` |
| Синк ключей на ноды | `GetFirstActiveApiKeyRaw`, `ListAllNonDeletedApiKeysRaw`, `ListAllActiveApiKeysRaw`, `ListActiveApiKeyHashes` |

- `LoginOrRegisterWithGoogle` оставлен для совместимости и делегирует в `ExternalLoginService`
  с провайдером `"google"`. Класс `GoogleAuthService` — **мёртвый код**: в DI не зарегистрирован
  и никем не вызывается.
- Из raw-key RPC реально используется только `ListAllNonDeletedApiKeysRaw`
  (ControlPlane, `GrpcRouterControlPlaneService`) и `GetFirstActiveApiKeyRaw` (WebApp,
  `/admin/gatewaytest`, `/admin/nodes`). `ListAllActiveApiKeysRaw` и `ListActiveApiKeyHashes`
  потребителей в репозитории не имеют.
- `UpdateUser` использует proto-`optional`: незаданные поля берутся из текущего пользователя.

## Репозиторий ([IAuthRepository.cs](Application/Services/IAuthRepository.cs))

Две реализации (PG + in-memory) — **любой новый метод добавлять в обе**. Транзакционные пары
вынесены в отдельные методы: `AddUserAndMarkInviteUsedAsync`, `AddUserWithEmailVerificationAsync`,
`AddUserWithExternalLoginAsync`, `ActivateUserAndDeleteVerificationAsync`,
`UpdatePasswordAndDeleteResetAsync`. Поиск пользователя по email — `where lower(email) = lower(@Email)`.

`InMemoryAuthRepository` хранит **живые доменные объекты**, поэтому мутирующие методы вроде
`UpdateLoginThrottleAsync` там no-op (объект уже изменён). `UpdatePasswordHashAsync` пересобирает
`UserAccount` (`PasswordHash` — get-only).

## Схема БД (`db/migrations/auth/`, 12 файлов)

| Таблица | Что |
|---|---|
| `auth.users` | id, email, role, status, `password_hash` (nullable — внешний логин), `display_name`, `max_api_keys` (default 10), `display_currency` (default `RUB`), `failed_login_count`, `locked_until_utc` |
| `auth.api_keys` | prefix, `secret_hash` (unique), `raw_key`, `last_used_at_utc`, `disabled_at_utc`, `deleted_at_utc` |
| `auth.invites` | `token_hash` (unique), role, expires/used |
| `auth.external_logins` | `(provider, provider_sub)` unique, FK на users |
| `auth.email_verifications` | one-row-per-user (PK = user_id, on delete cascade): `code_hash`, expiry, `sent_at_utc`, `attempts` |
| `auth.password_resets` | та же форма, что `email_verifications` |

Заметные миграции: **002** сеет дефолтного админа `admin@example.com` (фиксированный pbkdf2-хеш);
**009** переименовывает `revoked_at_utc` → `disabled_at_utc` (вместе с предикатом частичного индекса
из 004); **011** добавляет lockout-поля и `password_resets`; **012** заменяет case-sensitive
`ux_auth_users_email` на unique-индекс по `lower(email)`.

## Конфигурация

| Ключ | Заметки |
|---|---|
| `ConnectionStrings:AuthDb` | в `appsettings.json` лежит **только pooling-хвост** без `Host`/`Database` — реальная строка приходит из env (`ConnectionStrings__AuthDb` в compose/kustomize); в Development переопределена в `""` → in-memory |
| `ProductAnalytics:*` | `Enabled`, `Provider`, `IdentitySalt`, `QueueCapacity`, `BatchSize`, … — общий блок из BuildingBlocks/Analytics |
| `Seq__Url`, `Kestrel__EndpointDefaults__Protocols=Http2` | задаются в compose, в appsettings их нет |

`AuthOptions__InitialAdminEmail` / `AuthOptions__InitialAdminPassword` в `deploy/compose/compose.yml`
**кодом не читаются** — класса `AuthOptions` нет, дефолтный админ приходит только из миграции 002.

## Подводные камни

- **Порог длины пароля различается**: фасад пропускает ≥ 8 (`MinPasswordLength`), а
  `Pbkdf2PasswordHasher.HashPassword` бросает `ArgumentException` при < 12. Пароль 8–11 символов
  проходит валидацию фасада и падает в хешере; в `RegisterUser` / `ResetPassword` / `ChangePassword`
  это исключение не перехвачено. WebApp страхует своей валидацией `MinLength(12)`.
- **Сессии не персистятся**: `UserSession` — просто record, токен минтится и уезжает в
  `LoginResponse`. Таблицы сессий нет, RPC валидации/отзыва сессии нет; WebApp кладёт токен
  в claim `session_token` и дальше опирается на свою cookie-аутентификацию.
- **У сервиса нет `ServiceAuthInterceptor`** (в отличие от Billing и Content) — все RPC,
  включая выдающие сырые API-ключи, доступны любому, кто дотянулся до gRPC-порта.
  Защита — только сетевая (compose/NetworkPolicy).
- **Одна сущность `EmailVerification` обслуживает две таблицы** — `email_verifications` и
  `password_resets`. Методы репозитория разные, различай по имени вызова.
- `launchSettings.json` при `dotnet run` поднимает сервис на **5193/7005**, а потребители
  в своих `GrpcServices:Auth` ждут `http://localhost:5001` — для локальной связки задавай порт явно.
- Email нормализуется в конструкторе `UserAccount` (`EmailValidator.Normalize`), поэтому
  сравнивать email в новых запросах нужно уже в нормализованном виде.

## Тесты (`tests/OpenRussiaRouter.AuthService.Tests`)

Unit-тесты поверх `InMemoryAuthRepository` (домен, фасад, signup/reset/external-login, аналитика),
интеграционные — `GrpcAuthServiceIntegrationTests` через `WebApplicationFactory<Program>` (in-memory
репозиторий, реальный gRPC-канал), Postgres-тесты — `PostgresTestDatabase` на Testcontainers
(`postgres:16-alpine`, накатывает все `db/migrations/auth/*.sql`; без Docker `StartAsync` возвращает
`null` и тесты скипаются). **Env-override строки подключения в этом проекте не читается** —
`ORR_AUTH_POSTGRES_TEST_CONNECTION` здесь ни на что не влияет.
