#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_DLL="${1:-$REPO_DIR/server/PartyGame.Api/bin/Release/net10.0/PartyGame.Api.dll}"
[[ -f "$API_DLL" ]] || { echo "API DLL not found: $API_DLL" >&2; exit 66; }
for tool in curl dotnet node; do command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 69; }; done

port="$(node "$REPO_DIR/scripts/find-free-port.mjs")"
runtime="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-security-smoke.XXXXXX")"
log="$runtime/api.log"
token="$(node -e 'process.stdout.write(require("crypto").randomBytes(32).toString("hex"))')"
pid=""
success=false
cleanup() {
  local code=$?
  [[ -z "$pid" ]] || { kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; }
  if [[ "$success" == true ]]; then rm -rf "$runtime"; else echo "Security smoke diagnostics: $log" >&2; fi
  exit "$code"
}
trap cleanup EXIT INT TERM
mkdir -p "$runtime/data" "$runtime/media"

# Production HTTP must reject an accidental process start without explicit LAN opt-in.
if ASPNETCORE_ENVIRONMENT=Production PARTYGAME_URLS="http://127.0.0.1:$port" PARTYGAME_DATABASE_PATH="$runtime/data/blocked.db" PARTYGAME_MEDIA_ROOT="$runtime/media" PARTYGAME_PUBLIC_BASE_URL="http://127.0.0.1:$port" PARTYGAME_ALLOWED_ORIGINS="http://allowed.example" PARTYGAME_OPERATOR_TOKEN="$token" dotnet "$API_DLL" check >"$runtime/blocked.log" 2>&1; then
  echo "Production HTTP unexpectedly started without opt-in." >&2; exit 1
fi

ASPNETCORE_ENVIRONMENT=Production \
PARTYGAME_URLS="http://127.0.0.1:$port" \
PARTYGAME_DATABASE_PATH="$runtime/data/partygame.db" \
PARTYGAME_MEDIA_ROOT="$runtime/media" \
PARTYGAME_PUBLIC_BASE_URL="http://127.0.0.1:$port" \
PARTYGAME_ALLOWED_ORIGINS="http://allowed.example" \
PARTYGAME_OPERATOR_TOKEN="$token" \
PARTYGAME_ALLOW_INSECURE_LAN_HTTP=true \
PARTYGAME_APPLY_MIGRATIONS=true \
dotnet "$API_DLL" >"$log" 2>&1 &
pid=$!
for _ in $(seq 1 40); do curl --silent --fail "http://127.0.0.1:$port/health" >/dev/null && break; kill -0 "$pid" 2>/dev/null || { cat "$log" >&2; exit 1; }; sleep 1; done
curl --silent --fail "http://127.0.0.1:$port/health" >/dev/null

admin="http://127.0.0.1:$port/api/admin/content-packages"
[[ "$(curl --silent -o /dev/null -w '%{http_code}' "$admin")" == 401 ]]
[[ "$(curl --silent -H 'Authorization: Bearer wrong' -o /dev/null -w '%{http_code}' "$admin")" == 401 ]]
[[ "$(curl --silent -H "Authorization: Bearer $token" -o /dev/null -w '%{http_code}' "$admin")" == 200 ]]
rate_limited=false
for _ in $(seq 1 24); do
  status="$(curl --silent -H "Authorization: Bearer $token" -o /dev/null -w '%{http_code}' "$admin")"
  if [[ "$status" == 429 ]]; then rate_limited=true; break; fi
done
[[ "$rate_limited" == true ]] || { echo "Operator rate limit did not return 429." >&2; exit 1; }
[[ -z "$(curl --silent -D - -o /dev/null -H 'Origin: http://blocked.example' "$admin" | tr -d '\r' | rg '^Access-Control-Allow-Origin:' || true)" ]]
curl --silent -D "$runtime/cors" -o /dev/null -H 'Origin: http://allowed.example' "http://127.0.0.1:$port/health"
rg -q '^Access-Control-Allow-Origin: http://allowed.example' "$runtime/cors" || { echo "Allowed CORS origin was not accepted." >&2; exit 1; }
curl --silent -D "$runtime/headers" -o /dev/null "http://127.0.0.1:$port/health"
for header in X-Content-Type-Options Referrer-Policy X-Frame-Options Permissions-Policy Content-Security-Policy; do rg -qi "^${header}:" "$runtime/headers" || { echo "Missing security header: $header" >&2; exit 1; }; done
if rg -F "$token" "$log"; then echo "Operator token appeared in log." >&2; exit 1; fi

success=true
echo "Security smoke PASS"
