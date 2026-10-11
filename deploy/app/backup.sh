#!/bin/sh
# Nightly backup of one stack: PostgreSQL dump (custom format, for pg_restore) and the photos folder.
# Runs in the "backup" service; "backup.sh now" makes one backup immediately (used before upgrades).
set -eu
BACKUP_HOUR="${BACKUP_HOUR:-2}"
BACKUP_KEEP_DAYS="${BACKUP_KEEP_DAYS:-14}"

backup() {
    stamp=$(date +%Y%m%d-%H%M%S)
    # Write to a temporary name first so a half-written file is never mistaken for a backup.
    pg_dump --format=custom --no-owner --file="/backups/db-$stamp.dump.tmp"
    mv "/backups/db-$stamp.dump.tmp" "/backups/db-$stamp.dump"
    if [ -d /app/data/uploads ]; then
        tar -czf "/backups/photos-$stamp.tar.gz.tmp" -C /app/data uploads
        mv "/backups/photos-$stamp.tar.gz.tmp" "/backups/photos-$stamp.tar.gz"
    fi
    find /backups -maxdepth 1 \( -name 'db-*.dump' -o -name 'photos-*.tar.gz' \) -mtime +"$BACKUP_KEEP_DAYS" -delete
    echo "$(date '+%F %T') backup $stamp done ($(du -h "/backups/db-$stamp.dump" | cut -f1) database)"
}

if [ "${1:-}" = "now" ]; then
    backup
    exit 0
fi

echo "Backups daily at $(printf %02d "$BACKUP_HOUR"):00, kept $BACKUP_KEEP_DAYS days"
while true; do
    now=$(date +%s)
    next=$(date -d "$(date +%Y-%m-%d) $(printf %02d "$BACKUP_HOUR"):00:00" +%s)
    [ "$next" -gt "$now" ] || next=$((next + 86400))
    sleep $((next - now))
    backup || echo "$(date '+%F %T') backup FAILED"
done
