#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/diagnostics-common.sh
source "$SCRIPT_DIR/lib/diagnostics-common.sh"
BUNDLE=""; DIRECTORY=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --bundle) BUNDLE="${2:-}"; shift 2 ;;
    --directory) DIRECTORY="${2:-}"; shift 2 ;;
    *) diagnostics_die "Usage: verify-support-bundle.sh --bundle <tar.gz> | --directory <staging-dir>" ;;
  esac
done
[[ -n "$BUNDLE" || -n "$DIRECTORY" ]] || diagnostics_die "a bundle or directory is required."
TMP=""; cleanup() { if [[ -n "$TMP" ]]; then rm -rf "$TMP"; fi; }; trap cleanup EXIT
if [[ -n "$BUNDLE" ]]; then
  [[ -f "$BUNDLE" && ! -L "$BUNDLE" ]] || diagnostics_die "bundle must be a regular file."
  TMP="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-verify.XXXXXX")"
  tar -tzf "$BUNDLE" | while IFS= read -r entry; do [[ "$entry" != /* && "$entry" != *"../"* ]] || diagnostics_die "archive contains unsafe path"; done
  tar -xzf "$BUNDLE" -C "$TMP"
  DIRECTORY="$TMP"
fi
[[ -d "$DIRECTORY" && ! -L "$DIRECTORY" ]] || diagnostics_die "directory must be a non-symlink directory."
for required in support-manifest.json SUPPORT_INFO.txt checksums.sha256 version diagnostics configuration logs deployment database backup network; do [[ -e "$DIRECTORY/$required" ]] || diagnostics_die "missing bundle entry: $required"; done
node -e 'const m=require(process.argv[1]); if(m.supportBundleFormatVersion!=="1" || !m.createdAtUtc || !m.applicationVersion || !Array.isArray(m.includedSections)) process.exit(1)' "$DIRECTORY/support-manifest.json" || diagnostics_die "invalid support manifest"
(cd "$DIRECTORY" && shasum -a 256 -c checksums.sha256) >/dev/null || diagnostics_die "checksum mismatch"
if find "$DIRECTORY" -type l -print -quit | grep -q .; then diagnostics_die "bundle contains a symlink"; fi
if find "$DIRECTORY" -type f \( -iname '*.db' -o -iname '*.sqlite*' -o -iname '*.wal' -o -iname '*.shm' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.png' \) -print -quit | grep -q .; then diagnostics_die "bundle contains forbidden database or media data"; fi
diagnostics_assert_redacted "$DIRECTORY" || diagnostics_die "bundle secret scan failed"
printf 'PASS support bundle verification\n'
