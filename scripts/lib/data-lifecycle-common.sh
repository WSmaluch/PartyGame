#!/usr/bin/env bash
# Shared primitives for PartyGame data lifecycle operations. All paths passed
# here are operator-owned absolute paths; manifests deliberately contain only
# relative names.
set -euo pipefail

DATA_EXIT_INCOMPLETE=20
DATA_EXIT_CHECKSUM=21
DATA_EXIT_SQLITE=22
DATA_EXIT_FORMAT=23
DATA_EXIT_SCHEMA=24
DATA_EXIT_LOCK=75

data_die() { echo "PartyGame data: $*" >&2; exit "${2:-64}"; }
data_runtime_dir() { printf '%s/runtime' "$1"; }
data_database_path() { printf '%s/database/partygame.db' "$(data_runtime_dir "$1")"; }
data_media_root() { printf '%s/media' "$(data_runtime_dir "$1")"; }
data_lock_dir() { printf '%s/operations/data-operation.lock' "$(data_runtime_dir "$1")"; }
data_require_absolute() { [[ "$2" = /* ]] || data_die "$1 must be an absolute path"; }
data_sha() { shasum -a 256 "$1" | awk '{print $1}'; }
data_size() { [[ -e "$1" ]] && stat -f '%z' "$1" || printf '0'; }
data_tree_size() { [[ -d "$1" ]] && find "$1" -type f -not -type l -exec stat -f '%z' {} + | awk '{sum += $1} END {print sum + 0}' || printf '0'; }
data_free_bytes() { df -Pk "$1" | awk 'NR == 2 { print $4 * 1024 }'; }
data_assert_space() {
  local path="$1" required="$2" free
  free="$(data_free_bytes "$path")"
  (( free >= required )) || data_die "insufficient free space before operation (required=${required}, available=${free})" 70
}
data_no_symlinks() { ! find "$1" -type l -print -quit | grep -q .; }
data_sqlite_integrity() { [[ "$(sqlite3 "$1" 'PRAGMA integrity_check;' | tr -d '\r')" == "ok" ]]; }
data_schema_version() {
  sqlite3 "$1" "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;" 2>/dev/null || true
}
data_acquire_lock() {
  local root="$1" operation="$2" lock metadata pid
  # A parent lifecycle operation may invoke a nested helper (for example the
  # pre-migration backup). The parent remains the sole owner of the atomic lock.
  [[ "${DATA_LOCK_PARENT:-false}" == true ]] && return
  lock="$(data_lock_dir "$root")"; metadata="$lock/metadata"; pid="$$"
  mkdir -p "$(dirname "$lock")"
  if mkdir "$lock" 2>/dev/null; then
    printf 'operation=%s\npid=%s\nstartedAtUtc=%s\napplicationVersion=%s\n' "$operation" "$pid" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "${PARTYGAME_APPLICATION_VERSION:-unknown}" > "$metadata"
    DATA_LOCK_DIR="$lock"; DATA_LOCK_OWNED=true
    return
  fi
  local owner=""; [[ -f "$metadata" ]] && owner="$(awk -F= '$1 == "pid" { print $2 }' "$metadata")"
  if [[ "$owner" =~ ^[1-9][0-9]*$ ]] && kill -0 "$owner" 2>/dev/null; then
    data_die "operation lock is held by active pid $owner" "$DATA_EXIT_LOCK"
  fi
  # A lock with no live owner is stale. Remove only this known lock directory,
  # then atomically retry acquisition.
  rm -rf "$lock"
  mkdir "$lock" || data_die "could not acquire operation lock" "$DATA_EXIT_LOCK"
  printf 'operation=%s\npid=%s\nstartedAtUtc=%s\napplicationVersion=%s\nrecoveredStaleLock=true\n' "$operation" "$pid" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "${PARTYGAME_APPLICATION_VERSION:-unknown}" > "$metadata"
  DATA_LOCK_DIR="$lock"; DATA_LOCK_OWNED=true
}
data_release_lock() {
  [[ "${DATA_LOCK_PARENT:-false}" == true ]] && return
  [[ "${DATA_LOCK_OWNED:-false}" == true && -n "${DATA_LOCK_DIR:-}" ]] && rm -rf "$DATA_LOCK_DIR"
  DATA_LOCK_DIR=""; DATA_LOCK_OWNED=false
}
data_checksums() {
  local root="$1"
  (cd "$root" && find database media -type f -not -type l -print | LC_ALL=C sort | while IFS= read -r file; do shasum -a 256 "$file"; done) > "$root/checksums.sha256"
}
data_verify_checksums() { (cd "$1" && shasum -a 256 -c checksums.sha256); }
data_validate_backup_layout() {
  local backup="$1"
  [[ -d "$backup/database" && -d "$backup/media" && -f "$backup/database/partygame.db" && -f "$backup/backup-manifest.json" && -f "$backup/checksums.sha256" && -f "$backup/BACKUP_INFO.txt" ]] || data_die "backup is incomplete" "$DATA_EXIT_INCOMPLETE"
  data_no_symlinks "$backup" || data_die "backup contains a symbolic link" "$DATA_EXIT_FORMAT"
  jq -e '.backupFormatVersion == 1 and (.databaseFile == "database/partygame.db") and (.databaseSchemaVersion|type == "string") and (.checksums|type == "object")' "$backup/backup-manifest.json" >/dev/null || data_die "unsupported or invalid backup manifest" "$DATA_EXIT_FORMAT"
}
data_validate_media_consistency() {
  local backup="$1" keys actual
  keys="$(mktemp "${TMPDIR:-/tmp}/partygame-media-keys.XXXXXX")"
  actual="$(mktemp "${TMPDIR:-/tmp}/partygame-media-files.XXXXXX")"
  trap 'rm -f "$keys" "$actual"' RETURN
  sqlite3 "$backup/database/partygame.db" 'SELECT DisplayStorageKey FROM MediaAssets UNION SELECT ThumbnailStorageKey FROM MediaAssets;' | sed '/^$/d' | LC_ALL=C sort -u > "$keys"
  find "$backup/media" -type f -not -type l -print | sed "s|^$backup/media/||" | LC_ALL=C sort -u > "$actual"
  cmp -s "$keys" "$actual" || data_die "media files do not match MediaAsset records" "$DATA_EXIT_INCOMPLETE"
}
