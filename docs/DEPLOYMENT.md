# Deployment and operations runbook

How to run FELCRA Cattle Profiling on a Linux VPS: first installation, updates, backups and restore,
monitoring. Everything runs in Docker; the files are in [`deploy/`](../deploy).

```
                 Internet (443)
                       │
              ┌────────┴────────┐  deploy/proxy   (one per server)
              │ Caddy           │  HTTPS certificates from Let's Encrypt, renewed automatically
              └───┬─────────┬───┘
     felcra-production-app   felcra-staging-app        docker network "felcra-web"
   ┌──────────────┴───┐   ┌──┴───────────────┐
   │ app  (API + both │   │ app              │  deploy/app   (one stack per environment)
   │ web apps)        │   │                  │
   │ db   PostgreSQL  │   │ db               │  not reachable from outside
   │ backup (nightly) │   │ backup           │
   └──────────────────┘   └──────────────────┘
```

## 1. Server preparation (once)

Ubuntu 24.04 LTS, 2 vCPU / 4 GB RAM / 40 GB disk is enough for the pilot (one farm, tens of users).

```bash
# Updates and a firewall: only SSH and web traffic.
sudo apt update && sudo apt upgrade -y
sudo apt install -y unattended-upgrades ufw git
sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw allow 443/udp && sudo ufw enable

# Docker Engine + Compose plugin (https://docs.docker.com/engine/install/ubuntu/)
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER   # sign out and in again

# Application files (no source build needed on the server)
sudo mkdir -p /srv/felcra && sudo chown $USER /srv/felcra
git clone <repository-url> /srv/felcra/app
mkdir -p /srv/felcra/backups/production /srv/felcra/backups/staging
```

Use SSH keys only (`PasswordAuthentication no` in `/etc/ssh/sshd_config`).

**DNS:** point `profiling.felcra.com.my` (and `staging.profiling.felcra.com.my`) to the server's IP address
before starting the proxy, so Let's Encrypt can issue the certificates.

## 2. Reverse proxy (once)

```bash
cd /srv/felcra/app/deploy/proxy
docker network create felcra-web
cp sites/production.caddy.example sites/production.caddy   # set the domain
cp sites/staging.caddy.example sites/staging.caddy         # optional
ACME_EMAIL=it@felcra.com.my docker compose -p felcra-proxy up -d
```

Put `ACME_EMAIL=...` in `deploy/proxy/.env` so later restarts pick it up.

## 3. First deployment of a stack

```bash
cd /srv/felcra/app/deploy/app
cp env.example production.env
chmod 600 production.env
```

Fill in `production.env`:

| Setting | How |
|---|---|
| `APP_IMAGE` / `APP_TAG` | `ghcr.io/<github-owner>/felcra-profiling` and a released version, e.g. `1.0.0` |
| `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY` | `openssl rand -base64 48` each. Never reuse between staging and production |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | First HQ admin, created only while there are no users. Sign in and change the password, then you may remove the password from the file |
| `PUSH_PUBLIC_KEY`, `PUSH_PRIVATE_KEY` | `docker run --rm ghcr.io/<owner>/felcra-profiling:<tag> vapid-keys`. Keep them: new keys make every phone turn notifications on again |
| `EMAIL_FROM`, `SMTP_*`, `MONTHLY_REPORTS=true` | FELCRA's mail server, for the monthly report e-mail |
| `BACKUP_DIR` | `/srv/felcra/backups/production` |
| `SEED_DEMO_DATA` | `false` in production; `true` on staging gives the Pagoh demo farm and sample accounts |

If the image is private on GitHub, sign in once: `docker login ghcr.io -u <user>` with a token that can read packages.

```bash
cd /srv/felcra/app/deploy
./deploy.sh production 1.0.0
```

The script takes a backup (when a database already exists), pulls the image, starts the stack (database
migrations run automatically at start-up) and waits until the API reports healthy. Then open
`https://profiling.felcra.com.my/admin/`.

> On the very first start the log shows one "Failed executing DbCommand … __ef_migrations_history" error:
> that is the database being checked before its first migration and is expected.

## 4. Updating

Releases are built by GitHub Actions ([`release.yml`](../.github/workflows/release.yml)):
every push to `main` publishes `:main` (for staging); a tag `v1.2.3` publishes `:1.2.3` and `:latest`.

```bash
cd /srv/felcra/app && git pull            # only needed when deploy/ files changed
cd deploy && ./deploy.sh staging main     # try it on staging first
./deploy.sh production 1.2.3
```

**Rollback:** `./deploy.sh production <previous-version>`. If the newer version had changed the database
(see the release notes), restore the backup the deploy script took just before the upgrade (section 6).

## 5. Backups

The `backup` service writes, every night at 02:00 (Malaysia time):

- `db-YYYYMMDD-HHMMSS.dump` — the whole database (PostgreSQL custom format)
- `photos-YYYYMMDD-HHMMSS.tar.gz` — uploaded photos

into `BACKUP_DIR`, keeping `BACKUP_KEEP_DAYS` (14) days. A backup on demand:

```bash
cd /srv/felcra/app/deploy/app
docker compose -p felcra-production --env-file production.env exec backup /bin/sh /backup.sh now
```

