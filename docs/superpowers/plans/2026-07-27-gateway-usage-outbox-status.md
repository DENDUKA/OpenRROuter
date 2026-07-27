# T-005 Gateway Usage Outbox Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permit ApiGateway to persist `usage_missing` usage events without weakening the existing status constraint.

**Architecture:** Keep the outbox C# contract unchanged. Add an immutable third gateway migration that replaces the named `CHECK` constraint with the same four historic statuses plus `usage_missing`; make the PostgreSQL repository tests apply every gateway migration and prove each allowed status persists.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Dapper, Npgsql, PostgreSQL Testcontainers, raw SQL migrations.

---

### Task 1: Add a regression test that applies the complete gateway schema

**Files:**
- Modify: `tests/OpenRussiaRouter.ApiGateway.Tests/Billing/PostgresUsageOutboxRepositoryTests.cs:29-49, 213-224`

- [ ] **Step 1: Replace the one-file test migration helper with a deterministic complete-migration helper**

Replace each `_database.ApplyMigration();` call with `_database.ApplyMigrations();`. Replace the helper body with:

```csharp
public void ApplyMigrations()
{
    using var connection = new NpgsqlConnection(ConnectionString);
    var migrationDirectory = Path.Combine(
        FindRepositoryRoot(),
        "db",
        "migrations",
        "gateway");

    foreach (var migrationPath in Directory.EnumerateFiles(migrationDirectory, "*.sql")
                 .OrderBy(static path => path, StringComparer.Ordinal))
    {
        connection.Execute(File.ReadAllText(migrationPath));
    }
}
```

- [ ] **Step 2: Add a parameterized failing regression test for all supported statuses**

Add next to `EnqueueAsync_Idempotent_DoesNotDuplicate`:

```csharp
[Theory]
[Trait("Category", "PostgreSQL")]
[InlineData("pending")]
[InlineData("processing")]
[InlineData("completed")]
[InlineData("failed")]
[InlineData("usage_missing")]
public async Task EnqueueAsync_AllowedStatus_Persists(string status)
{
    if (_database is null)
    {
        return;
    }

    _database.ApplyMigrations();
    var repo = CreateRepository();
    await repo.EnqueueAsync(CreateEvent("req-" + status) with { Status = status }, CancellationToken.None);

    using var connection = new NpgsqlConnection(_database.ConnectionString);
    var persisted = await connection.ExecuteScalarAsync<string>(
        "select status from gateway.usage_outbox where request_id = @RequestId;",
        new { RequestId = "req-" + status });

    persisted.Should().Be(status);
}
```

- [ ] **Step 3: Run the narrow test before adding the migration**

Run:

```powershell
dotnet test tests/OpenRussiaRouter.ApiGateway.Tests --no-restore -nr:false -m:1 --filter "FullyQualifiedName~PostgresUsageOutboxRepositoryTests.EnqueueAsync_AllowedStatus_Persists"
```

Expected: the `usage_missing` case fails because `003_usage_outbox_allow_usage_missing.sql` does not yet exist (or because the current constraint rejects the value); the four historic statuses pass.

### Task 2: Extend the status constraint with an immutable migration

**Files:**
- Create: `db/migrations/gateway/003_usage_outbox_allow_usage_missing.sql`

- [ ] **Step 1: Add the new migration without editing `002_usage_outbox_status_check.sql`**

Create the file with:

```sql
-- Keep the outbox schema aligned with handlers that record a response without token usage.
ALTER TABLE gateway.usage_outbox
    DROP CONSTRAINT ck_usage_outbox_status;

ALTER TABLE gateway.usage_outbox
    ADD CONSTRAINT ck_usage_outbox_status
    CHECK (status IN ('pending', 'processing', 'completed', 'failed', 'usage_missing'));
```

- [ ] **Step 2: Re-run the focused regression test**

Run the command from Task 1, Step 3.

Expected: all five theory cases pass. If Docker is unavailable, the existing test fixture skips its assertions; report that limitation and still run the non-container test suite.

- [ ] **Step 3: Run the ApiGateway test project and solution build**

Run:

```powershell
dotnet test tests/OpenRussiaRouter.ApiGateway.Tests --no-restore -nr:false -m:1
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: both commands exit with code 0 and no warnings.

- [ ] **Step 4: Update task status and commit the implementation**

Set `T-005` in `.opencode/tasks.md` to `done` with the current ISO-8601 completion time, then run:

```powershell
git add -- db/migrations/gateway/003_usage_outbox_allow_usage_missing.sql tests/OpenRussiaRouter.ApiGateway.Tests/Billing/PostgresUsageOutboxRepositoryTests.cs .opencode/tasks.md
git commit -m "fix(gateway): persist usage-missing outbox events"
```

Do not change ApiGateway runtime code or previous migration files: the handlers already emit the required status.

