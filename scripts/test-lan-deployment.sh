#!/usr/bin/env bash
# Exercises deployment only under a throw-away root. It never uses a user installation.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"

release_dir=""
host=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --release-dir) release_dir="${2:-}"; shift 2 ;;
    --host) host="${2:-}"; shift 2 ;;
    *) echo "Usage: $0 --release-dir <release> [--host <private-ip>]" >&2; exit 64 ;;
  esac
done
[[ -n "$release_dir" && -d "$release_dir" ]] || { echo "--release-dir must name a release artifact" >&2; exit 64; }
if [[ -z "$host" ]]; then
  candidates="$(lan_detect_private_ipv4s | sort -u)"
  [[ "$(printf '%s\n' "$candidates" | sed '/^$/d' | wc -l | tr -d ' ')" == 1 ]] || { echo "pass --host when LAN address is absent or ambiguous" >&2; exit 64; }
  host="$candidates"
fi
lan_is_private_ipv4 "$host" || { echo "--host must be private IPv4" >&2; exit 64; }

port="$(node "$SCRIPT_DIR/find-free-port.mjs")"
test_root="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-lan-test.XXXXXX")"
runtime_marker="$test_root/runtime/preserved-marker"
cleanup() {
  "$SCRIPT_DIR/stop-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port" >/dev/null 2>&1 || true
  chmod -R u+w "$test_root" 2>/dev/null || true
  rm -rf "$test_root"
}
trap cleanup EXIT INT TERM

# Pure helper checks, including the public-address exclusions.
lan_is_private_ipv4 10.2.3.4
lan_is_private_ipv4 172.16.0.1
lan_is_private_ipv4 192.168.1.1
! lan_is_private_ipv4 172.32.0.1
! lan_is_private_ipv4 127.0.0.1
! lan_is_private_ipv4 0.0.0.0

# A byte-corrupt artifact must be rejected before current is written.
bad="$test_root/bad-release"
mkdir -p "$bad"
cp -R "$release_dir/." "$bad/"
printf 'corrupt' >> "$bad/api/PartyGame.Api.dll"
if "$SCRIPT_DIR/deploy-lan.sh" --deploy-root "$test_root" --release-dir "$bad" --host "$host" --port "$port"; then
  echo "corrupt artifact was accepted" >&2; exit 1
fi
[[ ! -e "$test_root/current" ]]
rm -rf "$bad"

"$SCRIPT_DIR/deploy-lan.sh" --deploy-root "$test_root" --release-dir "$release_dir" --host "$host" --port "$port"
"$SCRIPT_DIR/start-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port" # idempotent second start
"$SCRIPT_DIR/status-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port"
"$SCRIPT_DIR/smoke-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port"
touch "$runtime_marker"

# Obcy/stary PID is diagnosed and never signalled by stop/status.
"$SCRIPT_DIR/stop-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port"
printf '%s\n' "$$" > "$test_root/runtime/pid/partygame-api.pid"
if "$SCRIPT_DIR/status-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port"; then
  echo "foreign PID was accepted" >&2; exit 1
else
  [[ $? -eq 2 ]]
fi
rm -f "$test_root/runtime/pid/partygame-api.pid"

# A listener owned by another process prevents start without touching that process.
python3 -c 'import socket, time; s=socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1); s.bind(("0.0.0.0", int(__import__("sys").argv[1]))); s.listen(); time.sleep(20)' "$port" &
occupied_pid=$!
sleep 1
if "$SCRIPT_DIR/start-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port"; then
  echo "occupied port was accepted" >&2; exit 1
fi
kill -TERM "$occupied_pid" 2>/dev/null || true
wait "$occupied_pid" 2>/dev/null || true

# Re-deployment keeps runtime and regenerates configuration without a web rebuild.
"$SCRIPT_DIR/deploy-lan.sh" --deploy-root "$test_root" --release-dir "$release_dir" --host "$host" --port "$port"
[[ -f "$runtime_marker" ]]
"$SCRIPT_DIR/restart-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port"

# Make an otherwise valid second release to exercise current switching and rollback.
second="$test_root/second-source"
mkdir -p "$second"
cp -R "$release_dir/." "$second/"
second_version="$(node "$SCRIPT_DIR/release-assets.mjs" version "$second/manifest.json")-lan-rollback"
node "$SCRIPT_DIR/release-assets.mjs" manifest "$second" "$second_version" "lan-test" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(dotnet --version)" "$(node --version)" "$(npm --version)"
first_version="$(node "$SCRIPT_DIR/release-assets.mjs" version "$release_dir/manifest.json")"
"$SCRIPT_DIR/deploy-lan.sh" --deploy-root "$test_root" --release-dir "$second" --host "$host" --port "$port"
"$SCRIPT_DIR/deploy-lan.sh" --deploy-root "$test_root" --rollback "$first_version" --host "$host" --port "$port"
[[ -f "$runtime_marker" ]]

"$SCRIPT_DIR/stop-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port"
! "$SCRIPT_DIR/status-lan.sh" --deploy-root "$test_root" --host "$host" --port "$port"
echo "PartyGame LAN lifecycle PASS: host=$host port=$port"
