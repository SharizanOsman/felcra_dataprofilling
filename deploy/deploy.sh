#!/usr/bin/env bash
# Deploys or updates one stack on the server:   ./deploy.sh production [image-tag]
# Takes a backup first (database migrations run when the new version starts), then waits until healthy.
set -euo pipefail
stack="${1:?usage: deploy.sh <production|staging> [tag]}"
cd "$(dirname "$0")/app"
env_file="$stack.env"
[[ -f "$env_file" ]] || { echo "Missing deploy/app/$env_file (copy env.example)"; exit 1; }
[[ -n "${2:-}" ]] && export APP_TAG="$2"
dc() { docker compose -p "felcra-$stack" --env-file "$env_file" "$@"; }

docker network inspect felcra-web >/dev/null 2>&1 || docker network create felcra-web

if [[ -n "$(dc ps -q db 2>/dev/null)" ]]; then
    echo "== Backup before upgrade"
    dc exec -T backup /bin/sh /backup.sh now
fi

echo "== Pull and start"
[[ "${SKIP_PULL:-}" == 1 ]] || dc pull app   # SKIP_PULL=1: use a locally built image
dc up -d --remove-orphans

echo "== Waiting for the API to report healthy"
for _ in $(seq 1 60); do
    status=$(docker inspect --format '{{.State.Health.Status}}' "$(dc ps -q app)" 2>/dev/null || true)
    if [[ "$status" == "healthy" ]]; then
        echo "felcra-$stack is up ($(docker inspect --format '{{.Config.Image}}' "$(dc ps -q app)"))"
        exit 0
    fi
    sleep 5
done
echo "The API did not become healthy. Recent logs:"
dc logs --tail 80 app
exit 1