**Off-site copy (required for production):** backups on the same server do not survive losing the server.
Create `deploy/app/rclone.conf` with a remote named `offsite` (S3-compatible storage, Google Drive,
another server over SFTP — `docker run --rm -it -v $PWD:/config/rclone rclone/rclone:1.68.2 config`), set
`OFFSITE_TARGET`, and start the copier:

```bash
docker compose -p felcra-production --env-file production.env --profile offsite up -d offsite
```

It copies new backups every 6 hours. Check the far side once a month.

## 6. Restore

```bash
cd /srv/felcra/app/deploy
./restore.sh production /srv/felcra/backups/production/db-20261012-020000.dump \
                        /srv/felcra/backups/production/photos-20261012-020000.tar.gz
```

It asks for the stack name as confirmation, stops the API, replaces the database and photos, and starts the
API again. Users stay signed in only if the backup still contains their sessions; otherwise they sign in again.
Records made on phones after the backup and not yet synced are sent again by the phones automatically.

**Test a restore every 3 months** — on staging: copy a production backup there and restore it.

## 7. Monitoring and logs

| What | Where |
|---|---|
| Health | `https://<domain>/health` returns 200 when the API and database are working. Add it to an uptime monitor (e.g. UptimeRobot) with e-mail alerts |
| Container state | `docker compose -p felcra-production --env-file production.env ps` (the app shows `healthy`) |
| Application log | `docker compose ... logs -f app`, and daily files in the `appdata` volume: `docker compose ... exec app ls /app/data/logs` (kept 30 days) |
| Proxy log / certificates | `docker compose -p felcra-proxy logs caddy` |
| Disk | `df -h` and `du -sh /srv/felcra/backups/*` — backups and photos grow over time |

## 8. Settings reference

The stack passes `deploy/app/*.env` values to the API as environment variables (`Section__Key`). See the
configuration table in the [README](../README.md#configuration). Production defaults are in
`src/Felcra.Profiling.Api/appsettings.Production.json` (data under `/app/data`, migrations at start-up,
trusting the reverse proxy's `X-Forwarded-*` headers).

## 9. Trying the production stack on a laptop

```bash
docker build -t felcra-profiling:local .
docker network create felcra-web
cp deploy/proxy/sites/local.caddy.example deploy/proxy/sites/local.caddy
(cd deploy/proxy && ACME_EMAIL=admin@felcra.local HTTP_PORT=8088 HTTPS_PORT=8443 docker compose -p felcra-proxy up -d)
cp deploy/app/env.example deploy/app/local.env   # STACK_NAME=felcra-local, APP_IMAGE=felcra-profiling, APP_TAG=local, secrets
SKIP_PULL=1 deploy/deploy.sh local
```

Open `https://localhost:8443/admin/` (Caddy's local certificate: accept the browser warning, or trust it with
`docker compose -p felcra-proxy exec caddy caddy trust`).

## 10. Using an existing nginx instead of Caddy

On a server that already runs nginx (other sites on ports 80/443), skip the Caddy proxy (section 2) and let nginx
forward to the app:

```bash
cd /srv/felcra/app/deploy/app
cp nginx.override.yml compose.override.yml        # publishes the app on 127.0.0.1:8080 only
docker network create felcra-web 2>/dev/null || true
SKIP_PULL=1 ../deploy.sh production               # or ../deploy.sh production <tag>
curl -s http://127.0.0.1:8080/health              # must answer: Healthy

sudo cp ../nginx/felcra.conf.example /etc/nginx/sites-available/felcra   # set the domain and certificate paths
sudo ln -s /etc/nginx/sites-available/felcra /etc/nginx/sites-enabled/
sudo certbot --nginx -d <domain>                  # if the site has no certificate yet
sudo nginx -t && sudo systemctl reload nginx
```

The nginx site must pass `Host`, `X-Forwarded-For` and `X-Forwarded-Proto` (as in the example) and allow 25 MB
uploads. If port 8080 is taken on the server, set `APP_PORT=<free port>` in the env file and use that port in
`proxy_pass`.

## 11. Troubleshooting

**502 Bad Gateway** — the proxy is running but cannot reach the app:

| Check | Command | Expected |
|---|---|---|
| Containers running | `docker compose -p felcra-production --env-file production.env ps` | `app` and `db` *healthy* |
| App started without errors | `docker compose -p felcra-production --env-file production.env logs --tail 100 app` | "Now listening on: http://[::]:8080", no exception |
| App answers on the server | `curl -s http://127.0.0.1:8080/health` (nginx route) | `Healthy` |
| What nginx says | `sudo tail -n 30 /var/log/nginx/error.log` | "Connection refused … upstream" = nothing on that port (no `compose.override.yml`, wrong port, or app down) |
| nginx forwards to the right place | `sudo nginx -T \| grep -B2 -A12 <domain>` | `proxy_pass http://127.0.0.1:8080;` |

Common causes: the app container is not published to the host (nginx route without `compose.override.yml`); the app
exits at start because a setting is missing (`JWT_SIGNING_KEY` shorter than 32 characters, wrong
`POSTGRES_PASSWORD` after the database was first created — see the log); `proxy_pass` points to another port.
