#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"
# shellcheck source=lib/diagnostics-common.sh
source "$SCRIPT_DIR/lib/diagnostics-common.sh"
lan_parse_arguments "$@"
lan_load_environment
for tool in curl node unzip; do command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 69; }; done
base="$(lan_url)"
headers=(-H "Authorization: Bearer $PARTYGAME_OPERATOR_TOKEN")
summary="$(curl --fail --silent --show-error "${headers[@]}" "$base/api/admin/diagnostics/summary")"
printf '%s' "$summary" | node -e 'let b=""; process.stdin.on("data", d=>b+=d); process.stdin.on("end",()=>{const x=JSON.parse(b); if(!x.version?.applicationVersion || !x.database || !x.logging) process.exit(1);})'
created="$(curl --fail --silent --show-error "${headers[@]}" -X POST "$base/api/admin/diagnostics/support-bundles?mode=minimal")"
id="$(printf '%s' "$created" | node -e 'let b=""; process.stdin.on("data",d=>b+=d); process.stdin.on("end",()=>process.stdout.write(JSON.parse(b).id||""))')"
[[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]] || { echo "invalid support bundle id" >&2; exit 1; }
work="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-diagnostics-smoke.XXXXXX")"; trap 'rm -rf "$work"' EXIT INT TERM
curl --fail --silent --show-error "${headers[@]}" "$base/api/admin/diagnostics/support-bundles/$id/download" -o "$work/support.zip"
unzip -q "$work/support.zip" -d "$work/support"
for required in support-manifest.json SUPPORT_INFO.txt diagnostics/summary.json version/version-contract.json; do
  [[ -f "$work/support/$required" ]] || { echo "API support bundle missing $required" >&2; exit 1; }
done
node -e 'const m=require(process.argv[1]); if(m.supportBundleFormatVersion!=="1" || !m.applicationVersion || !Array.isArray(m.omittedSections)) process.exit(1)' "$work/support/support-manifest.json"
if find "$work/support" -type f \( -iname '*.db' -o -iname '*.sqlite*' -o -iname '*.wal' -o -iname '*.shm' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.png' \) -print -quit | grep -q .; then echo "API support bundle contains forbidden data" >&2; exit 1; fi
diagnostics_assert_redacted "$work/support" || { echo "support bundle redaction failed" >&2; exit 1; }
echo "Diagnostics smoke PASS"
