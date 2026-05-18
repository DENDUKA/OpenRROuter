# Default Admin Seed Design

## Goal

Ensure development and deployed environments always have an initial `admin@example.com` account when the auth database is empty or lacks that email.

## Approach

Add a new auth SQL migration, `002_seed_default_admin.sql`, that inserts `admin@example.com` as an active `Admin` user only when no user with that email exists case-insensitively.

The default password is `StrongPassword123!`. The migration stores only a PBKDF2-SHA256 hash in the existing AuthService format:

`pbkdf2-sha256.210000.<salt>.<hash>`

Existing accounts are not modified. This avoids overwriting a real admin's password or status after the first boot.

## Files

- `db/migrations/auth/002_seed_default_admin.sql`: idempotent seed migration.
- `docker-compose.yml`: include the new SQL file in the manual compose migration step.
- `tests/OpenRussiaRouter.BuildingBlocks.Tests/Migrations/SqlMigrationFilesTests.cs`: verify the seed migration exists and does not store the raw password.

## Verification

Run the migration tests and apply the new SQL against the current local `orr_auth` database. Confirm `admin@example.com` exists and login succeeds with `StrongPassword123!`.
