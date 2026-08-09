#!/usr/bin/env bash
# Builds the source-free, transferable PartyGame RC package from a verified release artifact.
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
release_dir=""; output=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --release-dir) release_dir="${2:-}"; shift 2 ;;
    --output) output="${2:-}"; shift 2 ;;
    *) echo "Usage: $0 --release-dir <artifact-dir> [--output <tar.gz>]" >&2; exit 64 ;;
  esac
done
[[ -n "$release_dir" && -d "$release_dir" ]] || { echo "--release-dir must name a release artifact" >&2; exit 64; }
release_dir="$(cd "$release_dir" && pwd -P)"
[[ -f "$release_dir/manifest.json" && -f "$release_dir/checksums.sha256" ]] || { echo "release artifact is incomplete" >&2; exit 66; }
[[ -z "$(find "$release_dir" -type l -print -quit)" ]] || { echo "release artifact must not contain symlinks" >&2; exit 65; }
(cd "$release_dir" && shasum -a 256 -c checksums.sha256) >/dev/null
version="$(node "$REPO_DIR/scripts/release-assets.mjs" version "$release_dir/manifest.json")"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo "invalid release version" >&2; exit 65; }
output="${output:-$REPO_DIR/artifacts/packages/partygame-$version.tar.gz}"
mkdir -p "$(dirname "$output")"
stage="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-package.XXXXXX")"
root="$stage/partygame-$version"
cleanup() { rm -rf "$stage"; }
trap cleanup EXIT INT TERM
mkdir -p "$root"/{scripts/lib,config,docs,deployment/systemd,deployment/launchd}
cp -R "$release_dir" "$root/release"
for script in deploy-lan.sh start-lan.sh stop-lan.sh status-lan.sh restart-lan.sh smoke-lan.sh backup-data.sh restore-data.sh migrate-data.sh verify-backup.sh security-smoke.sh scan-secrets.sh create-support-bundle.sh verify-support-bundle.sh diagnose-lan.sh find-free-port.mjs release-assets.mjs diagnostics-smoke.sh uninstall-lan.sh render-autostart-template.sh; do
  cp "$REPO_DIR/scripts/$script" "$root/scripts/$script"
done
cp "$REPO_DIR/scripts/lib/lan-common.sh" "$REPO_DIR/scripts/lib/data-lifecycle-common.sh" "$REPO_DIR/scripts/lib/diagnostics-common.sh" "$root/scripts/lib/"
cp "$REPO_DIR/scripts/install-release.sh" "$root/scripts/install.sh"
cp "$REPO_DIR/scripts/start-lan.sh" "$root/scripts/start.sh"
cp "$REPO_DIR/scripts/stop-lan.sh" "$root/scripts/stop.sh"
cp "$REPO_DIR/scripts/status-lan.sh" "$root/scripts/status.sh"
cp "$REPO_DIR/scripts/restart-lan.sh" "$root/scripts/restart.sh"
cp "$REPO_DIR/scripts/diagnostics-smoke.sh" "$root/scripts/diagnose.sh"
cp "$REPO_DIR/scripts/backup-data.sh" "$root/scripts/backup.sh"
cp "$REPO_DIR/scripts/restore-data.sh" "$root/scripts/restore.sh"
cp "$REPO_DIR/scripts/uninstall-lan.sh" "$root/scripts/uninstall.sh"
cp -R "$REPO_DIR/deployment/." "$root/deployment/"
chmod 755 "$root/scripts"/*.sh
cat > "$root/config/partygame.env.example" <<'EOF'
# Copying this file is optional: install.sh creates config/partygame.env with mode 600.
# Trusted LAN HTTP requires an explicit install-time --host private IPv4 address.
# Never store a real PARTYGAME_OPERATOR_TOKEN in this example or commit it to Git.
PARTYGAME_LAN_PORT=5050
PARTYGAME_LOG_LEVEL=Information
PARTYGAME_LOG_FORMAT=json
EOF
for doc in INSTALL UPGRADE BACKUP_RESTORE SECURITY TROUBLESHOOTING; do
  case "$doc" in
    INSTALL) source_doc=install.md ;;
    UPGRADE) source_doc=upgrade.md ;;
    BACKUP_RESTORE) source_doc=backup_restore.md ;;
    SECURITY) source_doc=security.md ;;
    TROUBLESHOOTING) source_doc=troubleshooting.md ;;
  esac
  cp "$REPO_DIR/docs/installation/$source_doc" "$root/docs/$doc.md"
done
cp "$REPO_DIR/RELEASE_NOTES.md" "$root/RELEASE_NOTES.md"
node - "$root" "$version" <<'NODE'
const fs = require('fs'); const path = require('path');
const [root, version] = process.argv.slice(2);
function files(dir, prefix='') { return fs.readdirSync(dir, {withFileTypes:true}).flatMap(entry => { const rel = path.join(prefix, entry.name); const full = path.join(dir, entry.name); return entry.isDirectory() ? files(full, rel) : entry.isFile() ? [rel.split(path.sep).join('/')] : []; }); }
const release = JSON.parse(fs.readFileSync(path.join(root, 'release/manifest.json'), 'utf8'));
fs.writeFileSync(path.join(root, 'package-manifest.json'), JSON.stringify({ packageFormatVersion: 1, product: 'PartyGame', version, releaseManifest: 'release/manifest.json', releaseCommitHash: release.commitHash, requiredTools: ['dotnet','node','curl','shasum','tar'], runtimeOutsideRelease: true, scripts: ['install.sh','start.sh','stop.sh','status.sh','restart.sh','diagnose.sh','backup.sh','restore.sh','uninstall.sh'] }, null, 2) + '\n');
const crypto = require('crypto');
const included = files(root).filter(file => !['checksums.sha256', 'package-manifest.json'].includes(file)).sort();
const lines = included.map(file => `${crypto.createHash('sha256').update(fs.readFileSync(path.join(root,file))).digest('hex')}  ${file}`);
fs.writeFileSync(path.join(root, 'checksums.sha256'), lines.join('\n') + '\n');
NODE
tar -czf "$output" -C "$stage" "partygame-$version"
echo "PACKAGE_PATH=$output"
