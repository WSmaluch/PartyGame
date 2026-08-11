#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/data-lifecycle-common.sh"
source "$SCRIPT_DIR/lib/lan-common.sh"
DEPLOY_ROOT=""; BACKUP_ROOT=""; MODE="migrate"
while [[ $# -gt 0 ]]; do case "$1" in --deploy-root) DEPLOY_ROOT="${2:-}"; shift 2;; --backup-root) BACKUP_ROOT="${2:-}"; shift 2;; --check) MODE="check"; shift;; --migrate) MODE="migrate"; shift;; --migrate-on-start) MODE="migrate-on-start"; shift;; *) data_die "usage: migrate-data.sh --deploy-root PATH --backup-root PATH [--check|--migrate|--migrate-on-start]";; esac; done
[[ -n "$DEPLOY_ROOT" && -n "$BACKUP_ROOT" ]] || data_die "--deploy-root and --backup-root are required"; data_require_absolute deploy-root "$DEPLOY_ROOT"; data_require_absolute backup-root "$BACKUP_ROOT"
API_DLL="$DEPLOY_ROOT/current/api/PartyGame.Api.dll"; [[ -f "$API_DLL" ]] || data_die "installed API release is missing" "$DATA_EXIT_INCOMPLETE"
LAN_DEPLOY_ROOT="$DEPLOY_ROOT"; lan_load_environment
if [[ "$MODE" == check ]]; then PARTYGAME_APPLY_MIGRATIONS=false dotnet "$API_DLL" check; exit $?; fi
if [[ "$MODE" == migrate-on-start ]]; then
  echo "migrate-on-start is opt-in only: set PARTYGAME_APPLY_MIGRATIONS=true for a stopped, backed-up deployment. deploy-lan.sh uses the explicit migrate mode by default."
  exit 0
fi
data_acquire_lock "$DEPLOY_ROOT" "migration"; trap data_release_lock EXIT
DB="$(data_database_path "$DEPLOY_ROOT")"
if [[ -f "$DB" ]]; then
  before="$(PARTYGAME_APPLY_MIGRATIONS=false dotnet "$API_DLL" check)"
  source_schema="$(printf '%s' "$before" | node -e 'let body=""; process.stdin.on("data", chunk => body += chunk); process.stdin.on("end", () => { try { const value=JSON.parse(body).DatabaseSchemaVersion; if (typeof value !== "string" || !value) process.exit(1); process.stdout.write(value); } catch { process.exit(1); } })')"
  target_schema="$(printf '%s' "$before" | node -e 'let body=""; process.stdin.on("data", chunk => body += chunk); process.stdin.on("end", () => { try { const value=JSON.parse(body).LatestSupportedSchemaVersion; if (typeof value !== "string" || !value) process.exit(1); process.stdout.write(value); } catch { process.exit(1); } })')"
  if [[ "$source_schema" != "$target_schema" ]]; then
    pre_output="$(DATA_LOCK_PARENT=true PARTYGAME_APPLICATION_VERSION="${PARTYGAME_DEPLOYMENT_VERSION:-unknown}" "$SCRIPT_DIR/backup-data.sh" --deploy-root "$DEPLOY_ROOT" --backup-root "$BACKUP_ROOT" --maintenance --name "$(date -u +%Y%m%dT%H%M%SZ)-$$-pre-migration")"
    pre_backup="${pre_output#BACKUP_PATH=}"
    echo "PRE_MIGRATION_BACKUP=$pre_backup sourceSchema=$source_schema targetSchema=$target_schema"
  fi
fi
PARTYGAME_APPLY_MIGRATIONS=false dotnet "$API_DLL" migrate
