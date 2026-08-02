#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/diagnostics-common.sh
source "$SCRIPT_DIR/lib/diagnostics-common.sh"

LOG_ROOT="${PARTYGAME_LOG_DIRECTORY:-}"
KEEP_FILES=14
KEEP_DAYS=14
DRY_RUN=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --log-root) LOG_ROOT="${2:-}"; shift 2 ;;
    --keep-files) KEEP_FILES="${2:-}"; shift 2 ;;
    --keep-days) KEEP_DAYS="${2:-}"; shift 2 ;;
    --dry-run) DRY_RUN=true; shift ;;
    *) diagnostics_die "Usage: prune-logs.sh --log-root <absolute-dir> [--keep-files N] [--keep-days N] [--dry-run]" ;;
  esac
done
[[ "$KEEP_FILES" =~ ^[1-9][0-9]*$ && "$KEEP_DAYS" =~ ^[0-9]+$ ]] || diagnostics_die "retention values must be non-negative integers (keep-files at least 1)."
diagnostics_require_absolute_directory "$LOG_ROOT" "log root"

now="$(date +%s)"
index=0
while IFS= read -r file; do
  index=$((index + 1))
  mtime="$(stat -f %m "$file" 2>/dev/null || stat -c %Y "$file")"
  age_days=$(( (now - mtime) / 86400 ))
  if (( index <= KEEP_FILES || age_days <= KEEP_DAYS )); then continue; fi
  if [[ "$DRY_RUN" == true ]]; then
    printf 'DRY-RUN REMOVE %s\n' "$(basename "$file")"
  else
    rm -- "$file"
    printf 'REMOVED %s\n' "$(basename "$file")"
  fi
done < <(find "$LOG_ROOT" -type f -name 'partygame-*.log*' ! -type l -print 2>/dev/null | while IFS= read -r file; do diagnostics_is_safe_file "$LOG_ROOT" "$file" && printf '%s\n' "$file"; done | sort -r)
