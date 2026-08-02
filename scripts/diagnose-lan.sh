#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=lib/diagnostics-common.sh
source "$SCRIPT_DIR/lib/diagnostics-common.sh"
DEPLOY_ROOT="${PARTYGAME_DEPLOY_ROOT:-}"; BASE_URL=""; TOKEN_ENV="PARTYGAME_OPERATOR_TOKEN"; OUTPUT=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --deploy-root) DEPLOY_ROOT="${2:-}"; shift 2 ;;
    --base-url) BASE_URL="${2:-}"; shift 2 ;;
    --operator-token-env) TOKEN_ENV="${2:-}"; shift 2 ;;
    --output) OUTPUT="${2:-}"; shift 2 ;;
    *) diagnostics_die "Usage: diagnose-lan.sh --deploy-root <absolute-dir> --base-url <http(s)-url> [--operator-token-env NAME] [--output file]" ;;
  esac
done
diagnostics_require_absolute_directory "$DEPLOY_ROOT" "deploy root"
[[ "$BASE_URL" =~ ^https?://[^/]+ ]] || diagnostics_die "--base-url must be an absolute HTTP(S) URL."
[[ "$TOKEN_ENV" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || diagnostics_die "operator token env name is invalid."
if [[ -n "$OUTPUT" ]]; then [[ "$OUTPUT" = /* ]] || diagnostics_die "output must be absolute."; mkdir -p "$(dirname "$OUTPUT")"; exec > "$OUTPUT"; fi
status=0
check() { local name="$1" result="$2" detail="$3"; printf '%-28s %s %s\n' "$name" "$result" "$detail"; [[ "$result" == FAIL ]] && status=1; }
release="$DEPLOY_ROOT/current"
if [[ -L "$release" && -f "$release/manifest.json" ]]; then check RELEASE PASS "current release found"; else check RELEASE FAIL "current release or manifest missing"; fi
if [[ -f "$DEPLOY_ROOT/runtime/pid/partygame-api.pid" ]] && kill -0 "$(tr -d '[:space:]' < "$DEPLOY_ROOT/runtime/pid/partygame-api.pid")" 2>/dev/null; then check PROCESS PASS "PID running"; else check PROCESS WARN "PID not running or unavailable"; fi
curl --silent --show-error --max-time 10 --fail "$BASE_URL/health" >/dev/null && check HEALTH PASS "reachable" || check HEALTH FAIL "unreachable"
curl --silent --show-error --max-time 10 --fail "$BASE_URL/health/ready" >/dev/null && check READINESS PASS "ready" || check READINESS WARN "not ready"
curl --silent --show-error --max-time 10 --fail "$BASE_URL/api/system/version" >/dev/null && check VERSION PASS "available" || check VERSION FAIL "unavailable"
token="${!TOKEN_ENV:-}"
header_file="$(mktemp "${TMPDIR:-/private/tmp}/partygame-diagnostics-header.XXXXXX")"; trap 'rm -f "$header_file"' EXIT; chmod 600 "$header_file"; printf 'Authorization: Bearer %s\n' "$token" > "$header_file"
if [[ -n "$token" ]] && curl --silent --show-error --max-time 10 --fail -H "@$header_file" "$BASE_URL/api/admin/diagnostics/summary" >/dev/null; then check ADMIN_DIAGNOSTICS PASS "authorized"; else check ADMIN_DIAGNOSTICS WARN "unavailable or unauthorized"; fi
[[ -f "$DEPLOY_ROOT/runtime/database/partygame.db" ]] && check DATABASE PASS "present" || check DATABASE WARN "database file missing"
[[ -d "$DEPLOY_ROOT/runtime/media" ]] && check MEDIA_ROOT PASS "present" || check MEDIA_ROOT WARN "missing"
[[ -d "$DEPLOY_ROOT/runtime/logs" ]] && check LOG_ROOT PASS "present" || check LOG_ROOT WARN "missing"
[[ -d "$DEPLOY_ROOT/runtime/backups" ]] && check BACKUP_ROOT PASS "present" || check BACKUP_ROOT WARN "missing"
df -k "$DEPLOY_ROOT" >/dev/null && check DISK_SPACE PASS "available" || check DISK_SPACE FAIL "unavailable"
if [[ -L "$release" ]]; then "$REPO_DIR/scripts/smoke-release.sh" "$(cd "$release" && pwd -P)" >/dev/null && check MANIFEST_CHECKSUMS PASS "valid" || check MANIFEST_CHECKSUMS FAIL "invalid"; fi
exit "$status"
