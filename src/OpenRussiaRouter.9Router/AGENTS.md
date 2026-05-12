# Repository Guidelines

## Project Overview

This directory contains the 9Router web dashboard and local AI routing gateway.
It is a Next.js app that exposes an OpenAI-compatible `/v1/*` API, dashboard
management routes, provider credential management, request translation,
fallback routing, usage tracking, and optional cloud sync.

## Key Directories

- `src/app`: Next.js App Router pages and API routes.
- `src/app/api/v1` and `src/app/api/v1beta`: OpenAI/Claude-compatible API surface.
- `src/app/api`: dashboard, auth, providers, usage, tunnel, CLI helper, and settings APIs.
- `src/sse`: local SSE handlers that bridge API routes into the routing core.
- `open-sse`: provider executors, translators, RTK filters, streaming utilities, and core routing logic.
- `src/lib`: local persistence, auth, tunnel, network proxy, updater, DB, and runtime init helpers.
- `src/shared`: shared UI components, constants, hooks, and service helpers.
- `src/store`: Zustand client-side stores.
- `cloud`: Cloudflare Worker companion implementation and migrations.
- `gitbook`: documentation site.
- `tests`: Vitest unit tests for selected core/cloud behavior.
- `skills`: packaged 9Router skills.

## Setup

The source package is private (`9router-app`), so local development should run
from this repository rather than global npm package commands.

For a fresh checkout:

```powershell
Copy-Item .env.example .env
npm install
```

Current init note: `.env` has been created, but `npm install` was interrupted
before `node_modules` or a lockfile appeared. Run `npm install` before building
or starting the app.

## Development Commands

```powershell
npm run dev
```

The dev script starts Next.js on port `20128`. The dashboard is normally at:

- `http://localhost:20128/dashboard`
- `http://localhost:20128/v1` for OpenAI-compatible API clients

If you need explicit environment variables in PowerShell:

```powershell
$env:PORT = "20128"
$env:NEXT_PUBLIC_BASE_URL = "http://localhost:20128"
npm run dev
```

The npm `build` and `start` scripts use POSIX-style inline env assignment
(`NODE_ENV=production ...`). On Windows PowerShell, prefer either Git Bash/WSL
for those scripts or run the equivalent commands with PowerShell env variables:

```powershell
$env:NODE_ENV = "production"
npx next build --webpack

$env:NODE_ENV = "production"
$env:PORT = "20128"
$env:HOSTNAME = "0.0.0.0"
$env:NEXT_PUBLIC_BASE_URL = "http://localhost:20128"
npx next start
```

## Tests

Tests live in `tests/` and use Vitest. The test README documents a separate
Vitest install path:

```powershell
Set-Location tests
npm test
```

If local dependencies are missing for the test package, follow
`tests/README.md`.

## Architecture Notes

- Next rewrites in `next.config.mjs` map `/v1/*` to `/api/v1/*`.
- Chat request flow usually starts in `src/app/api/v1/chat/completions/route.js`,
  moves through `src/sse/handlers/chat.js`, then into `open-sse/handlers/chatCore.js`.
- Provider-specific calls are implemented in `open-sse/executors/*`.
- Request and response translation lives under `open-sse/translator/*`.
- Local state is primarily handled by `src/lib/localDb.js` and newer DB helpers
  under `src/lib/db/*`.
- Runtime initialization lives in `src/lib/initCloudSync.js`,
  `src/lib/network/initOutboundProxy.js`, and `src/shared/services/initializeApp.js`.

## Coding Conventions

- This codebase is JavaScript-first; follow the existing module style in the
  file you edit.
- Prefer existing helpers and constants from `src/shared`, `src/lib`, and
  `open-sse` before adding new utilities.
- Keep route handlers thin when possible; put routing/provider logic in the
  existing service/core layers.
- Preserve compatibility behavior for `/v1/*` clients and streaming responses.
- Avoid broad refactors while changing provider routing, translation, auth,
  persistence, or usage tracking; these paths have cross-cutting effects.

## Safety Notes

- Do not commit real secrets from `.env` or local provider credentials.
- Be careful with local runtime data under `DATA_DIR` or `~/.9router`; it may
  contain provider connections, API keys, usage logs, and request history.
- The surrounding git repository may include files outside this directory.
  Keep edits scoped to this app unless the user explicitly asks otherwise.
