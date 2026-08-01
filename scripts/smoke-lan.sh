#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"
lan_parse_arguments "$@"
lan_load_environment
base="$(lan_url)"
for path in /health /health/ready /api/system/version /display/ /admin/ /display/config.json /admin/config.json /hubs/game/negotiate?negotiateVersion=1; do
  curl --fail --silent --show-error "$base$path" >/dev/null
done
if curl --fail --silent "$base/display/config.json" | grep -Eq 'localhost|127\.0\.0\.1'; then
  echo "PartyGame LAN: Display config contains a loopback address." >&2; exit 1
fi
if curl --fail --silent "$base/admin/config.json" | grep -Eq 'localhost|127\.0\.0\.1'; then
  echo "PartyGame LAN: Admin config contains a loopback address." >&2; exit 1
fi
echo "PartyGame LAN smoke PASS: $base"
