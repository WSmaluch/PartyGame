#!/usr/bin/env bash
# Removes deployment code by default while preserving operator runtime data and backups.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"
deploy_root=""; runtime_root=""; host=""; port="5050"; purge=false; confirmed=false; non_interactive=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --deploy-root) deploy_root="${2:-}"; shift 2 ;;
    --runtime-root) runtime_root="${2:-}"; shift 2 ;;
    --host) host="${2:-}"; shift 2 ;;
    --port) port="${2:-}"; shift 2 ;;
    --purge-data) purge=true; shift ;;
    --confirm-purge) confirmed=true; shift ;;
    --non-interactive) non_interactive=true; shift ;;
    *) echo "Usage: $0 --deploy-root ABSOLUTE --host PRIVATE_IPV4 [--runtime-root ABSOLUTE] [--purge-data --confirm-purge --non-interactive]" >&2; exit 64 ;;
  esac
done
lan_parse_arguments --deploy-root "$deploy_root" --runtime-root "${runtime_root:-$deploy_root/runtime}" --host "$host" --port "$port"
[[ "$LAN_DEPLOY_ROOT" != / && "$LAN_RUNTIME_ROOT" != / ]] || lan_die "refusing unsafe root path"
if [[ "$purge" == true && ( "$confirmed" != true || "$non_interactive" != true ) ]]; then
  lan_die "--purge-data requires both --confirm-purge and --non-interactive"
fi
"$SCRIPT_DIR/stop-lan.sh" --deploy-root "$LAN_DEPLOY_ROOT" --runtime-root "$LAN_RUNTIME_ROOT" --host "$LAN_HOST" --port "$LAN_PORT" >/dev/null 2>&1 || [[ $? -eq 1 ]]
rm -f "$LAN_DEPLOY_ROOT/current"
chmod -R u+w "$LAN_DEPLOY_ROOT/releases" 2>/dev/null || true
rm -rf "$LAN_DEPLOY_ROOT/releases" "$LAN_DEPLOY_ROOT/config"
if [[ "$purge" == true ]]; then
  rm -rf "$LAN_RUNTIME_ROOT" "$LAN_DEPLOY_ROOT/backups"
  echo "PartyGame uninstall PASS: release and fixture runtime data removed."
else
  echo "PartyGame uninstall PASS: release removed; runtime, database, media and backups were preserved."
fi
