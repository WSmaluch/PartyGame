#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/lan-common.sh
source "$SCRIPT_DIR/lib/lan-common.sh"
lan_parse_arguments "$@"
lan_prepare_runtime
lan_load_environment
release="$(lan_current_release)"
lan_assert_release_layout "$release"

if pid="$(lan_read_pid)"; then
  if lan_pid_is_ours "$pid" "$release"; then
    if lan_wait_ready "$(lan_url)"; then lan_print_urls; exit 0; fi
    echo "PartyGame LAN: own API process $pid exists but is not ready." >&2; exit 3
  fi
  echo "PartyGame LAN: PID file exists but does not belong to current deployment." >&2; exit 2
fi
lan_port_is_free || { echo "PartyGame LAN: port $LAN_PORT is already in use." >&2; exit 1; }

log_file="$(lan_runtime_dir)/logs/partygame-api-$(date -u +%Y%m%dT%H%M%SZ).log"
api_dll="$release/api/PartyGame.Api.dll"
(
  cd "$release/api"
  export ASPNETCORE_ENVIRONMENT=Production
  exec dotnet "$api_dll"
) >>"$log_file" 2>&1 &
pid=$!
tmp_pid="$(lan_pid_file).tmp.$$"
printf '%s\n' "$pid" > "$tmp_pid"
mv -f "$tmp_pid" "$(lan_pid_file)"
printf 'pid=%s\nrelease=%s\nlog=%s\n' "$pid" "$release" "$log_file" > "$(lan_pid_meta_file)"

if lan_wait_ready "$(lan_url)"; then lan_print_urls; exit 0; fi
if lan_pid_is_ours "$pid" "$release"; then kill -TERM "$pid" 2>/dev/null || true; fi
rm -f "$(lan_pid_file)" "$(lan_pid_meta_file)"
echo "PartyGame LAN: readiness timed out; see $log_file" >&2
exit 1
