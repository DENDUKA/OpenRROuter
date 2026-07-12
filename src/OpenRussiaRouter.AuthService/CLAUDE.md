# AuthService — CLAUDE.md

Учётные записи, сессии, жизненный цикл API-ключей, email-верификация, инвайты, внешний (Google) логин. **gRPC-сервер (порт 5001)** + HTTP `/health`. Layered: Domain → Application → Infrastructure.

## Регистрация

`Program.cs` → `Startup.ConfigureServices` ([Startup.cs](Infrastructure/Startup/Startup.cs)): `AddGrpc` + `AddControllers` (для `/health`) + `AddAuthInfrastructure(ConnectionStrings:AuthDb)` + `AddAuthApplicationServices`. Pipeline мапит `GrpcAuthService` и `/health`. Если строка подключения пустая — поднимается in-memory репозиторий.

## Слои

| Слой | Где | Что |
|---|---|---|
| Domain | `Domain/Entities` | `UserAccount`, `UserSession`, `ApiKey`, `Invite`, `EmailVerification`, `ExternalLogin` — вся инвариантная логика в сущностях (фабричные методы, мутаторы) |
| Domain | `Domain/Enums` | `UserRole` (User/Admin), `UserStatus` (Active / **PendingVerification** / …) |
| Application | `Application/Services` | `AuthAccountApplicationService` (фасад), `LoginService`, `InviteRegistrationService`, `EmailVerificationService`, `GoogleAuthService`, `ApiKeyFactory`, `AuthApiKeyValidator`, `IAuthRepository`, `AuthErrorCodes` |
| Infrastructure | `Infrastructure/Grpc` | `GrpcAuthService` — тонкий адаптер gRPC → фасад |
| Infrastructure | `Infrastructure/Persistence` | Dapper-entity + `AuthPersistenceMapper`, `AuthDbConnectionFactory` |
| Infrastructure | `Infrastructure/Repository` | `AuthRepository` (PG) + `InMemoryAuthRepository`; `AuthApiKeyValidator` + in-memory |
| Infrastructure | `Infrastructure/Security` | `Pbkdf2PasswordHasher` (пароли), `Sha256SecretHasher` (ключи/токены/коды), `SecretGenerator` (`ISecretGenerator`) |

## Фасад — `AuthAccountApplicationService` ([файл](Application/Services/AuthAccountApplicationService.cs))

Ключевые операции и инварианты:

- **Self-serve signup** (`RegisterUserAsync`): пароль ≥ `MinPasswordLength` (8) иначе `WeakPassword`; email нормализуется (`EmailValidator.Normalize`); дубль email → `EmailAlreadyRegistered`; создаётся `UserAccount.CreateForEmailSignup` в статусе **`PendingVerification`**, плюс `EmailVerification`. Возвращает **сырой 6-значный код** (`RawCode`) — его рассылает WebApp; в БД только хеш.
- **VerifyEmailAsync**: проверяет код через `EmailVerificationService.Check`; на `Confirmed` → `ActivateUserAndDeleteVerificationAsync` + `user.Activate()`. Коды ошибок: `CodeExpired`, `TooManyAttempts`, `InvalidCode`, `NoPendingVerification`.
- **ResendVerificationAsync**: `Reissue` с кулдауном → `ResendCooldown` если рано.
- **Инвайты** (admin-путь): `CreateInviteAsync` (хеш токена в БД), `CompleteInviteAsync` (резолв по `tokenHash`, создание пользователя + пометка инвайта использованным одной операцией).
- **Пароли**: `RequestPasswordResetAsync` (6-значный код в `auth.password_resets`, та же механика `EmailVerificationService`; неизвестный email / внешний логин → `PasswordResetNotAvailable`, WebApp не раскрывает существование аккаунта), `ResetPasswordAsync` (код → новый хеш + сброс lockout + удаление reset-строки атомарно), `ChangePasswordAsync` (проверка текущего пароля). **Login lockout**: `LoginService` — 5 неудачных попыток → блокировка на 15 минут (`AccountLocked`); счётчики мутируются на `UserAccount`, персистит вызывающий через `UpdateLoginThrottleAsync` (см. `GrpcAuthService.Login`).
- **API-ключи**: `CreateApiKeyAsync` требует `UserStatus.Active` и проверяет лимит `currentKeysCount >= user.MaxApiKeys` (бросает `InvalidOperationException`). `Rename`/`Revoke`/`Delete` бросают `KeyNotFoundException`, если не нашли. `Delete` работает только над уже отозванным ключом.

## Безопасность ([ApiKeyFactory.cs](Application/Services/ApiKeyFactory.cs), [EmailVerificationService.cs](Application/Services/EmailVerificationService.cs))

- **Формат ключа**: `sk-or-<36 случайных, разделитель '-'>`. В БД: видимый `prefix` (12 символов) + SHA-256 `SecretHash` + `RawKey` (для пуша на ноды). Plaintext отдаётся вызывающему один раз.
- **`ApiKeyFactory.VerifyAndMarkUsed`**: проверяет `IsActive` + хеш, при успехе `MarkUsed`.
- **`EmailVerificationService`**: код = `RandomNumberGenerator.GetInt32(100000,1000000)`; TTL 15 мин (`CodeTtl`), кулдаун ресенда 60с (`ResendCooldown`), макс. 5 попыток (`MaxAttempts`) — счётчик мутируется на сущности, persist делает фасад. Хешируется тем же `Sha256SecretHasher`.

## Репозиторий ([IAuthRepository.cs](Application/Services/IAuthRepository.cs))

Две реализации (PG + in-memory) — **любой новый метод добавлять в обе**. Транзакционные пары вынесены в отдельные методы: `AddUserAndMarkInviteUsedAsync`, `AddUserWithEmailVerificationAsync`, `AddUserWithExternalLoginAsync`, `ActivateUserAndDeleteVerificationAsync`. Для интеграции с нодами есть `ListAllActiveApiKeysRawAsync` (raw-ключи для bulk-sync из ControlPlane), `ListActiveApiKeyHashesAsync`, `FindActiveApiKeyBySecretHashAsync`.

## БД и тесты

- Миграции: `db/migrations/auth/` (6 файлов), применяются на старте через `ApplySqlMigrationsAsync`.
- Тесты: unit + integration + PG (Testcontainers). Override коннекта — `ORR_AUTH_POSTGRES_TEST_CONNECTION` (обычно не нужен).
