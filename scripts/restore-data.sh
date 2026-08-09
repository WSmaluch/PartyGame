#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/data-lifecycle-common.sh"
DEPLOY_ROOT=""; BACKUP=""; DRY_RUN=false; FORCE=false; BACKUP_ROOT=""
while [[ $# -gt 0 ]]; do case "$1" in
  --deploy-root) DEPLOY_ROOT="${2:-}"; shift 2;; --backup) BACKUP="${2:-}"; shift 2;; --backup-root) BACKUP_ROOT="${2:-}"; shift 2;; --dry-run) DRY_RUN=true; shift;; --force) FORCE=true; shift;; *) data_die "usage: restore-data.sh --deploy-root PATH --backup PATH [--backup-root PATH] [--dry-run] [--force]";; esac; done
[[ -n "$DEPLOY_ROOT" && -n "$BACKUP" ]] || data_die "--deploy-root and --backup are required"
data_require_absolute deploy-root "$DEPLOY_ROOT"; data_require_absolute backup "$BACKUP"; BACKUP_ROOT="${BACKUP_ROOT:-$(dirname "$BACKUP")}"; data_require_absolute backup-root "$BACKUP_ROOT"
"$SCRIPT_DIR/verify-backup.sh" "$BACKUP"
DB="$(data_database_path "$DEPLOY_ROOT")"; MEDIA="$(data_media_root "$DEPLOY_ROOT")"; RUNTIME="$(data_runtime_dir "$DEPLOY_ROOT")"
mkdir -p "$RUNTIME/database" "$RUNTIME/temp" "$RUNTIME/operations"
required=$(( $(data_size "$BACKUP/database/partygame.db") + $(data_tree_size "$BACKUP/media") + $(data_size "$DB") + $(data_tree_size "$MEDIA") + 2097152 )); data_assert_space "$DEPLOY_ROOT" "$required"
if [[ "$DRY_RUN" == true ]]; then echo "RESTORE_DRY_RUN: verified backup, schema and free space; no process or data changed."; exit 0; fi
if [[ -f "$RUNTIME/pid/partygame-api.pid" ]]; then pid="$(tr -d '[:space:]' < "$RUNTIME/pid/partygame-api.pid")"; if [[ "$pid" =~ ^[1-9][0-9]*$ ]] && kill -0 "$pid" 2>/dev/null; then data_die "restore requires a stopped API process" "$DATA_EXIT_LOCK"; fi; fi
# Pre-restore backup is deliberately complete before the restore lock is held.
# A disaster-recovery restore into an empty runtime has nothing to preserve.
pre_backup=""
if [[ -f "$DB" && -d "$MEDIA" ]]; then
  pre_output="$("$SCRIPT_DIR/backup-data.sh" --deploy-root "$DEPLOY_ROOT" --backup-root "$BACKUP_ROOT" --maintenance --name "$(date -u +%Y%m%dT%H%M%SZ)-$$-pre-restore")"
  pre_backup="${pre_output#BACKUP_PATH=}"
fi
data_acquire_lock "$DEPLOY_ROOT" "restore"; trap 'data_release_lock; [[ -n "${INCOMING:-}" ]] && rm -rf "$INCOMING"' EXIT
INCOMING="$(mktemp -d "$RUNTIME/temp/restore.XXXXXX")"; mkdir -p "$INCOMING/database" "$INCOMING/media"
cp -p "$BACKUP/database/partygame.db" "$INCOMING/database/partygame.db"; cp -pR "$BACKUP/media/." "$INCOMING/media/"
data_no_symlinks "$INCOMING" || data_die "restore input contains symbolic links" "$DATA_EXIT_FORMAT"
data_sqlite_integrity "$INCOMING/database/partygame.db" || data_die "restored SQLite integrity_check failed" "$DATA_EXIT_SQLITE"
old_db="$RUNTIME/database/partygame.db.pre-restore"; old_media="$RUNTIME/media.pre-restore"; rm -f "$old_db"; rm -rf "$old_media"
mv "$DB" "$old_db" 2>/dev/null || true; mv "$MEDIA" "$old_media" 2>/dev/null || true
if mv "$INCOMING/database/partygame.db" "$DB" && [[ "${PARTYGAME_RESTORE_TEST_FAIL_AFTER_DATABASE_SWAP:-false}" != true ]] && mv "$INCOMING/media" "$MEDIA" && data_sqlite_integrity "$DB"; then
  rm -f "$old_db"; rm -rf "$old_media"; rmdir "$INCOMING" 2>/dev/null || true; INCOMING=""; echo "RESTORE_PASS preRestoreBackup=${pre_backup:-none}"; exit 0
fi
rm -f "$DB"; rm -rf "$MEDIA"; [[ -f "$old_db" ]] && mv "$old_db" "$DB"; [[ -d "$old_media" ]] && mv "$old_media" "$MEDIA"; data_die "restore failed; pre-restore data was rolled back" 1
