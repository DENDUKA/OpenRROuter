# Single-host Docker Compose deployment (dev)

Runs the full stack — ApiGateway, AuthService, BillingService, ControlPlane,
CollectorService, WebApp, Postgres, Seq, and the f9router node (`Inc64/F9Router`
fork) — on one host via Docker Compose. This is the **dev** topology; production
uses the k8s/Kustomize/ArgoCD setup under `infra/`.

## Layout on the server (`/opt/orr/`)

```
/opt/orr/
  app/              git clone of DENDUKA/OpenRussiaRouter  (build context)
  f9router/         git clone of Inc64/F9Router            (build context)
  compose.yml       this file
  .env              real secrets (NOT in git)
  update.sh update-orr.sh update-9router.sh bootstrap-git.sh
```

The build contexts (`./app`, `./f9router`) are git clones, so updating = `git pull` +
rebuild. The compose file and `.env` live one level above them and are not part of
either clone.

## Prerequisites

- Ubuntu 24.04 x86_64, Docker Engine + Compose plugin, git.
- Per-repo **deploy keys** (read-only) so the host can clone the private repos. Add the
  public keys in each repo's *Settings → Deploy keys*, and configure `~/.ssh/config`:

  ```
  Host github-orr
    HostName github.com
    User git
    IdentityFile /root/.ssh/orr_deploy_key
    IdentitiesOnly yes
  Host github-f9
    HostName github.com
    User git
    IdentityFile /root/.ssh/f9_deploy_key
    IdentitiesOnly yes
  ```

## First deploy

```bash
mkdir -p /opt/orr && cd /opt/orr
# put compose.yml + the *.sh scripts here, and a filled-in .env (see .env.example)
cp .env.example .env && $EDITOR .env
./bootstrap-git.sh          # clones app + f9router at main, builds, starts
```

After it's up, register the f9router node in the admin UI
(`/admin/nodes` → Add Node, endpoint `http://f9router:20128/`, Control Token =
`ORR_NODE_CONTROL_TOKEN`).

## Updating

```bash
./update.sh          # both products
./update.sh orr      # only OpenRussiaRouter (.NET services)
./update.sh 9r       # only f9router
```

Each script: `git pull --ff-only` → rebuild only its services → restart →
health-gate → **rollback** to the previous commit if health fails.

## Access (IP-only, plain HTTP — dev)

| Service | URL |
|---|---|
| API gateway | `http://<ip>:8080` |
| WebApp | `http://<ip>:3000` |
| Collector gRPC | `<ip>:5005` |
| Collector health | `http://<ip>:5006/health` |
| Seq | `http://<ip>:5341` |
| f9router | `http://<ip>:20128` |

For production add a domain + reverse proxy (Caddy) with TLS and set
`F9_PUBLIC_URL=https://…` plus f9router `AUTH_COOKIE_SECURE=true`.

## Notes / gotchas baked into compose.yml

- gRPC between services needs HTTP/2 cleartext. Auth/billing/control use HTTP/2
  on their service port; Collector has separate HTTP/1 health and HTTP/2 gRPC
  endpoints. Without HTTP/2, calls fail with `HTTP_1_1_REQUIRED`.
- No standalone `migrate` container: the .NET services apply SQL migrations
  idempotently on startup (`schema_migrations`), so a separate runner only causes
  non-idempotent re-run failures.
- Npgsql connection strings use `Connection Idle Lifetime` (not the invalid
  `ConnectionIdleTimeout`).
- `datalust/seq:latest` requires `SEQ_FIRSTRUN_ADMINPASSWORD`.
