#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
port="$(node "$REPO_DIR/scripts/find-free-port.mjs")"
test_log="$(mktemp /private/tmp/partygame-manual-script-test.XXXXXX)"
cleanup() {
  if [[ -n "${test_pid:-}" ]] && kill -0 "$test_pid" 2>/dev/null; then kill -TERM "$test_pid" 2>/dev/null || true; fi
  rm -f "$test_log"
}
trap cleanup EXIT INT TERM

python3 -m http.server "$port" --bind 127.0.0.1 > "$test_log" 2>&1 &
test_pid=$!
deadline=$((SECONDS + 8))
until lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; do
  (( SECONDS < deadline )) || { echo "fixture did not start" >&2; exit 1; }
  sleep 1
done

output="$($REPO_DIR/scripts/manual-down.sh --port "$port")"
[[ "$output" == *"PID: $test_pid"* ]]
[[ "$output" == *"PartyGame stopped."* ]]
[[ "$output" == *"Port $port is free."* ]]
! lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1

output="$($REPO_DIR/scripts/manual-down.sh --port "$port")"
[[ "$output" == *"PartyGame is not running."* ]]

host_output="$(PARTYGAME_MANUAL_HOST=192.168.50.20 bash -c 'source "$1/scripts/lib/manual-common.sh"; manual_detect_host' _ "$REPO_DIR")"
[[ "$host_output" == "LAN IP: 192.168.50.20" ]]

for script in manual-up.sh manual-down.sh manual-restart.sh open-ios.sh lib/manual-common.sh; do bash -n "$REPO_DIR/scripts/$script"; done
echo "Manual script tests PASS"
