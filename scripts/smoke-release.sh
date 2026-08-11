#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RELEASE_DIR="${1:?Usage: scripts/smoke-release.sh artifacts/release/<version>}"
RELEASE_DIR="$(cd "$RELEASE_DIR" && pwd)"
API_DLL="$RELEASE_DIR/api/PartyGame.Api.dll"
MANIFEST="$RELEASE_DIR/manifest.json"

for tool in curl dotnet node; do command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 69; }; done
[[ -f "$API_DLL" && -f "$MANIFEST" ]] || { echo "Release artifact is incomplete: $RELEASE_DIR" >&2; exit 66; }

PORT="$(node "$REPO_DIR/scripts/find-free-port.mjs")"
RUNTIME_DIR="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-release-smoke.XXXXXX")"
LOG_FILE="$RUNTIME_DIR/backend.log"
PID=""
SUCCESS=false
mkdir -p "$RUNTIME_DIR/data" "$RUNTIME_DIR/media"

cleanup() {
  local exit_code=$?
  if [[ -n "$PID" ]] && kill -0 "$PID" 2>/dev/null; then
    kill "$PID" 2>/dev/null || true
    wait "$PID" 2>/dev/null || true
  fi
  if [[ "$SUCCESS" == true ]]; then
    rm -rf "$RUNTIME_DIR"
  else
    echo "Release smoke failed; diagnostic log retained at $LOG_FILE" >&2
  fi
  exit "$exit_code"
}
trap cleanup EXIT INT TERM

ASPNETCORE_ENVIRONMENT=Production \
PARTYGAME_URLS="http://127.0.0.1:$PORT" \
PARTYGAME_DATABASE_PATH="$RUNTIME_DIR/data/partygame.db" \
PARTYGAME_MEDIA_ROOT="$RUNTIME_DIR/media" \
PARTYGAME_PUBLIC_BASE_URL="http://127.0.0.1:$PORT" \
PARTYGAME_ALLOWED_ORIGINS="http://127.0.0.1:5173,http://127.0.0.1:5174" \
PARTYGAME_DISPLAY_PUBLIC_URL="http://127.0.0.1:5173/display" \
PARTYGAME_ADMIN_PUBLIC_URL="http://127.0.0.1:5174/admin" \
PARTYGAME_APPLY_MIGRATIONS=true \
PARTYGAME_ALLOW_INSECURE_LAN_HTTP=true \
PARTYGAME_OPERATOR_TOKEN="release-smoke-operator-token-that-is-not-a-secret" \
PARTYGAME_DEPLOYMENT_ENABLED=true \
PARTYGAME_DISPLAY_ROOT="$RELEASE_DIR/display" \
PARTYGAME_ADMIN_ROOT="$RELEASE_DIR/admin" \
dotnet "$API_DLL" >"$LOG_FILE" 2>&1 &
PID=$!

for _ in $(seq 1 40); do
  if curl --silent --fail "http://127.0.0.1:$PORT/health" >/dev/null; then break; fi
  if ! kill -0 "$PID" 2>/dev/null; then cat "$LOG_FILE" >&2; exit 1; fi
  sleep 1
done
curl --silent --fail "http://127.0.0.1:$PORT/health" >/dev/null
curl --silent --fail "http://127.0.0.1:$PORT/health/ready" >/dev/null
curl --silent --fail "http://127.0.0.1:$PORT/api/content/packages" >/dev/null

smoke_dir="$RUNTIME_DIR/static-smoke"
mkdir -p "$smoke_dir"
assert_html() {
  local path="$1" headers="$smoke_dir/headers" body="$smoke_dir/body"
  curl --silent --show-error --fail --dump-header "$headers" "http://127.0.0.1:$PORT$path" -o "$body"
  grep -Eiq '^Content-Type: text/html([;[:space:]]|$)' "$headers" || { echo "Release smoke: $path is not HTML." >&2; exit 1; }
}
assert_json_config() {
  local path="$1" headers="$smoke_dir/headers" body="$smoke_dir/body"
  curl --silent --show-error --fail --dump-header "$headers" "http://127.0.0.1:$PORT$path" -o "$body"
  grep -Eiq '^Content-Type: application/json([;[:space:]]|$)' "$headers" || { echo "Release smoke: $path is not JSON." >&2; exit 1; }
  node -e 'JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"))' "$body"
}
assert_html /display/
assert_html /admin/
assert_json_config /display/config.json
assert_json_config /admin/config.json

EXPECTED_VERSION="$(node "$REPO_DIR/scripts/release-assets.mjs" version "$MANIFEST")"
ACTUAL_VERSION="$(curl --silent --fail "http://127.0.0.1:$PORT/api/system/version" | node -e 'let body=""; process.stdin.on("data", part => body += part); process.stdin.on("end", () => process.stdout.write(JSON.parse(body).version));')"
[[ "$ACTUAL_VERSION" == "$EXPECTED_VERSION" ]] || { echo "Version mismatch: manifest=$EXPECTED_VERSION endpoint=$ACTUAL_VERSION" >&2; exit 1; }

SUCCESS=true
echo "Release smoke PASS: $EXPECTED_VERSION"
