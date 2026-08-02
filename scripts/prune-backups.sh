#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/data-lifecycle-common.sh"
ROOT=""; DEPLOY_ROOT=""; KEEP_LAST="0"; KEEP_DAYS="0"; DRY_RUN=false
while [[ $# -gt 0 ]]; do case "$1" in --backup-root) ROOT="${2:-}"; shift 2;; --deploy-root) DEPLOY_ROOT="${2:-}"; shift 2;; --keep-last) KEEP_LAST="${2:-}"; shift 2;; --keep-days) KEEP_DAYS="${2:-}"; shift 2;; --dry-run) DRY_RUN=true; shift;; *) data_die "usage: prune-backups.sh --backup-root PATH [--deploy-root PATH] --keep-last N --keep-days N [--dry-run]";; esac; done
[[ "$KEEP_LAST" =~ ^[0-9]+$ && "$KEEP_DAYS" =~ ^[0-9]+$ && -n "$ROOT" ]] || data_die "backup root and non-negative retention values are required"
data_require_absolute backup-root "$ROOT"; all=(); while IFS= read -r path; do all+=("$path"); done < <(find "$ROOT" -mindepth 1 -maxdepth 1 -type d -not -name '.*' -print | LC_ALL=C sort -r)
if [[ -n "$DEPLOY_ROOT" ]]; then
  data_require_absolute deploy-root "$DEPLOY_ROOT"
  [[ ! -d "$(data_lock_dir "$DEPLOY_ROOT")" ]] || data_die "cannot prune while a data lifecycle operation is active" "$DATA_EXIT_LOCK"
fi
valid=(); for path in "${all[@]}"; do if "$SCRIPT_DIR/verify-backup.sh" "$path" >/dev/null 2>&1; then valid+=("$path"); fi; done
(( ${#valid[@]} > 0 )) || data_die "no verified backup may be pruned" "$DATA_EXIT_INCOMPLETE"
now="$(date +%s)"; kept=0
for path in "${valid[@]}"; do mtime="$(stat -f '%m' "$path")"; age_days=$(( (now - mtime) / 86400 )); if (( kept < KEEP_LAST || age_days <= KEEP_DAYS )); then kept=$((kept+1)); echo "KEEP $path"; continue; fi; if [[ "$DRY_RUN" == true ]]; then echo "PRUNE_DRY_RUN $path"; else rm -rf "$path"; echo "PRUNED $path"; fi; done
