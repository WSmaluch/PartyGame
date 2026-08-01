#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"
lan_parse_arguments "$@"
if ! pid="$(lan_read_pid)"; then echo "PartyGame LAN: stopped."; exit 1; fi
release="$(lan_current_release)"
if ! lan_pid_is_ours "$pid" "$release"; then echo "PartyGame LAN: PID $pid is stale or belongs to another process." >&2; exit 2; fi
if lan_wait_ready "$(lan_url)"; then echo "PartyGame LAN: running (PID $pid), readiness PASS."; exit 0; fi
echo "PartyGame LAN: process $pid is running, readiness FAIL." >&2; exit 3
