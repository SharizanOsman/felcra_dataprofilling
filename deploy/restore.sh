#!/usr/bin/env bash
# Restores a backup into a stack (replaces its database and photos):
#   ./restore.sh production /srv/felcra/backups/production/db-20261012-020000.dump [photos-20261012-020000.tar.gz]
set -euo pipefail
stack="${1:?usage: restore.sh <stack> <db-*.dump> [photos-*.tar.gz]}"
dump="$(realpath "${2:?database dump required}")"
photos="${3:+$(realpath "$3")}"
# Check the files before anything is stopped or dropped.
[[ -f "$dump" ]] || { echo "Not found: $dump"; exit 1; }
[[ -z "$photos" || -f "$photos" ]] || { echo "Not found: $photos"; exit 1; }
cd "$(dirname "$0")/app"
dc() { docker compose -p "felcra-$stack" --env-file "$stack.env" "$@"; }

read -r -p "This replaces all data of felcra-$stack with $(basename "$dump"). Type the stack name to continue: " answer
[[ "$answer" == "$stack" ]] || { echo "Cancelled."; exit 1; }

echo "== Stopping the API"
dc stop app

echo "== Restoring the database"
dc exec -T db sh -c 'dropdb -U felcra --if-exists --force felcra && createdb -U felcra felcra'
dc exec -T db pg_restore -U felcra -d felcra --no-owner --exit-on-error < "$dump"

if [[ -n "$photos" ]]; then
    echo "== Restoring photos"
    # The API runs as user 1654 (APP_UID of the .NET images).
    docker run --rm -i -v "felcra-${stack}_appdata:/data" alpine:3.20 \
        sh -c 'rm -rf /data/uploads && tar -xzf - -C /data && chown -R 1654:1654 /data/uploads' < "$photos"
fi

echo "== Starting the API"
dc start app
echo "Restored. Check the portal, then sign in to confirm."
