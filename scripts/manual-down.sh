#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/manual-common.sh
source "$SCRIPT_DIR/lib/manual-common.sh"
manual_parse_port "$@"

if ! manual_stop_port; then
  echo "PartyGame is not running."
  echo "Port $MANUAL_PORT is free."
  exit 0
fi

echo "PartyGame stopped."
echo "Port $MANUAL_PORT is free."
