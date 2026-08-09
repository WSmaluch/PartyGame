#!/usr/bin/env bash
# Exercises active-release resolution without starting a PartyGame process.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/lan-common.sh"

root="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-active-release-resolver.XXXXXX")"
cleanup() { chmod -R u+w "$root" 2>/dev/null || true; rm -rf "$root"; }
trap cleanup EXIT INT TERM

make_release() {
  local directory="$1"
  mkdir -p "$directory"/{api,display,admin}
  : > "$directory/manifest.json"
  : > "$directory/BUILD_INFO.txt"
  : > "$directory/checksums.sha256"
  : > "$directory/api/PartyGame.Api.dll"
  : > "$directory/display/index.html"
  : > "$directory/admin/index.html"
}

expect_resolved() {
  local expected="$1" actual
  actual="$(resolve_current_release "$root")"
  [[ "$actual" == "$expected" ]] || { echo "unexpected active release resolution" >&2; exit 1; }
}

expect_rejected() {
  if resolve_current_release "$root" >/dev/null 2>&1; then
    echo "invalid active release was accepted" >&2
    exit 1
  fi
}

mkdir -p "$root/releases"
make_release "$root/releases/one"
ln -s releases/one "$root/current"
expect_resolved "$(cd -P "$root/releases/one" && pwd)"

rm "$root/current"
ln -s "$root/releases/one" "$root/current"
expect_resolved "$(cd -P "$root/releases/one" && pwd)"

rm "$root/current"
mkdir -p "$root/outside" "$root/releases-evil"
make_release "$root/outside/escape"
make_release "$root/releases-evil/prefix"
ln -s ../outside/escape "$root/current"
expect_rejected
rm "$root/current"
ln -s "$root/releases-evil/prefix" "$root/current"
expect_rejected
rm "$root/current"
ln -s releases/missing "$root/current"
expect_rejected
rm "$root/current"
: > "$root/current"
expect_rejected
rm "$root/current"
make_release "$root/releases/incomplete"
rm "$root/releases/incomplete/api/PartyGame.Api.dll"
ln -s releases/incomplete "$root/current"
expect_rejected

# A replacement link models the post-upgrade current-release switch.
rm "$root/current"
make_release "$root/releases/two"
ln -s releases/two "$root/current"
expect_resolved "$(cd -P "$root/releases/two" && pwd)"
echo "Active release resolver PASS"
