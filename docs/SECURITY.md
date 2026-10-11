# Security

How FELCRA Cattle Profiling addresses the [OWASP Top 10 (2021)](https://owasp.org/Top10/), and what remains
for go-live. Automated checks are in
[`SecurityTests.cs`](../tests/Felcra.Profiling.Api.IntegrationTests/SecurityTests.cs) and run on every build.

## OWASP Top 10 checklist

| Risk | Controls | Verified by |
|---|---|---|
| **A01 Broken access control** | Every API route requires a signed-in user unless explicitly anonymous (only sign-in, refresh and sign-out). Roles: HQ admin, farm manager, field officer, enforced per endpoint. Farm managers and officers only see their assigned farms: EF Core query filters apply to every farm-scoped table, so other farms' records are "not found". Users can only remove their own push subscriptions. | `Every_api_route_except_sign_in_requires_a_token` (calls every route), `Roles_limit_what_each_user_can_do`, `Users_cannot_reach_animals_on_farms_they_are_not_assigned_to`, report and push tests |
| **A02 Cryptographic failures** | HTTPS only (Caddy with Let's Encrypt, HSTS 180 days). Passwords hashed by ASP.NET Core Identity (PBKDF2). Refresh tokens stored only as SHA-256 hashes. JWT signing key ≥ 32 characters, validated at start-up, supplied per environment. Database, SMTP and push keys only in the server's env file (`chmod 600`). | Start-up validation; deployment runbook |
| **A03 Injection** | All database access through EF Core (parameterised). Blazor encodes all output; e-mail HTML is encoded. CSV/Excel import parses values, never formulas. | Code review |
| **A04 Insecure design** | Offline records carry client IDs, so a replayed send is stored once. Reuse of a rotated refresh token is treated as theft and ends all of the user's sessions. Destructive actions (delete animal, restore) need HQ or explicit confirmation. Monthly reports and notifications are claimed per period so they cannot be sent twice. | `Refresh_rotates_token_and_reuse_revokes_all_sessions`, offline sync tests |
| **A05 Security misconfiguration** | Strict Content-Security-Policy (scripts only from the site, no inline scripts, no frames), `X-Frame-Options: DENY`, `nosniff`, `Permissions-Policy` (camera only for this site), `no-store` on API answers, no `Server` header. API explorer only in development. Errors return problem details without stack traces. Fonts are self-hosted (no third-party requests). The container runs as a non-root user. | `Responses_carry_security_headers`, `Malformed_requests_get_a_problem_without_internal_details`, browser suites fail on any CSP violation |
| **A06 Vulnerable components** | .NET 10 LTS, central package versions (`Directory.Packages.props`), only maintained MIT/Apache/OFL libraries. | Run `dotnet list package --vulnerable` each release; update the base images monthly (`deploy.sh` pulls the rebuilt image) |
| **A07 Identification & authentication failures** | Password policy (8+ characters, upper/lower case, digit). Lockout for 15 minutes after 5 wrong passwords (also when changing the password). Sign-in rate limit per IP (10/minute). Same answer for unknown accounts and wrong passwords. Short access tokens (15 minutes), rotating refresh tokens. Users change their own password, which signs out their other devices; HQ can reset passwords and deactivate users, which ends their sessions. | `Sign_in_does_not_reveal_which_accounts_exist`, `Users_change_their_own_password_and_other_sessions_end`, `Forged_or_unsigned_tokens_are_rejected`, auth tests |
| **A08 Software & data integrity failures** | Images are built by GitHub Actions from the repository and pulled by version tag. Uploaded photos are accepted only when their content is a JPEG, PNG or WebP image (by file signature), max 8 MB. | Record tests |
| **A09 Logging & monitoring failures** | Every change to master data, animals, records and users is written to the audit log (who, when, old/new values); password changes are logged without the password. Request logs and daily log files (30 days). `/health` for uptime monitoring. | Audit log tests; runbook §7 |
| **A10 Server-side request forgery** | The server only calls out to SMTP (configured) and Web Push endpoints; push endpoints must be absolute `https` URLs and are only used for the subscribing user's own device. | `Only_https_push_endpoints_are_accepted` |

Also: a general API rate limit per user (600 requests/minute, `RateLimiting:ApiRequestsPerMinute`) and a 25 MB
request limit at the proxy.

## Before go-live

1. **Independent penetration test** of the staging site (the automated checks above are a baseline, not a substitute).
2. Run `dotnet list package --vulnerable --include-transitive` and update anything reported.
3. Production secrets generated on the server (`openssl rand -base64 48`), never copied from staging.
4. Off-site backups enabled and a restore tested ([DEPLOYMENT.md](DEPLOYMENT.md) §5–6).
5. SSH: keys only, firewall allowing 22/80/443 only, unattended security upgrades on.
6. Uptime monitor on `/health` with alerts to FELCRA IT.

## Known limitations

- No two-factor sign-in. Worth adding for HQ admins if FELCRA's policy requires it.
- Data on phones (IndexedDB) is protected by the phone's own lock screen, not encrypted by the app. Signing out
  removes it; lost phones should be reported so HQ can deactivate the user (which ends their sessions).
- `style-src 'unsafe-inline'` is needed by the UI component library (MudBlazor). Scripts remain strictly limited.

## Reporting a problem

Report suspected vulnerabilities privately to FELCRA IT (not in a public issue), with steps to reproduce.
