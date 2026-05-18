# Default Admin Seed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an idempotent auth migration that creates `admin@example.com` with password `StrongPassword123!`.

**Architecture:** The seed lives in SQL so it runs wherever existing auth migrations run. The migration inserts only when the email is absent, preserving any existing admin account.

**Tech Stack:** .NET 8, PostgreSQL SQL migrations, xUnit migration file tests.

---

### Task 1: Add Default Admin Migration

**Files:**
- Create: `db/migrations/auth/002_seed_default_admin.sql`

- [ ] **Step 1: Create SQL migration**

```sql
insert into auth.users (id, email, role, status, password_hash, created_at_utc)
select
    'user-admin-default',
    'admin@example.com',
    'Admin',
    'Active',
    'pbkdf2-sha256.210000.T3BlblJ1c3NpYUFkbWluIQ==.VmZIzhJ+iIemWRUCr8NvIaTH5owirvHAm0R4Ip3Yrhk=',
    now()
where not exists (
    select 1
    from auth.users
    where lower(email) = 'admin@example.com'
);
```

- [ ] **Step 2: Apply to local database**

Run:

```powershell
kubectl -n orr-dev exec -i deploy/postgres -- env PGPASSWORD=CHANGE_ME_IN_PRODUCTION psql -U orr -d orr_auth < db/migrations/auth/002_seed_default_admin.sql
```

Expected: `INSERT 0 1` on an empty auth database, or `INSERT 0 0` when the admin already exists.

### Task 2: Keep Compose Migration List Current

**Files:**
- Modify: `docker-compose.yml`

- [ ] **Step 1: Add the new auth migration after `001_initial.sql`**

```yaml
psql -h postgres -U postgres -d openrussiarouter -f /migrations/auth/001_initial.sql &&
psql -h postgres -U postgres -d openrussiarouter -f /migrations/auth/002_seed_default_admin.sql &&
psql -h postgres -U postgres -d openrussiarouter -f /migrations/billing/001_initial.sql &&
```

- [ ] **Step 2: Validate compose syntax**

Run:

```powershell
docker compose config
```

Expected: command exits successfully and includes `/migrations/auth/002_seed_default_admin.sql`.

### Task 3: Add Migration File Test

**Files:**
- Modify: `tests/OpenRussiaRouter.BuildingBlocks.Tests/Migrations/SqlMigrationFilesTests.cs`

- [ ] **Step 1: Add a test for the seed migration**

```csharp
[Fact]
public void AuthSeedAdminMigration_ExistsAndDoesNotStoreRawPassword()
{
    var sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "db", "migrations", "auth", "002_seed_default_admin.sql"));

    Assert.Contains("admin@example.com", sql, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("pbkdf2-sha256.210000.", sql, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("StrongPassword123!", sql, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run focused tests**

Run:

```powershell
dotnet test tests/OpenRussiaRouter.BuildingBlocks.Tests --filter SqlMigrationFilesTests -nr:false -m:1
```

Expected: all `SqlMigrationFilesTests` pass.

### Task 4: Verify Login

**Files:**
- No file changes.

- [ ] **Step 1: Restart AuthService or apply SQL manually**

Run:

```powershell
kubectl -n orr-dev rollout restart deploy/auth-service
kubectl -n orr-dev rollout status deploy/auth-service --timeout=120s
```

Expected: AuthService restarts successfully.

- [ ] **Step 2: Verify user exists**

Run:

```powershell
kubectl -n orr-dev exec deploy/postgres -- env PGPASSWORD=CHANGE_ME_IN_PRODUCTION psql -U orr -d orr_auth -At -F "|" -c "select email, role, status from auth.users where lower(email) = 'admin@example.com';"
```

Expected:

```text
admin@example.com|Admin|Active
```
