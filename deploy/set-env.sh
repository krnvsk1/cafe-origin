#!/bin/sh
# Записать настройки в /opt/cafe/.env (файл не в git — git pull его не трогает) и перезапустить API.
#
#   sh deploy/set-env.sh telegram              — спросит токен и имя бота (секрет вебхука создаст сам)
#   sh deploy/set-env.sh КЛЮЧ=значение [...]   — любые переменные, например EXTRA_DOMAINS="xn--... www.xn--..."
#
# Перед изменением делается копия .env.bak-ДАТА. Значения секретов на экран не выводятся.
set -eu
cd "$(dirname "$0")/.."
ENV=.env
[ -f "$ENV" ] || { echo "Нет файла $ENV в $(pwd) — сначала: cp deploy/.env.example .env"; exit 1; }

set_var() {
	key=$1; value=$2
	case "$key" in *[!A-Z0-9_]*|"") echo "Неверное имя переменной: $key"; exit 1 ;; esac
	tmp=$(mktemp)
	# Убираем прежнюю строку с этим ключом и дописываем новую — без sed, чтобы спецсимволы в значении не ломали замену
	grep -v "^${key}=" "$ENV" > "$tmp" || true
	printf '%s=%s\n' "$key" "$value" >> "$tmp"
	cat "$tmp" > "$ENV"
	rm -f "$tmp"
	echo "  $key — записано"
}

ask() {
	# ask ПЕРЕМЕННАЯ "вопрос" [скрыть]
	printf '%s: ' "$2"
	if [ "${3:-}" = hide ]; then stty -echo 2>/dev/null || true; fi
	read -r answer
	if [ "${3:-}" = hide ]; then stty echo 2>/dev/null || true; echo; fi
	eval "$1=\$answer"
}

[ $# -gt 0 ] || { sed -n '2,6p' "$0"; exit 1; }
cp "$ENV" "$ENV.bak-$(date +%Y%m%d-%H%M%S)"
chmod 600 "$ENV" "$ENV".bak-* 2>/dev/null || true

if [ "$1" = telegram ]; then
	echo "Telegram (для «Войти через Telegram»): токен и имя бота — из @BotFather"
	ask token "Токен бота (123456:ABC…)" hide
	ask username "Имя бота без @"
	[ -n "$token" ] || { echo "Токен пустой — ничего не меняю"; exit 1; }
	set_var TELEGRAM_BOT_TOKEN "$token"
	set_var TELEGRAM_BOT_USERNAME "${username#@}"
	if ! grep -q '^TELEGRAM_WEBHOOK_SECRET=.\+' "$ENV"; then
		set_var TELEGRAM_WEBHOOK_SECRET "$(openssl rand -hex 16)"
	fi
else
	for pair in "$@"; do
		case "$pair" in *=*) set_var "${pair%%=*}" "${pair#*=}" ;; *) echo "Ожидается КЛЮЧ=значение: $pair"; exit 1 ;; esac
	done
fi

echo "Перезапускаю API и веб-сервер с новыми настройками…"
docker compose -f docker-compose.prod.yml up -d api caddy
echo "Готово. Лог API: docker compose -f docker-compose.prod.yml logs --tail 50 api"
