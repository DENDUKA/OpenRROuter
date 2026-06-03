# Email integration plan — invite delivery + broadcast

**Status:** ✅ **implemented 2026-06-03** (code complete, builds, 6 unit tests green; the live
SMTP smoke test + secret provisioning are the only manual steps left — see "Implemented"
below). Drafted 2026-06-02 (from the VPN project, hence saved here so we continue *in this
repo*). Reusable patterns live in the global Claude skill `~/.claude/skills/email-broadcast/`.

**Decisions confirmed on resume:** scope = **A + B**; broadcast UI = **dedicated
`/admin/broadcast` page**; Brevo key = **shared with the VPN bot for now**.

## Decisions so far
- **Landing service: WebApp** ✅ (confirmed) — it has the admin UI, gRPC clients, knows its
  own base URL for `/invite/{token}`, and is an HTTP host where the background worker fits.
- **Provider: Brevo SMTP.** Sender `noreply@web-point.top`. **Shared SMTP key with the VPN
  bot for now** (split into a dedicated key later if needed).
- **No git commits by the agent** — user reviews/commits.

## Provider is already configured (done 2026-06-02)
`web-point.top` is **authenticated in Brevo** (DKIM + DMARC), so any `@web-point.top` sender
is DKIM-signed and lands in inbox, not spam. DNS lives in **Cloudflare** (registrar Porkbun,
NS delegated to Cloudflare):
- TXT `@` → `brevo-code:…`
- CNAME `brevo1._domainkey` / `brevo2._domainkey` → `b{1,2}.web-point-top.dkim.brevo.com` — **DNS only** (grey cloud)
- TXT `_dmarc` → `v=DMARC1; p=none; rua=mailto:rua@dmarc.brevo.com`

SMTP connection:
- Host `smtp-relay.brevo.com`, port `587` (STARTTLS).
- Username (Brevo SMTP login) `a734bb001@smtp-brevo.com`.
- Password = **Brevo SMTP key** — **NOT stored in this repo.** Pull from the Brevo SMTP page,
  or reuse the one in the VPN bot's user-secrets.

(Re-auth walkthrough, if ever needed: `~/.claude/skills/email-broadcast/references/brevo-setup.md`.)

## Why the plan is shaped this way (project facts)
- .NET 8 microservices, **Dapper + Npgsql (no EF Core)**, raw SQL migrations in
  `db/migrations/{service}/`, gRPC, repository pattern (Dapper + in-memory impls).
- Registration is **invite-based** (admin creates an invite token) — there is **no**
  self-serve email-code verification, so the skill's 6-digit code flow does **not** apply here.
- **No email sending exists anywhere yet.**
- Documented gap (CLAUDE.md → "Не готово"): *"Invite delivery — token only in admin create
  response, no email."* This plan closes it.
- `AuthAccountApplicationService.CreateInviteAsync` returns `CreatedInvite(Invite, RawToken)`;
  the WebApp admin page (`/admin/users`) currently just displays the token.
- `ListUsersAsync` already exists (AuthService gRPC) for the broadcast recipient list. Every
  user has an email by construction (invite), so **no "verified email" filter is needed**.

## What ports from the skill vs. what's custom
| Piece | Action |
|-------|--------|
| `IEmailSender` + `EmailMessage`/`EmailAttachment`, `SmtpEmailSender` (MailKit), `EmailSettings`, `EmailDispatchQueue`+iface, `EmailDispatchWorker` | **Drop-in** from `skills/email-broadcast/templates/dotnet/` → rename namespace to `OpenRussiaRouter.WebApp.Email` |
| 6-digit verification flow | **Skip** — invites instead |
| Broadcast service | Port, **simplified** (no verified-email filter) |

