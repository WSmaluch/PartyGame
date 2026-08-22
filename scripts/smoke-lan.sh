#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"
lan_parse_arguments "$@"
lan_load_environment
base="$(lan_url)"
for path in /health /health/ready /api/system/version; do
  curl --fail --silent --show-error "$base$path" >/dev/null
done
work="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-lan-smoke.XXXXXX")"
trap 'rm -rf "$work"' EXIT INT TERM

assert_html() {
  local path="$1" headers="$work/headers" body="$work/body"
  curl --fail --silent --show-error --dump-header "$headers" "$base$path" -o "$body"
  grep -Eiq '^Content-Type: text/html([;[:space:]]|$)' "$headers" || { echo "PartyGame LAN: $path is not HTML." >&2; exit 1; }
  grep -Eiq '<!doctype html|<html' "$body" || { echo "PartyGame LAN: $path does not contain an HTML document." >&2; exit 1; }
}

assert_json_config() {
  local path="$1" headers="$work/headers" body="$work/body"
  curl --fail --silent --show-error --dump-header "$headers" "$base$path" -o "$body"
  grep -Eiq '^Content-Type: application/json([;[:space:]]|$)' "$headers" || { echo "PartyGame LAN: $path is not JSON." >&2; exit 1; }
  node -e 'JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"))' "$body"
}

assert_javascript_asset() {
  local app="$1" public_path index headers asset
  public_path="${2:-/$app}"
  index="$work/$app-index.html"
  headers="$work/headers"
  curl --fail --silent --show-error "$base$public_path/" -o "$index"
  asset="$(sed -nE "s#.*src=\"($public_path/assets/[^\"]+\\.js)\".*#\\1#p" "$index" | head -n 1)"
  [[ -n "$asset" ]] || { echo "PartyGame LAN: $app index does not reference a JavaScript asset." >&2; exit 1; }
  curl --fail --silent --show-error --dump-header "$headers" "$base$asset" -o "$work/$app-asset.js"
  grep -Eiq '^Content-Type: (application|text)/javascript([;[:space:]]|$)' "$headers" || { echo "PartyGame LAN: $asset is not JavaScript." >&2; exit 1; }
}

assert_missing_static_file() {
  local path="$1" status
  status="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' "$base$path")"
  [[ "$status" == 404 ]] || { echo "PartyGame LAN: missing static file $path returned HTTP $status instead of 404." >&2; exit 1; }
}

assert_html /display/
assert_html /admin/
assert_html /play/
assert_json_config /display/config.json
assert_json_config /admin/config.json
assert_json_config /play/config.json
assert_javascript_asset display
assert_javascript_asset admin
assert_javascript_asset player /play
assert_missing_static_file /display/missing.js
assert_missing_static_file /admin/missing.json
assert_missing_static_file /play/missing.js
curl --fail --silent --show-error --request POST "$base/hubs/game/negotiate?negotiateVersion=1" >/dev/null
if curl --fail --silent "$base/display/config.json" | grep -Eq 'localhost|127\.0\.0\.1'; then
  echo "PartyGame LAN: Display config contains a loopback address." >&2; exit 1
fi
if curl --fail --silent "$base/admin/config.json" | grep -Eq 'localhost|127\.0\.0\.1'; then
  echo "PartyGame LAN: Admin config contains a loopback address." >&2; exit 1
fi
if curl --fail --silent "$base/play/config.json" | grep -Eq 'localhost|127\.0\.0\.1'; then
  echo "PartyGame LAN: Web Player config contains a loopback address." >&2; exit 1
fi
echo "PartyGame LAN smoke PASS: $base"
