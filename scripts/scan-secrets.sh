#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_DIR"

usage() {
  echo "Usage: scripts/scan-secrets.sh --tracked | --staged | --artifact <path>" >&2
  exit 64
}

[[ $# -ge 1 ]] || usage
mode="$1"
target=""
if [[ "$mode" == "--artifact" ]]; then
  [[ $# -eq 2 && -d "$2" ]] || usage
  target="$2"
elif [[ "$mode" != "--tracked" && "$mode" != "--staged" ]]; then
  usage
fi

# Real credentials only: documentation placeholders and source code that names a
# header do not match these patterns. Keep this list intentionally narrow.
pattern="(?i)-----BEGIN ([A-Z ]+)?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|Authorization:[[:space:]]*Bearer[[:space:]]+[A-Za-z0-9._~-]{24,}|PARTYGAME_OPERATOR_TOKEN=(?!REPLACE)[A-Za-z0-9._~-]{32,}|(password|secret)[[:space:]]*[:=][[:space:]]*[A-Za-z0-9._~+/-]{24,}"
exclude=(--glob '!**/.git/**' --glob '!**/node_modules/**' --glob '!**/DerivedData/**' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!scripts/scan-secrets.sh')

case "$mode" in
  --tracked)
    # Git supplies only tracked paths, so local runtime directories are not scanned.
    if git ls-files -z | xargs -0 rg -n --pcre2 "${exclude[@]}" -e "$pattern"; then
      echo "Potential secret found in tracked files." >&2
      exit 1
    fi
    ;;
  --staged)
    if git diff --cached --no-ext-diff -U0 | rg -n --pcre2 -e "$pattern"; then
      echo "Potential secret found in staged diff." >&2
      exit 1
    fi
    ;;
  --artifact)
    if rg -n --pcre2 "${exclude[@]}" -e "$pattern" "$target"; then
      echo "Potential secret found in release artifact." >&2
      exit 1
    fi
    ;;
esac

echo "Secret scan PASS: $mode"
