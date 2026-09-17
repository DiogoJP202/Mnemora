#!/bin/sh
set -u

api_pid=""
web_pid=""

stop_processes() {
  trap - INT TERM HUP

  if [ -n "$web_pid" ] && kill -0 "$web_pid" 2>/dev/null; then
    kill -TERM "$web_pid" 2>/dev/null || true
  fi

  if [ -n "$api_pid" ] && kill -0 "$api_pid" 2>/dev/null; then
    kill -TERM "$api_pid" 2>/dev/null || true
  fi

  [ -z "$web_pid" ] || wait "$web_pid" 2>/dev/null || true
  [ -z "$api_pid" ] || wait "$api_pid" 2>/dev/null || true
}

fail() {
  printf '%s\n' "$1" >&2
  stop_processes
  exit 1
}

trap 'stop_processes; exit 143' TERM
trap 'stop_processes; exit 130' INT
trap 'stop_processes; exit 129' HUP

export HOSTNAME=0.0.0.0
export PORT="${PORT:-3000}"
export ASPNETCORE_URLS=http://127.0.0.1:5100
export ConnectionStrings__Default="${ConnectionStrings__Default:-Data Source=/var/data/mnemora.db}"

if [ -z "${Mnemora__WebOrigin:-}" ] && [ -n "${RENDER_EXTERNAL_URL:-}" ]; then
  export Mnemora__WebOrigin="$RENDER_EXTERNAL_URL"
fi

dotnet /app/api/Mnemora.Api.dll --seed &
api_pid=$!

attempt=0
until node -e "fetch('http://127.0.0.1:5100/api/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"; do
  if ! kill -0 "$api_pid" 2>/dev/null; then
    wait "$api_pid"
    fail "A API encerrou antes de ficar pronta."
  fi

  attempt=$((attempt + 1))
  if [ "$attempt" -ge 180 ]; then
    fail "A API não ficou pronta dentro de 180 segundos."
  fi

  sleep 1
done

node /app/web/server.js &
web_pid=$!

while :; do
  if ! kill -0 "$api_pid" 2>/dev/null; then
    status=0
    wait "$api_pid" || status=$?
    stop_processes
    printf 'A API encerrou com status %s.\n' "$status" >&2
    [ "$status" -ne 0 ] || status=1
    exit "$status"
  fi

  if ! kill -0 "$web_pid" 2>/dev/null; then
    status=0
    wait "$web_pid" || status=$?
    stop_processes
    printf 'O frontend encerrou com status %s.\n' "$status" >&2
    [ "$status" -ne 0 ] || status=1
    exit "$status"
  fi

  sleep 1
done
