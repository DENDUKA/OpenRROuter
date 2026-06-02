# Email integration plan — invite delivery + broadcast

**Status:** planned, not started. Drafted 2026-06-02 (from the VPN project, hence saved here
so we continue *in this repo*). Reusable patterns live in the global Claude skill
`~/.claude/skills/email-broadcast/` (auto-loads in any project).

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
