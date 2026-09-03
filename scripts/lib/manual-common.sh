#!/usr/bin/env bash
set -euo pipefail

MANUAL_PORT=5050
MANUAL_WAIT_SECONDS="${PARTYGAME_MANUAL_WAIT_SECONDS:-8}"

manual_parse_port() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --port) MANUAL_PORT="${2:-}"; shift 2 ;;
      *) echo "Usage: $0 [--port 5050]" >&2; exit 64 ;;
    esac
  done
  [[ "$MANUAL_PORT" =~ ^[1-9][0-9]{0,4}$ ]] && (( MANUAL_PORT <= 65535 )) || { echo "Port must be in 1..65535." >&2; exit 64; }
}

manual_listener_pids() {
  command -v lsof >/dev/null 2>&1 || { echo "lsof is required to inspect port $MANUAL_PORT." >&2; exit 69; }
  lsof -nP -tiTCP:"$MANUAL_PORT" -sTCP:LISTEN 2>/dev/null | sort -u
}

manual_port_is_free() { [[ -z "$(manual_listener_pids)" ]]; }

manual_stop_port() {
  local pids pid command deadline
  pids="$(manual_listener_pids)"
  [[ -n "$pids" ]] || return 1
  echo "Port $MANUAL_PORT is occupied"
  while IFS= read -r pid; do
    [[ "$pid" =~ ^[1-9][0-9]*$ ]] || continue
    command="$(ps -p "$pid" -o command= 2>/dev/null || echo unknown)"
    echo "PID: $pid"
    echo "Command: $command"
    echo "Stopping it..."
    kill -TERM "$pid" 2>/dev/null || true
  done <<< "$pids"

  deadline=$((SECONDS + MANUAL_WAIT_SECONDS))
  while ! manual_port_is_free && (( SECONDS < deadline )); do sleep 1; done
  if ! manual_port_is_free; then
    while IFS= read -r pid; do
      [[ "$pid" =~ ^[1-9][0-9]*$ ]] || continue
      echo "PID $pid did not stop after SIGTERM; sending SIGKILL."
      kill -KILL "$pid" 2>/dev/null || true
    done <<< "$(manual_listener_pids)"
    sleep 1
  fi
  manual_port_is_free || { echo "Port $MANUAL_PORT is still occupied." >&2; exit 1; }
  return 0
}

manual_detect_host() {
  if [[ -n "${PARTYGAME_MANUAL_HOST:-}" ]]; then
    MANUAL_HOST="$PARTYGAME_MANUAL_HOST"
  else
    local interface
    interface="$(route get default 2>/dev/null | awk '/interface:/{print $2; exit}')"
    [[ -n "$interface" ]] || { echo "Could not detect the default-route interface." >&2; exit 69; }
    MANUAL_HOST="$(ipconfig getifaddr "$interface" 2>/dev/null || true)"
  fi
  [[ "$MANUAL_HOST" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || { echo "Detected LAN address is not IPv4: ${MANUAL_HOST:-empty}" >&2; exit 69; }
  [[ "$MANUAL_HOST" != 127.* && "$MANUAL_HOST" != "0.0.0.0" ]] || { echo "Detected LAN address is loopback: $MANUAL_HOST" >&2; exit 69; }
  local octet
  IFS=. read -r -a octets <<< "$MANUAL_HOST"
  for octet in "${octets[@]}"; do (( 10#$octet >= 0 && 10#$octet <= 255 )) || { echo "Detected LAN address is invalid: $MANUAL_HOST" >&2; exit 69; }; done
  echo "LAN IP: $MANUAL_HOST"
}