## Implementation steps
1. **NuGet:** add `MailKit` to `OpenRussiaRouter.WebApp`.
2. **Copy infra** into `src/OpenRussiaRouter.WebApp/Email/`, namespace `OpenRussiaRouter.WebApp.Email`.
3. **DI** — add an `AddEmailInfrastructure(this IServiceCollection, IConfiguration)` extension
   (match the repo's `Infrastructure/Startup/` convention), wired from `Program.cs`:
   - `Configure<EmailSettings>(config.GetSection("Email"))`
   - singleton `EmailDispatchQueue` + `IEmailDispatchQueue` alias
   - `AddHostedService<EmailDispatchWorker>()`
   - scoped `IEmailSender` → `SmtpEmailSender`
   - the broadcast service + invite-email helper.
4. **Config** — `Email` section in WebApp `appsettings.json` (host/port/sender/tuning,
   placeholders only). Secrets via env: `Email__SmtpUsername`, `Email__SmtpPassword`.
   - Local: `dotnet user-secrets set "Email:SmtpPassword" "…"`.
   - **k8s:** put creds in a Secret (e.g. `email-secret`) + a **kustomize env-patch** — do
     NOT use `kubectl set env` (ArgoCD `selfHeal: true` reverts it in ~1–2 min).
   - `Email__SenderAddress=noreply@web-point.top`.
5. **(A) Invite email** — closes the documented gap. In the create-invite handler
   (`Pages/Admin/Users.cshtml.cs`), after the gRPC call returns `rawToken`, build
   `{scheme}://{host}/invite/{rawToken}` and enqueue an `EmailMessage` to `invite.Email`
   (subject + link). Keep showing the token in the admin UI as a fallback.
6. **(B) Broadcast** — admin action: `ListUsers` (AuthService gRPC) → build a per-user
   `EmailMessage` → `EnqueueBatchAsync`. Small admin UI: subject/body input, recipient count,
   confirm, "send". Worker throttles to ~1 msg/s.
7. **Tests** — fake `IEmailSender`: invite enqueues the correct link; worker retry/backoff
   (3 attempts, 1/2/4 s) then discard; broadcast enqueues N messages.
8. **Smoke** — create an invite to your own address → link arrives in **Inbox**, From =
   `@web-point.top`; check Brevo → Logs (delivered).

## Open decisions to confirm on resume
1. **Scope:** both A (invite email) + B (broadcast), or B only? *(Recommend both — shared infra,
   A is a documented gap.)*
2. **Broadcast UI:** a dedicated `/admin/broadcast` page, or a button on `/admin/users`?
3. **Brevo key:** keep shared with VPN, or generate a dedicated SMTP key for this project now?

## Pointers
- Skill: `~/.claude/skills/email-broadcast/` — `SKILL.md` (design), `references/dotnet.md`
  (wiring + adaptable verification/broadcast code), `references/brevo-setup.md` (provider),
  `templates/dotnet/` (copy-paste infra).

## Implemented (2026-06-03)

**Code — all under WebApp, namespace `OpenRussiaRouter.WebApp.Email`:**
- `Email/` — `IEmailSender`+`EmailMessage`/`EmailAttachment`, `SmtpEmailSender` (MailKit),
  `EmailSettings`, `IEmailDispatchQueue`+`EmailDispatchQueue` (bounded channel),
  `EmailDispatchWorker` (sequential drain, 3 attempts @ 1/2/4 s, throttle), `EmailTemplates`
  (invite builder), `EmailBroadcastService`, `EmailServiceCollectionExtensions`
  (`AddEmailInfrastructure`).
- `Program.cs` — `builder.Services.AddEmailInfrastructure(builder.Configuration)`.
- `appsettings.json` — `Email` section (host/port/sender/`BatchDelayMs`; **no secrets**).
- **(A)** `Pages/Admin/Users.cshtml.cs` — after `CreateInviteAsync`, enqueues
  `EmailTemplates.InviteEmail(email, link)`; token still shown as fallback (enqueue failure
  is swallowed + logged).
- **(B)** `Pages/Admin/Broadcast.cshtml(.cs)` — compose → preview (recipient count) → send;
  nav link added to `_AdminLayout.cshtml`. Recipients via AuthService `ListUsers` (no
  verified-email filter — every user has an email by construction).
- Pkg: `MailKit 4.17.0` (central `Directory.Packages.props` + WebApp `.csproj`).

**Tests:** `tests/OpenRussiaRouter.WebApp.Tests/` (added to the .sln) — invite link builder,
broadcast `BuildMessages` (N msgs, skips blanks), queue enqueue/batch, worker retry→discard→
continue. `dotnet test` → **6 passed**.

**Secrets (NOT in repo — `SmtpUsername`/`SmtpPassword` read from env/secrets at runtime):**
- Local: `dotnet user-secrets set "Email:SmtpUsername" "a734bb001@smtp-brevo.com"` and
  `dotnet user-secrets set "Email:SmtpPassword" "<brevo-smtp-key>"` (run in
  `src/OpenRussiaRouter.WebApp`).
- **k8s (dev):** `infra/.../overlays/dev/services/webapp-email-patch.yaml` injects
  `Email__SmtpUsername`/`Email__SmtpPassword` from Secret **`webapp-email-secret`** (keys
  `smtp-username` / `smtp-password`). Create it manually (selfHeal-safe), mirroring
  `webapp-google-secret`/`f9router-secret`:

  ```bash
  kubectl create secret generic webapp-email-secret -n orr-dev \
    --from-literal=smtp-username='a734bb001@smtp-brevo.com' \
    --from-literal=smtp-password='<brevo-smtp-key>'
  ```

- **Compose VPS** (`/opt/orr/`, box 31.76.49.2): server compose is
  `deploy/compose/compose.yml` (NOT the repo-root `docker-compose.yml`, which is for local dev).
  Its webapp passes `Email__SmtpUsername`/`Email__SmtpPassword` from
  `${EMAIL_SMTP_USERNAME}`/`${EMAIL_SMTP_PASSWORD}`; fill those in **`/opt/orr/.env`** (which
  lives one level *above* the `app/` git clone, so it's outside any repo — see
  `deploy/compose/.env.example`). Then `./update.sh orr` (git pull → rebuild → restart).

**Manual smoke (left to do):** set the secret, create an invite to your own address → the
`/invite/{token}` link should arrive in **Inbox** (From `@web-point.top`); confirm in Brevo →
Logs (delivered). Then `/admin/broadcast` → send a test to all users.

**Note:** in-memory queue → messages still queued at shutdown are lost (acceptable for invite/
broadcast; matches the documented BILLING-004 outbox gap pattern if durability is wanted later).

## Follow-up: self-serve signup with email verification (2026-06-03)

**Why:** the landing "Signup" button pointed at a fake `/invite/demo` token — there was no
public registration, only admin invites. Decision (user): drop invite-only as the *primary*
path and add a real signup (email + password → confirm email → done). Invite mechanism
**kept** as an admin tool. Verification method = **6-digit code** (no public base-URL needed,
works on IP-only/compose deploys; reuses the same SMTP infra above). Build green, **+14
unit tests** (AuthService 64, WebApp 7).

**Flow:** `/signup` (email + password ≥12) → AuthService `RegisterUser` creates a
`PendingVerification` user + a hashed 6-digit code (raw code returned to WebApp, mailed via
`EmailTemplates.VerificationEmail`) → redirect to `/verify-email?email=` → `VerifyEmail`
confirms the code, activates the account, and issues a session (auto-login) → workspace.
`Resend` re-issues a code (60 s cooldown). Login of an unconfirmed account returns
`email_not_verified` and bounces to `/verify-email`.

**Code:**

- AuthService — `UserStatus.PendingVerification`; `UserAccount.CreateForEmailSignup/Activate/
  Restore`; `EmailVerification` entity; `EmailVerificationService` (15-min TTL, 5 attempts,
  60 s resend cooldown; SHA-256 of the code, never the code); `AuthAccountApplicationService`
  `RegisterUserAsync`/`VerifyEmailAsync`/`ResendVerificationAsync` (+ result records);
  `LoginService` blocks pending + `IssueSession` helper; 4 new `IAuthRepository` methods
  (InMemory + Dapper + the test double).
- proto — `RegisterUser`, `VerifyEmail` (→ `LoginResponse`), `ResendVerification`.
- DB — `db/migrations/auth/006_add_email_verifications.sql` (auto-applied on startup; auth now
  at 6).
- WebApp — `Pages/Signup`, `Pages/VerifyEmail`; `AuthProvider` Register/VerifyEmail/Resend;
  `EmailTemplates.VerificationEmail`; landing "Signup" → `/signup`; `/login` ↔ `/signup` links.

**⚠ Requires the Email SMTP secret to be set** (same `webapp-email-secret` / `EMAIL_SMTP_*`
as above). Without it the code email never sends, so signup creates the pending account but the
user gets no code — set the secret before this is usable end-to-end.

**Manual smoke (left to do):** deploy → open `/`, click Signup, register your own address →
6-digit code arrives in Inbox (From `@web-point.top`) → enter it on `/verify-email` → lands in
the workspace logged in. Also: wrong code, expired code, and "Resend".
