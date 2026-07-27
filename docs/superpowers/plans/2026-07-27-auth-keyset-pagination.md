# T-009 Auth Keyset Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make PostgreSQL user and API-key pagination follow the same descending `created_at_utc, id` ordering as the response.

**Architecture:** Retain the existing id-only cursor API. Resolve its timestamp in a scalar subquery scoped to the same table (and user for API keys), then apply a tuple comparison before the existing order by.

**Tech Stack:** .NET 8, Dapper, PostgreSQL, xUnit, FluentAssertions, Testcontainers.

---

### Task 1: Add PostgreSQL page-boundary regression tests

**Files:**
- Modify: `tests/OpenRussiaRouter.AuthService.Tests/Infrastructure/Persistence/AuthRepositoryPostgresTests.cs`

- [ ] Seed at least three users with intentionally non-monotonic ids and ascending creation timestamps; fetch page one with limit two and page two with the last page-one id. Assert concatenated ids equal the expected descending creation order with no duplicate.
- [ ] Seed at least three API keys for one user with the same pattern and perform the equivalent two-page assertion.
- [ ] Run the focused tests before changing SQL; expected failure is missing or duplicated rows under the id-only cursor.

### Task 2: Align both PostgreSQL cursors with their order keys

**Files:**
- Modify: `src/OpenRussiaRouter.AuthService/Infrastructure/Repository/AuthRepository.cs`

- [ ] Replace `id > @AfterUserId` with a tuple predicate equivalent to:

```sql
and (created_at_utc, id) < (
    select created_at_utc, id
    from auth.users
    where id = @AfterUserId
)
```

- [ ] Replace the API-key id predicate with the same tuple comparison against `auth.api_keys`, scoped by `id = @AfterApiKeyId and user_id = @UserId`.
- [ ] Keep `order by created_at_utc desc, id desc`, existing deleted filtering, public method signatures and in-memory semantics unchanged.

### Task 3: Verify and close

**Files:**
- Modify: `.opencode/tasks.md`
- Modify: `src/OpenRussiaRouter.AuthService/CLAUDE.md` only if its pagination documentation needs an invariant update.

- [ ] Run `dotnet test tests/OpenRussiaRouter.AuthService.Tests --no-restore -nr:false -m:1` and `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`.
- [ ] Mark T-009 done with the actual ISO-8601 completion time and commit only implementation, tests, necessary docs and tasks with `fix(auth): align keyset pagination with sort order`.

