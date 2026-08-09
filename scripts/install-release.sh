#!/usr/bin/env bash
# Installs a signed PartyGame package without requiring the source repository or an IDE.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
package=""; install_root=""; runtime_root=""; host=""; port="5050"; non_interactive=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --package) package="${2:-}"; shift 2 ;;
    --install-root) install_root="${2:-}"; shift 2 ;;
    --runtime-root) runtime_root="${2:-}"; shift 2 ;;
    --host) host="${2:-}"; shift 2 ;;
    --port) port="${2:-}"; shift 2 ;;
    --non-interactive) non_interactive=true; shift ;;
    *) echo "Usage: $0 --package FILE --install-root ABSOLUTE --host PRIVATE_IPV4 [--port N] [--runtime-root ABSOLUTE] [--non-interactive]" >&2; exit 64 ;;
  esac
done
[[ -n "$package" && -n "$install_root" && -n "$host" ]] || { echo "--package, --install-root and --host are required" >&2; exit 64; }
[[ "$install_root" = /* ]] || { echo "--install-root must be absolute" >&2; exit 64; }
runtime_root="${runtime_root:-$install_root/runtime}"
[[ "$runtime_root" = /* ]] || { echo "--runtime-root must be absolute" >&2; exit 64; }
[[ "$install_root" != / && "$runtime_root" != / ]] || { echo "refusing unsafe root path" >&2; exit 64; }
for tool in tar shasum node dotnet curl df; do command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 69; }; done
"$SCRIPT_DIR/verify-release-package.sh" --package "$package" >/dev/null
if [[ "$non_interactive" == true && -z "${PARTYGAME_OPERATOR_TOKEN:-}" ]]; then
  echo "PARTYGAME_OPERATOR_TOKEN is required with --non-interactive; the installer never creates an unrecoverable silent secret." >&2
  exit 64
fi
if [[ -z "${PARTYGAME_OPERATOR_TOKEN:-}" ]]; then
  [[ -t 0 ]] || { echo "PARTYGAME_OPERATOR_TOKEN is required when standard input is not interactive." >&2; exit 64; }
  read -r -s -p "PartyGame operator token (32+ chars): " PARTYGAME_OPERATOR_TOKEN; echo
fi
[[ ${#PARTYGAME_OPERATOR_TOKEN} -ge 32 ]] || { echo "operator token must be at least 32 characters" >&2; exit 64; }
available="$(df -Pk "$(dirname "$install_root")" | awk 'NR == 2 {print $4 * 1024}')"
[[ "$available" =~ ^[0-9]+$ ]] && (( available >= 104857600 )) || { echo "insufficient free space for installation" >&2; exit 70; }
stage="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-install.XXXXXX")"
created_release=""
cleanup() {
  code=$?
  rm -rf "$stage"
  if (( code != 0 )) && [[ -n "$created_release" && ! -L "$install_root/current" ]]; then rm -rf "$created_release"; fi
  exit "$code"
}
trap cleanup EXIT INT TERM
tar -xzf "$package" -C "$stage"
roots=("$stage"/partygame-*)
[[ ${#roots[@]} -eq 1 && -d "${roots[0]}" ]] || { echo "invalid package root" >&2; exit 65; }
package_root="${roots[0]}"
version="$(node "$package_root/scripts/release-assets.mjs" version "$package_root/release/manifest.json")"
mkdir -p "$install_root" "$runtime_root"
created_release="$install_root/releases/$version"
PARTYGAME_OPERATOR_TOKEN="$PARTYGAME_OPERATOR_TOKEN" "$package_root/scripts/deploy-lan.sh" \
  --deploy-root "$install_root" --runtime-root "$runtime_root" --release-dir "$package_root/release" --host "$host" --port "$port"
"$package_root/scripts/security-smoke.sh" "$install_root/current/api/PartyGame.Api.dll" >/dev/null
"$package_root/scripts/diagnostics-smoke.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
printf 'PartyGame installed safely. Display: http://%s:%s/display/\nAdmin: http://%s:%s/admin/\n' "$host" "$port" "$host" "$port"
