#!/usr/bin/env bash
set -euo pipefail
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
template=""; output=""; install_root=""; runtime_root=""; host=""; port="5050"
while [[ $# -gt 0 ]]; do case "$1" in
  --template) template="${2:-}"; shift 2;; --output) output="${2:-}"; shift 2;; --install-root) install_root="${2:-}"; shift 2;; --runtime-root) runtime_root="${2:-}"; shift 2;; --host) host="${2:-}"; shift 2;; --port) port="${2:-}"; shift 2;;
  *) echo "Usage: $0 --template systemd|launchd --output FILE --install-root ABS --runtime-root ABS --host IP [--port N]" >&2; exit 64;; esac; done
[[ "$template" == systemd || "$template" == launchd ]] || { echo "--template must be systemd or launchd" >&2; exit 64; }
[[ -n "$output" && "$install_root" = /* && "$runtime_root" = /* && -n "$host" ]] || { echo "output and absolute roots plus host are required" >&2; exit 64; }
source_file="$REPO_DIR/deployment/systemd/partygame.service"
[[ "$template" == launchd ]] && source_file="$REPO_DIR/deployment/launchd/com.partygame.server.plist"
[[ -f "$source_file" ]] || { echo "template not found" >&2; exit 66; }
escape() { printf '%s' "$1" | sed 's/[&|]/\\&/g'; }
sed -e "s|{{INSTALL_ROOT}}|$(escape "$install_root")|g" -e "s|{{RUNTIME_ROOT}}|$(escape "$runtime_root")|g" -e "s|{{HOST}}|$(escape "$host")|g" -e "s|{{PORT}}|$(escape "$port")|g" "$source_file" > "$output"
if [[ "$template" == launchd ]] && command -v plutil >/dev/null; then plutil -lint "$output" >/dev/null; fi
if [[ "$template" == systemd ]] && command -v systemd-analyze >/dev/null; then systemd-analyze verify "$output" >/dev/null; fi
echo "Autostart template rendered: $output"
