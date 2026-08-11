#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
work="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-release-dependency-audit.XXXXXX")"
trap 'rm -rf "$work"' EXIT INT TERM
root="$work/partygame-test"
mkdir -p "$root/scripts"
cat > "$root/package-manifest.json" <<'JSON'
{"packageFormatVersion":1,"version":"test","requiredTools":["node","tar"]}
JSON
printf '%s\n' 'rg -q "token" api.log' > "$root/scripts/security-smoke.sh"
tar -czf "$work/undeclared-rg.tar.gz" -C "$work" partygame-test
if node "$SCRIPT_DIR/audit-release-dependencies.mjs" --package "$work/undeclared-rg.tar.gz" >/dev/null 2>&1; then
  echo "dependency audit accepted an undeclared rg command" >&2
  exit 1
fi
printf '%s\n' 'grep -q "token" api.log' > "$root/scripts/security-smoke.sh"
tar -czf "$work/declared-system-tool.tar.gz" -C "$work" partygame-test
node "$SCRIPT_DIR/audit-release-dependencies.mjs" --package "$work/declared-system-tool.tar.gz" >/dev/null
echo "Release dependency audit regression PASS"
