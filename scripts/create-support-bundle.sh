#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=lib/diagnostics-common.sh
source "$SCRIPT_DIR/lib/diagnostics-common.sh"

DEPLOY_ROOT="${PARTYGAME_DEPLOY_ROOT:-}"
MODE="standard"
OUTPUT=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --deploy-root) DEPLOY_ROOT="${2:-}"; shift 2 ;;
    --mode) MODE="${2:-}"; shift 2 ;;
    --output) OUTPUT="${2:-}"; shift 2 ;;
    *) diagnostics_die "Usage: create-support-bundle.sh --deploy-root <absolute-dir> [--mode minimal|standard|extended] [--output <absolute-dir>]" ;;
  esac
done
[[ "$MODE" == minimal || "$MODE" == standard || "$MODE" == extended ]] || diagnostics_die "mode must be minimal, standard, or extended."
diagnostics_require_absolute_directory "$DEPLOY_ROOT" "deploy root"
[[ -L "$DEPLOY_ROOT/current" ]] || diagnostics_die "deployment has no current release."
RELEASE_DIR="$(cd "$DEPLOY_ROOT/current" && pwd -P)"
[[ "$RELEASE_DIR" == "$DEPLOY_ROOT"/releases/* && -f "$RELEASE_DIR/manifest.json" ]] || diagnostics_die "current release is outside deploy-root/releases or incomplete."
if [[ -z "$OUTPUT" ]]; then OUTPUT="$DEPLOY_ROOT/runtime/support-bundles"; fi
[[ "$OUTPUT" = /* ]] || diagnostics_die "output must be an absolute path."
mkdir -p "$OUTPUT"
[[ ! -L "$OUTPUT" ]] || diagnostics_die "output directory must not be a symlink."

VERSION="$(node "$REPO_DIR/scripts/release-assets.mjs" version "$RELEASE_DIR/manifest.json")"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
TARGET="$OUTPUT/partygame-support-$STAMP-$VERSION.tar.gz"
TMP_ROOT="$(mktemp -d "$OUTPUT/.partygame-support.XXXXXX")"
STAGE="$TMP_ROOT/bundle"
mkdir -p "$STAGE"/{version,diagnostics,configuration,logs,deployment,database,backup,network}
cleanup() { rm -rf "$TMP_ROOT"; }
trap cleanup EXIT

cp "$RELEASE_DIR/manifest.json" "$STAGE/version/release-manifest.json"
sed -E -e 's#(/Users/)[^/[:space:]" ]+#\1[REDACTED]#g' -e 's#(/home/)[^/[:space:]" ]+#\1[REDACTED]#g' "$RELEASE_DIR/BUILD_INFO.txt" > "$STAGE/version/BUILD_INFO.txt"
{
  printf 'PartyGame support bundle\nMode: %s\nVersion: %s\nCreated UTC: %s\n' "$MODE" "$VERSION" "$STAMP"
  printf 'This bundle deliberately excludes databases, SQLite sidecars, media, credentials, player data and raw request bodies.\n'
} > "$STAGE/SUPPORT_INFO.txt"

if [[ -f "$DEPLOY_ROOT/config/partygame.env" && ! -L "$DEPLOY_ROOT/config/partygame.env" ]]; then
  diagnostics_redact_stream < "$DEPLOY_ROOT/config/partygame.env" > "$STAGE/configuration/environment.redacted.txt"
  sed -n -E 's/^([A-Za-z_][A-Za-z0-9_]*)=.*/\1/p' "$DEPLOY_ROOT/config/partygame.env" | sort -u > "$STAGE/configuration/environment-variable-names.txt"
fi

BASE_URL=""
if [[ -f "$DEPLOY_ROOT/config/partygame.env" ]]; then BASE_URL="$(sed -n 's/^PARTYGAME_PUBLIC_BASE_URL=//p' "$DEPLOY_ROOT/config/partygame.env" | head -1)"; fi
if [[ -n "$BASE_URL" ]]; then
  curl --silent --show-error --max-time 10 "$BASE_URL/health" | diagnostics_redact_stream > "$STAGE/diagnostics/health.json" || printf '{"status":"unavailable"}\n' > "$STAGE/diagnostics/health.json"
  curl --silent --show-error --max-time 10 "$BASE_URL/health/ready" | diagnostics_redact_stream > "$STAGE/diagnostics/readiness.json" || printf '{"status":"unavailable"}\n' > "$STAGE/diagnostics/readiness.json"
  curl --silent --show-error --max-time 10 "$BASE_URL/api/system/version" | diagnostics_redact_stream > "$STAGE/version/api-version.json" || true
fi

LOG_ROOT="$DEPLOY_ROOT/runtime/logs"
MAX_FILES=3; MAX_BYTES=$((2 * 1024 * 1024)); [[ "$MODE" == extended ]] && { MAX_FILES=8; MAX_BYTES=$((8 * 1024 * 1024)); }
[[ "$MODE" == minimal ]] && { MAX_FILES=1; MAX_BYTES=$((256 * 1024)); }
count=0; truncated=false
if [[ -d "$LOG_ROOT" && ! -L "$LOG_ROOT" ]]; then
  while IFS= read -r file; do
    diagnostics_is_safe_file "$LOG_ROOT" "$file" || continue
    (( count < MAX_FILES )) || { truncated=true; continue; }
    name="$(basename "$file")"
    tail -c "$MAX_BYTES" "$file" | diagnostics_redact_stream > "$STAGE/logs/$name.redacted.txt"
    count=$((count + 1))
  done < <(find "$LOG_ROOT" -type f -name 'partygame-*.log*' ! -type l -print 2>/dev/null | sort -r)
fi
printf '{"logFilesIncluded":%s,"logsTruncated":%s,"databaseIncluded":false,"mediaIncluded":false}\n' "$count" "$truncated" > "$STAGE/diagnostics/collection.json"

if [[ -d "$DEPLOY_ROOT/runtime/backups" && ! -L "$DEPLOY_ROOT/runtime/backups" ]]; then
  find "$DEPLOY_ROOT/runtime/backups" -maxdepth 2 -type f -name '*manifest*.json' ! -type l -print 2>/dev/null | head -1 | while IFS= read -r file; do diagnostics_redact_stream < "$file" > "$STAGE/backup/last-backup-manifest.redacted.json"; done
fi
df -k "$DEPLOY_ROOT" | diagnostics_redact_stream > "$STAGE/deployment/disk-space.txt" || true
printf 'dotnet=%s\nnode=%s\n' "$(dotnet --version 2>/dev/null || echo unavailable)" "$(node --version 2>/dev/null || echo unavailable)" > "$STAGE/deployment/runtime-versions.txt"

(cd "$STAGE" && find . -type f ! -name checksums.sha256 ! -name support-manifest.json -print | LC_ALL=C sort | while IFS= read -r file; do shasum -a 256 "$file"; done > checksums.sha256)
node - "$STAGE" "$VERSION" "$MODE" "$STAMP" "$count" "$truncated" <<'NODE'
const fs = require('fs'); const path = require('path');
const [root, version, mode, createdAtUtc, count, truncated] = process.argv.slice(2);
const files = []; const walk = dir => { for (const entry of fs.readdirSync(dir, { withFileTypes: true })) { const full = path.join(dir, entry.name); if (entry.isDirectory()) walk(full); else if (entry.isFile() && entry.name !== 'support-manifest.json') files.push(path.relative(root, full)); } }; walk(root);
const checksums = Object.fromEntries(fs.readFileSync(path.join(root, 'checksums.sha256'), 'utf8').trim().split('\n').filter(Boolean).map(line => { const [hash, file] = line.split(/  +/); return [file, hash]; }));
const size = files.reduce((sum, file) => sum + fs.statSync(path.join(root, file)).size, 0);
fs.writeFileSync(path.join(root, 'support-manifest.json'), JSON.stringify({ supportBundleFormatVersion: '1', createdAtUtc, applicationVersion: version, commitHash: 'from-release-manifest', environment: 'deployment', databaseSchemaVersion: 'see-api-version.json', includedSections: ['version','diagnostics','configuration','logs','deployment','backup','network'], omittedSections: ['database','sqlite-wal','sqlite-shm','media','tokens','player-data'], redactionRulesVersion: '1', sourceLogTimeRange: 'bounded-tail', fileCount: files.length, totalUncompressedSize: size, checksums, diagnosticSummary: { mode, logFilesIncluded: Number(count), logsTruncated: truncated === 'true' } }, null, 2) + '\n');
NODE
"$SCRIPT_DIR/verify-support-bundle.sh" --directory "$STAGE"
tar -czf "$TMP_ROOT/bundle.tar.gz" -C "$STAGE" .
mv "$TMP_ROOT/bundle.tar.gz" "$TARGET"
printf 'PASS support bundle: %s\n' "$(basename "$TARGET")"
