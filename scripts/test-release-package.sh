#!/usr/bin/env bash
# End-to-end operational test. It uses only temporary deployment and runtime roots.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
package=""; previous_package=""; host=""; port=""
while [[ $# -gt 0 ]]; do case "$1" in
  --package) package="${2:-}"; shift 2;; --previous-package) previous_package="${2:-}"; shift 2;; --host) host="${2:-}"; shift 2;; --port) port="${2:-}"; shift 2;;
  *) echo "Usage: $0 --package RC.tar.gz --host PRIVATE_IPV4 [--previous-package previous.tar.gz] [--port N]" >&2; exit 64;; esac; done
[[ -n "$package" && -n "$host" ]] || { echo "--package and --host are required" >&2; exit 64; }
[[ -f "$package" ]] || { echo "package not found" >&2; exit 66; }
port="${port:-$(node "$SCRIPT_DIR/find-free-port.mjs")}"
root="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-rc-install.XXXXXX")"
install_root="$root/install"; runtime_root="$root/runtime"; backups="$root/backups"
token="rc-validation-$(node -e 'process.stdout.write(require("crypto").randomBytes(24).toString("hex"))')"
step() { printf 'RC package test step: %s\n' "$1" >&2; }
cleanup() {
  code=$?
  "$SCRIPT_DIR/uninstall-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null 2>&1 || true
  if (( code == 0 )); then
    chmod -R u+w "$root" 2>/dev/null || true
    rm -rf "$root"
  else
    echo "RC_PACKAGE_TEST_EVIDENCE=$root" >&2
  fi
  exit "$code"
}
trap cleanup EXIT INT TERM
run_install() {
  local archive="$1" skip_postchecks="${2:-false}"
  PARTYGAME_OPERATOR_TOKEN="$token" PARTYGAME_INSTALL_SKIP_POSTCHECKS="$skip_postchecks" "$SCRIPT_DIR/install-release.sh" --package "$archive" --install-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" --non-interactive >/dev/null
}
step verify-rc-package
"$SCRIPT_DIR/verify-release-package.sh" --package "$package" >/dev/null
if [[ -n "$previous_package" ]]; then
  step verify-previous-package
  "$SCRIPT_DIR/verify-release-package.sh" --package "$previous_package" >/dev/null
  # The historical release predates collision-resistant support-bundle names.
  # The RC installation below runs the required security and diagnostics smoke.
  step install-previous
  run_install "$previous_package" true
fi
step install-rc
run_install "$package"
step status-and-smoke
"$SCRIPT_DIR/status-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
"$SCRIPT_DIR/smoke-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
step diagnostics-and-support-bundle
"$SCRIPT_DIR/diagnostics-smoke.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
PARTYGAME_RUNTIME_ROOT="$runtime_root" "$SCRIPT_DIR/create-support-bundle.sh" --deploy-root "$install_root" --mode standard >/dev/null
support_bundle="$(find "$runtime_root/support-bundles" -maxdepth 1 -type f -name 'partygame-support-*.tar.gz' -print -quit)"
[[ -n "$support_bundle" ]] || { echo "support bundle was not created in external runtime root" >&2; exit 1; }
"$SCRIPT_DIR/verify-support-bundle.sh" --bundle "$support_bundle" >/dev/null
step backup-and-disaster-recovery
PARTYGAME_RUNTIME_ROOT="$runtime_root" "$SCRIPT_DIR/backup-data.sh" --deploy-root "$install_root" --backup-root "$backups" --maintenance --name rc-pre-restore >/dev/null
backup="$backups/rc-pre-restore"
"$SCRIPT_DIR/stop-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
mv "$runtime_root/database/partygame.db" "$runtime_root/database/partygame.db.loss-simulation"
mv "$runtime_root/media" "$runtime_root/media.loss-simulation"
PARTYGAME_RUNTIME_ROOT="$runtime_root" "$SCRIPT_DIR/restore-data.sh" --deploy-root "$install_root" --backup "$backup" --backup-root "$backups" --force >/dev/null
rm -f "$runtime_root/database/partygame.db.loss-simulation"; rm -rf "$runtime_root/media.loss-simulation"
"$SCRIPT_DIR/start-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
"$SCRIPT_DIR/smoke-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
if [[ -n "$previous_package" ]]; then
  step rollback
  previous_root="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-previous-package.XXXXXX")"
  tar -xzf "$previous_package" -C "$previous_root"
  previous_manifest="$(find "$previous_root" -path '*/release/manifest.json' -type f -print -quit)"
  previous_version="$(node "$SCRIPT_DIR/release-assets.mjs" version "$previous_manifest")"
  "$SCRIPT_DIR/deploy-lan.sh" --rollback "$previous_version" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
  "$SCRIPT_DIR/smoke-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
  rm -rf "$previous_root"
fi
step uninstall
"$SCRIPT_DIR/uninstall-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" >/dev/null
[[ -f "$runtime_root/database/partygame.db" && -d "$runtime_root/media" && -d "$backups" ]] || { echo "uninstall did not preserve runtime" >&2; exit 1; }
"$SCRIPT_DIR/uninstall-lan.sh" --deploy-root "$install_root" --runtime-root "$runtime_root" --host "$host" --port "$port" --purge-data --confirm-purge --non-interactive >/dev/null
[[ ! -e "$runtime_root" && ! -e "$install_root/backups" ]] || { echo "purge uninstall did not remove fixture data" >&2; exit 1; }
echo "RC_PACKAGE_TEST_PASS fresh-install=PASS backup-restore=PASS disaster-recovery=PASS uninstall=PASS"
