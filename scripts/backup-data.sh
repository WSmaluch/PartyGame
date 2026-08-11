#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/data-lifecycle-common.sh
source "$SCRIPT_DIR/lib/data-lifecycle-common.sh"

DEPLOY_ROOT=""; BACKUP_ROOT=""; NAME=""; MODE="online"
while [[ $# -gt 0 ]]; do case "$1" in
  --deploy-root) DEPLOY_ROOT="${2:-}"; shift 2;; --backup-root) BACKUP_ROOT="${2:-}"; shift 2;; --name) NAME="${2:-}"; shift 2;; --online) MODE="online"; shift;; --maintenance) MODE="maintenance"; shift;; *) data_die "usage: backup-data.sh --deploy-root PATH --backup-root PATH [--name NAME] [--online|--maintenance]";; esac; done
[[ -n "$DEPLOY_ROOT" && -n "$BACKUP_ROOT" ]] || data_die "--deploy-root and --backup-root are required"
data_require_absolute deploy-root "$DEPLOY_ROOT"; data_require_absolute backup-root "$BACKUP_ROOT"
DB="$(data_database_path "$DEPLOY_ROOT")"; MEDIA="$(data_media_root "$DEPLOY_ROOT")"
[[ -f "$DB" && -d "$MEDIA" ]] || data_die "runtime database or media directory is missing" "$DATA_EXIT_INCOMPLETE"
NAME="${NAME:-$(date -u +%Y%m%dT%H%M%SZ)-${PARTYGAME_APPLICATION_VERSION:-unknown}}"; [[ "$NAME" =~ ^[A-Za-z0-9._-]+$ ]] || data_die "backup name must contain only A-Z a-z 0-9 . _ -"
FINAL="$BACKUP_ROOT/$NAME"; [[ ! -e "$FINAL" ]] || data_die "backup destination already exists"
mkdir -p "$BACKUP_ROOT"
required=$(( $(data_size "$DB") + $(data_tree_size "$MEDIA") + $(data_size "$DB") / 10 + 1048576 )); data_assert_space "$BACKUP_ROOT" "$required"
data_acquire_lock "$DEPLOY_ROOT" "backup-$MODE"; trap 'data_release_lock; [[ -n "${STAGE:-}" ]] && rm -rf "$STAGE"' EXIT
STAGE="$(mktemp -d "$BACKUP_ROOT/.${NAME}.staging.XXXXXX")"; mkdir -p "$STAGE/database" "$STAGE/media"
# sqlite3 .backup is SQLite's online backup API and includes WAL state.
sqlite3 "$DB" ".backup '$STAGE/database/partygame.db'" || data_die "SQLite online backup failed" "$DATA_EXIT_SQLITE"
data_sqlite_integrity "$STAGE/database/partygame.db" || data_die "SQLite integrity_check failed" "$DATA_EXIT_SQLITE"
keys_file="$STAGE/.media-keys"
sqlite3 "$STAGE/database/partygame.db" 'SELECT DisplayStorageKey FROM MediaAssets UNION SELECT ThumbnailStorageKey FROM MediaAssets;' | sed '/^$/d' | LC_ALL=C sort -u > "$keys_file"
while IFS= read -r key; do
  [[ "$key" != /* && "$key" != *".."* ]] || data_die "invalid media key in database" "$DATA_EXIT_INCOMPLETE"
  source="$MEDIA/$key"; target="$STAGE/media/$key"; [[ -f "$source" && ! -L "$source" ]] || data_die "media record has no regular file" "$DATA_EXIT_INCOMPLETE"
  before="$(data_sha "$source")"; mkdir -p "$(dirname "$target")"; cp -p "$source" "$target"; after="$(data_sha "$source")"; [[ "$before" == "$after" && "$before" == "$(data_sha "$target")" ]] || data_die "media changed while being copied" "$DATA_EXIT_INCOMPLETE"
done < "$keys_file"
while IFS= read -r file; do key="${file#"$MEDIA/"}"; [[ "$key" == .* ]] && continue; grep -Fqx "$key" "$keys_file" || data_die "untracked media file detected" "$DATA_EXIT_INCOMPLETE"; done < <(find "$MEDIA" -type f -not -type l -print)
data_checksums "$STAGE"; data_verify_checksums "$STAGE" >/dev/null || data_die "checksum self-verification failed" "$DATA_EXIT_CHECKSUM"
schema="$(data_schema_version "$STAGE/database/partygame.db")"; [[ -n "$schema" ]] || data_die "schema history is missing" "$DATA_EXIT_SCHEMA"; key_count="$(awk 'END {print NR}' "$keys_file")"; rm -f "$keys_file"
dbsize="$(data_size "$STAGE/database/partygame.db")"; mediasize="$(data_tree_size "$STAGE/media")"; commit="${PARTYGAME_COMMIT_HASH:-unknown}"
node - "$STAGE/checksums.sha256" "$STAGE/backup-manifest.json" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "${PARTYGAME_APPLICATION_VERSION:-unknown}" "$commit" "$schema" "$MODE" "${PARTYGAME_DEPLOYMENT_VERSION:-unknown}" "${PARTYGAME_RUNTIME_ID:-$(basename "$DEPLOY_ROOT")}" "$dbsize" "$key_count" "$mediasize" <<'NODE'
const fs = require('fs');
const [checksumsPath, outputPath, createdAtUtc, applicationVersion, commitHash, databaseSchemaVersion, mode, sourceDeploymentVersion, sourceRuntimeId, databaseSize, mediaFileCount, mediaTotalSize] = process.argv.slice(2);
const checksums = Object.fromEntries(fs.readFileSync(checksumsPath, 'utf8').trim().split('\n').filter(Boolean).map(line => {
  const match = line.match(/^([a-f0-9]{64})  (.+)$/i);
  if (!match) throw new Error(`Invalid checksum line: ${line}`);
  return [match[2], match[1]];
}));
fs.writeFileSync(outputPath, JSON.stringify({
  backupFormatVersion: 1,
  createdAtUtc,
  applicationVersion,
  commitHash,
  databaseSchemaVersion,
  databaseFile: 'database/partygame.db',
  databaseSize: Number(databaseSize),
  mediaFileCount: Number(mediaFileCount),
  mediaTotalSize: Number(mediaTotalSize),
  sourceDeploymentVersion,
  sourceRuntimeId,
  mode,
  integrityCheck: 'ok',
  checksums
}, null, 2) + '\n');
NODE
printf 'PartyGame backup %s\nSchema: %s\nMode: %s\n' "$NAME" "$schema" "$MODE" > "$STAGE/BACKUP_INFO.txt"
mv "$STAGE" "$FINAL"; STAGE=""; echo "BACKUP_PATH=$FINAL"
