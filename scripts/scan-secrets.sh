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

case "$mode" in
  --tracked)
    # Git supplies only tracked paths, so local runtime directories are not scanned.
    if ! git ls-files -z | node "$REPO_DIR/scripts/secret-scan.mjs" --files0; then
      echo "Potential secret found in tracked files." >&2
      exit 1
    fi
    ;;
  --staged)
    if ! git diff --cached --no-ext-diff -U0 | node "$REPO_DIR/scripts/secret-scan.mjs" --stdin; then
      echo "Potential secret found in staged diff." >&2
      exit 1
    fi
    ;;
  --artifact)
    if ! node "$REPO_DIR/scripts/secret-scan.mjs" --directory "$target"; then
      echo "Potential secret found in release artifact." >&2
      exit 1
    fi
    ;;
esac

echo "Secret scan PASS: $mode"
