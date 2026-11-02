#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
DUMP=${1:-cafe.dump}
if [ ! -f "$DUMP" ] && [ -f "$ROOT/$DUMP" ]; then
	DUMP="$ROOT/$DUMP"
fi
if [ ! -f "$DUMP" ]; then
	echo "Нет файла дампа: $DUMP" >&2
	exit 1
fi
COMPOSE="docker compose -f $ROOT/docker-compose.prod.yml --env-file $ROOT/.env"
echo "Восстанавливаю $DUMP в postgres…"
$COMPOSE exec -T postgres pg_restore -U cafe -d cafe --clean --if-exists --no-owner --no-acl < "$DUMP"
echo "Готово."
