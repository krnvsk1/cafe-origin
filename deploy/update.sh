#!/bin/sh
# Обновление на сервере: sh /opt/cafe/deploy/update.sh — свежий main, пересборка, база и фото сохраняются
set -eu
cd "$(dirname "$0")/.."
git pull --ff-only
docker compose -f docker-compose.prod.yml up -d --build
docker image prune -f >/dev/null
docker compose -f docker-compose.prod.yml ps
