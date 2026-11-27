# Deployment and operations runbook

How to run FFISB LIVESTOCK 360 on a Linux VPS: first installation, updates, backups and restore,
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
On Hostinger, see [section 12](#12-hosting-on-a-hostinger-vps) first.

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

**DNS:** point `ffisblivestock360.com` (and `staging.ffisblivestock360.com`) to the server's IP address
before starting the proxy, so Let's Encrypt can issue the certificates.

## 2. Reverse proxy (once)

```bash
cd /srv/felcra/app/deploy/proxy
docker network create felcra-web
cp sites/production.caddy.example sites/production.caddy   # set the domain
cp sites/staging.caddy.example sites/staging.caddy         # optional
ACME_EMAIL=it@ffisblivestock360.com docker compose -p felcra-proxy up -d
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
`https://ffisblivestock360.com/admin/`.

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

**Outbound internet for the farm map.** The server calls `https://nominatim.openstreetmap.org` for place search on
the farm map, and users' browsers load map images from `https://tile.openstreetmap.org` (street) and
`https://server.arcgisonline.com` (satellite); the Content-Security-Policy allows exactly these. If a firewall
blocks them, the map shows no background and place search reports that it is unavailable, but farms can still be
placed by typing GPS coordinates. Set `Geocoding__Email` to an IT contact address so the OpenStreetMap operators can
reach FELCRA before blocking heavy use; for high volumes run your own Nominatim and set `Geocoding__BaseUrl`.

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

## 12. Hosting on a Hostinger VPS

Hostinger's Web/Shared and Cloud hosting plans run PHP sites only: they cannot run .NET, PostgreSQL or
Docker. Use a **Hostinger VPS**; after the hPanel steps below, sections 1–7 apply unchanged.

### In hPanel

| Setting | Choice |
|---|---|
| Plan | **KVM 2** (2 vCPU, 8 GB RAM) or larger. KVM 1 is too small to build the image on the server |
| Data centre | The one closest to Malaysia that is offered (e.g. Singapore) |
| Operating system | **Ubuntu 24.04**. The "Ubuntu 24.04 with Docker" template also works; then skip the Docker install in section 1 |
| SSH key | Add your public key (`ssh-keygen -t ed25519`, then the contents of `~/.ssh/id_ed25519.pub`) and avoid password logins |
| VPS firewall | If enabled, allow only 22/tcp, 80/tcp, 443/tcp and 443/udp. `ufw` on the server (section 1) does the same |
| Backups | Turn on the weekly VPS backup or take snapshots. This is in addition to the nightly backups and off-site copy (section 5), not instead of them |

The VPS starts with only `root`. Create a normal user before section 1 and work as that user:

```bash
ssh root@<vps-ip>
adduser felcra && usermod -aG sudo felcra
rsync -a ~/.ssh /home/felcra/ && chown -R felcra:felcra /home/felcra/.ssh
exit
ssh felcra@<vps-ip>
```

### DNS

The production address is `https://ffisblivestock360.com`. The site root opens the admin app (`/admin/`), and
officers use `/field/`.

Before starting the proxy (section 2), open hPanel → **Domains** → `ffisblivestock360.com` →
**DNS / Nameservers**. The domain must use Hostinger's nameservers. Add these records:

| Type | Name | Points to |
|---|---|---|
| A | `@` | VPS IP address |
| A | `www` | VPS IP address. `production.caddy.example` redirects it to the main address |
| A | `staging` | VPS IP address (only if a staging stack runs on the same VPS) |

Delete any existing `A`, `AAAA` or `CNAME` records for `@` and `www` that Hostinger created for domain parking or
its website builder. An `AAAA` record that points elsewhere is the usual reason Let's Encrypt fails.

Check with `nslookup ffisblivestock360.com` and `nslookup www.ffisblivestock360.com`. Both must return the VPS IP
before Let's Encrypt can issue the certificates; DNS changes can take a few hours to show.

### Getting the application image

- **From GitHub Container Registry (preferred).** Every push to `main` publishes `:main` and every `v*` tag
  publishes a version (section 4). Set `APP_IMAGE=ghcr.io/sharizanosman/felcra-profiling` (all lowercase). For a
  private repository, create a GitHub personal access token with `repo` and `read:packages`, use it as the password
  for `git clone`, and run `docker login ghcr.io -u <github-user>` with the same token.
- **Built on the VPS.** If no published image is available:

  ```bash
  cd /srv/felcra/app && docker build -t felcra-profiling:local .   # about 5–10 minutes on KVM 2
  # production.env: APP_IMAGE=felcra-profiling, APP_TAG=local
  SKIP_PULL=1 deploy/deploy.sh production
  ```

  To update, run `git pull`, then rebuild and deploy with the same commands.

### E-mail through Hostinger

To send the monthly report e-mail from a Hostinger mailbox, set the following in `production.env`:

- `SMTP_HOST=smtp.hostinger.com`
- `SMTP_PORT=465`
- `SMTP_USER` and `SMTP_PASSWORD`: the full mailbox address and its password
- `EMAIL_FROM`: the same address
- `MONTHLY_REPORTS=true`

The API works out the TLS mode from the port, so 465 and 587 both work.
