#!/bin/sh
set -eu
mkdir -p /backups
# Первый дамп через час после старта, дальше раз в сутки. Хранить 14 дней.
sleep 3600
while true; do
	file="/backups/cafe-$(date +%Y%m%d-%H%M).dump"
	pg_dump -h postgres -U "${POSTGRES_USER:-cafe}" -d "${POSTGRES_DB:-cafe}" -Fc -f "$file"
	find /backups -name 'cafe-*.dump' -mtime +14 -delete
	sleep 86400
done
