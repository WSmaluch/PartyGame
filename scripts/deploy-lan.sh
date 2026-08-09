#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"

rollback_version=""
args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --rollback) rollback_version="${2:-}"; shift 2 ;;
    *) args+=("$1"); shift ;;
  esac
done
lan_parse_arguments "${args[@]}"
lan_resolve_host
lan_prepare_runtime
previous=""
[[ -L "$(lan_current_link)" ]] && previous="$(lan_current_release)"

switch_current() {
  local target="$1" temporary="$LAN_DEPLOY_ROOT/.current.$$" link
  link="$(lan_current_link)"
  [[ ! -e "$link" || -L "$link" ]] || lan_die "current path must be a symlink when it exists."
  ln -s "$target" "$temporary"
  # BSD mv follows a symlink to a directory, placing the temporary link inside
  # the old release instead of replacing `current`. Deployment is stopped at
  # every call site, so explicitly replacing this verified symlink is safe.
  rm -f "$link"
  mv "$temporary" "$link"
}
write_web_config() {
  local target="$1" version="$2" public; public="$(lan_url)"
  for app in display admin; do
    cat > "$target/$app/config.json" <<EOF
{
  "apiBaseUrl": "/",
  "signalRHubUrl": "/hubs/game",
  "publicBaseUrl": "/$app/",
  "applicationVersion": "$version",
  "signalRBaseUrl": "/",
  "publicAppUrl": "$public/$app/",
  "buildVersion": "$version"
}
EOF
  done
}
restore_previous() {
  local active_pid
  if active_pid="$(lan_read_pid)"; then
    lan_stop_deployment_pid "$active_pid" || true
    rm -f "$(lan_pid_file)" "$(lan_pid_meta_file)"
  fi
  if [[ -n "$previous" ]]; then
    switch_current "$previous"
    lan_write_environment
    "$SCRIPT_DIR/start-lan.sh" --deploy-root "$LAN_DEPLOY_ROOT" --runtime-root "$LAN_RUNTIME_ROOT" --host "$LAN_HOST" --port "$LAN_PORT" || true
  else
    rm -f "$(lan_current_link)"
  fi
}

check_schema_compatibility() {
  local release="$1"
  PARTYGAME_APPLY_MIGRATIONS=false dotnet "$release/api/PartyGame.Api.dll" check >/dev/null
}

if [[ -n "$rollback_version" ]]; then
  target="$LAN_DEPLOY_ROOT/releases/$rollback_version"
  [[ -d "$target" ]] || lan_die "rollback version does not exist: $rollback_version"
  lan_verify_installed_release "$target"
  "$SCRIPT_DIR/stop-lan.sh" --deploy-root "$LAN_DEPLOY_ROOT" --runtime-root "$LAN_RUNTIME_ROOT" --host "$LAN_HOST" --port "$LAN_PORT" || [[ $? -eq 1 ]]
  lan_load_environment
  if ! check_schema_compatibility "$target"; then
    echo "PartyGame LAN: rollback blocked because the current database is incompatible with $rollback_version." >&2
    exit 1
  fi
  switch_current "$target"
  lan_write_environment
  if ! "$SCRIPT_DIR/start-lan.sh" --deploy-root "$LAN_DEPLOY_ROOT" --runtime-root "$LAN_RUNTIME_ROOT" --host "$LAN_HOST" --port "$LAN_PORT" || ! "$SCRIPT_DIR/smoke-lan.sh" --deploy-root "$LAN_DEPLOY_ROOT" --runtime-root "$LAN_RUNTIME_ROOT" --host "$LAN_HOST" --port "$LAN_PORT"; then
    restore_previous; echo "PartyGame LAN: rollback failed and previous current was restored." >&2; exit 1
  fi
  lan_print_urls; exit 0
fi

[[ -n "$LAN_RELEASE_DIR" ]] || lan_die "--release-dir is required unless --rollback is used."
[[ "$LAN_RELEASE_DIR" = /* ]] || lan_die "release directory must be an absolute path."
lan_verify_release "$LAN_RELEASE_DIR"
version="$(lan_release_version "$LAN_RELEASE_DIR")"
target="$LAN_DEPLOY_ROOT/releases/$version"
if [[ -e "$target" ]]; then
  lan_verify_installed_release "$target"
  cmp -s "$LAN_RELEASE_DIR/manifest.json" "$target/manifest.json" || lan_die "release version $version already exists with different manifest."
  chmod u+w "$target/display/config.json" "$target/admin/config.json"
  write_web_config "$target" "$version"
  chmod a-w "$target/display/config.json" "$target/admin/config.json"
else
  staging="$(mktemp -d "$LAN_DEPLOY_ROOT/releases/.${version}.staging.XXXXXX")"
  trap 'rm -rf "$staging"' EXIT
  cp -R "$LAN_RELEASE_DIR/." "$staging/"
  write_web_config "$staging" "$version"
  chmod -R a-w "$staging/api" "$staging/display" "$staging/admin" || true
  mv "$staging" "$target"
  trap - EXIT
fi

"$SCRIPT_DIR/stop-lan.sh" --deploy-root "$LAN_DEPLOY_ROOT" --runtime-root "$LAN_RUNTIME_ROOT" --host "$LAN_HOST" --port "$LAN_PORT" || [[ $? -eq 1 ]]
if [[ -f "$(lan_runtime_dir)/database/partygame.db" ]]; then
  PARTYGAME_RUNTIME_ROOT="$LAN_RUNTIME_ROOT" "$SCRIPT_DIR/backup-data.sh" --deploy-root "$LAN_DEPLOY_ROOT" --backup-root "$LAN_DEPLOY_ROOT/backups" --maintenance
fi
switch_current "$target"
lan_write_environment
if ! PARTYGAME_RUNTIME_ROOT="$LAN_RUNTIME_ROOT" "$SCRIPT_DIR/migrate-data.sh" --deploy-root "$LAN_DEPLOY_ROOT" --backup-root "$LAN_DEPLOY_ROOT/backups" --migrate || ! "$SCRIPT_DIR/start-lan.sh" --deploy-root "$LAN_DEPLOY_ROOT" --runtime-root "$LAN_RUNTIME_ROOT" --host "$LAN_HOST" --port "$LAN_PORT" || ! "$SCRIPT_DIR/smoke-lan.sh" --deploy-root "$LAN_DEPLOY_ROOT" --runtime-root "$LAN_RUNTIME_ROOT" --host "$LAN_HOST" --port "$LAN_PORT"; then
  restore_previous
  echo "PartyGame LAN: deployment failed and previous current was restored." >&2
  exit 1
fi
lan_print_urls
