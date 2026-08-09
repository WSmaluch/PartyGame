#!/usr/bin/env bash
set -euo pipefail

package=""
while [[ $# -gt 0 ]]; do case "$1" in --package) package="${2:-}"; shift 2;; *) echo "Usage: $0 --package <partygame.tar.gz>" >&2; exit 64;; esac; done
[[ -n "$package" && -f "$package" ]] || { echo "--package must name an archive" >&2; exit 64; }
for tool in tar shasum node find; do command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 69; }; done
entries="$(tar -tzf "$package")"
[[ -n "$entries" ]] || { echo "package is empty" >&2; exit 65; }
if printf '%s\n' "$entries" | awk 'BEGIN { bad=0 } /^\// || /(^|\/)\.\.($|\/)/ { bad=1 } END { exit bad }'; then :; else echo "package contains unsafe path" >&2; exit 65; fi
stage="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-package-verify.XXXXXX")"
trap 'rm -rf "$stage"' EXIT INT TERM
tar -xzf "$package" -C "$stage"
[[ -z "$(find "$stage" -type l -print -quit)" ]] || { echo "package contains symlink" >&2; exit 65; }
roots=("$stage"/partygame-*)
[[ ${#roots[@]} -eq 1 && -d "${roots[0]}" ]] || { echo "package must contain exactly one partygame-<version> root" >&2; exit 65; }
root="${roots[0]}"
for file in package-manifest.json checksums.sha256 release/manifest.json release/checksums.sha256 scripts/install.sh; do [[ -f "$root/$file" ]] || { echo "package missing $file" >&2; exit 66; }; done
node -e 'const m=require(process.argv[1]); if(m.packageFormatVersion!==1 || !m.version || !m.releaseCommitHash || !m.runtimeOutsideRelease) process.exit(1)' "$root/package-manifest.json" || { echo "invalid package manifest" >&2; exit 65; }
(cd "$root" && shasum -a 256 -c checksums.sha256) >/dev/null
(cd "$root/release" && shasum -a 256 -c checksums.sha256) >/dev/null
echo "PACKAGE_VERIFY_PASS version=$(node -e 'process.stdout.write(require(process.argv[1]).version)' "$root/package-manifest.json")"
