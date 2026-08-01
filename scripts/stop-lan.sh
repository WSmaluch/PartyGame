#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"
lan_parse_arguments "$@"
if ! pid="$(lan_read_pid)"; then echo "PartyGame LAN: not running."; exit 0; fi
release="$(lan_current_release)"
if ! lan_pid_is_ours "$pid" "$release"; then
  echo "PartyGame LAN: PID $pid does not belong to current deployment; it was not signalled." >&2; exit 2
fi
kill -TERM "$pid"
deadline=$((SECONDS + LAN_WAIT_SECONDS))
while kill -0 "$pid" 2>/dev/null && (( SECONDS < deadline )); do sleep 1; done
if kill -0 "$pid" 2>/dev/null; then
  kill -KILL "$pid"
  sleep 1
fi
if kill -0 "$pid" 2>/dev/null; then echo "PartyGame LAN: process $pid did not stop." >&2; exit 1; fi
rm -f "$(lan_pid_file)" "$(lan_pid_meta_file)"
echo "PartyGame LAN: stopped."
