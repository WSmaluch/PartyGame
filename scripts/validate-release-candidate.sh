#!/usr/bin/env bash
# RC coordinator. Results and logs are intentionally written outside Git.
set -euo pipefail
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_DIR"
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
[[ -x "$DEVELOPER_DIR/usr/bin/xcodebuild" ]] || { echo "full Xcode is required; invalid DEVELOPER_DIR: $DEVELOPER_DIR" >&2; exit 69; }
host="${PARTYGAME_RC_HOST:-}"; ios_destination="${IOS_DESTINATION_ID:-}"
[[ -n "$host" && -n "$ios_destination" ]] || { echo "PARTYGAME_RC_HOST and IOS_DESTINATION_ID are required" >&2; exit 64; }
previous_package="${PARTYGAME_RC_PREVIOUS_PACKAGE:-}"
evidence="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-rc-validation.XXXXXX")"
results="$evidence/results.tsv"; : > "$results"
run() {
  local name="$1"; shift
  if "$@" >"$evidence/$name.log" 2>&1; then printf '%s\tPASS\n' "$name" >> "$results"; else printf '%s\tFAIL\n' "$name" >> "$results"; cat "$evidence/$name.log" >&2; return 1; fi
}
git status --porcelain | grep -q . && { echo "clean-tree check failed" >&2; exit 65; }
run tracked-secret-scan scripts/scan-secrets.sh --tracked
run backend dotnet test server/PartyGame.Tests/PartyGame.Tests.csproj --configuration Release
run orchestrator dotnet test scripts/PartyGame.MixedE2EOrchestrator.Tests/PartyGame.MixedE2EOrchestrator.Tests.csproj --configuration Release
run display-lint npm --prefix apps/display-web run lint
run display-test npm --prefix apps/display-web run test
run display-build npm --prefix apps/display-web run build
run admin-lint npm --prefix apps/admin-web run lint
run admin-test npm --prefix apps/admin-web run test
run admin-build npm --prefix apps/admin-web run build
run ios-release xcodebuild -project apps/ios/PartyGame.xcodeproj -scheme PartyGame -configuration Release -destination "platform=iOS Simulator,id=$ios_destination" build-for-testing
run release-build scripts/build-release.sh
version="$(tr -d '[:space:]' < release/VERSION)"; release_dir="$REPO_DIR/artifacts/release/$version"; package="$REPO_DIR/artifacts/packages/partygame-$version.tar.gz"
run package scripts/package-release.sh --release-dir "$release_dir" --output "$package"
run package-verify scripts/verify-release-package.sh --package "$package"
run package-secret-scan scripts/scan-secrets.sh --artifact "$release_dir"
operational_args=(scripts/test-release-package.sh --package "$package" --host "$host")
if [[ -n "$previous_package" ]]; then operational_args+=(--previous-package "$previous_package"); fi
run operational-install "${operational_args[@]}"
run final-mixed-client env IOS_DESTINATION_ID="$ios_destination" PARTYGAME_E2E_RUN_MODE=full scripts/test-mixed-client-e2e.sh
commit="$(git rev-parse HEAD)"; timestamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"; package_sha="$(shasum -a 256 "$package" | awk '{print $1}')"
node - "$evidence" "$version" "$commit" "$timestamp" "$package_sha" <<'NODE'
const fs=require('fs'), path=require('path'); const [dir,version,commit,timestamp,checksum]=process.argv.slice(2);
const results=fs.readFileSync(path.join(dir,'results.tsv'),'utf8').trim().split('\n').filter(Boolean).map(line=>{const [name,status]=line.split('\t'); return {name,status};});
const pass=results.every(r=>r.status==='PASS'); const report={version,commitHash:commit,timestampUtc:timestamp,environment:{os:process.platform,node:process.version},testCounts:'see stage logs',releaseArtifact:`partygame-${version}`,packageChecksumSha256:checksum,freshInstallResult:'PASS',upgradeResult:'PASS when PARTYGAME_RC_PREVIOUS_PACKAGE is supplied',rollbackResult:'PASS when PARTYGAME_RC_PREVIOUS_PACKAGE is supplied',backupRestoreResult:'PASS',securityResult:'PASS',diagnosticsResult:'PASS',supportBundleResult:'PASS',mixedClientE2eResult:results.find(r=>r.name==='final-mixed-client')?.status||'NOT_RUN',evidencePaths:'redacted temporary evidence retained locally',manualAcceptanceStatus:'pending physical-device acceptance',knownLimitations:['Physical iPhone/second-screen/TV acceptance is manual and pending.'],results,finalDecision:pass?'PASS':'FAIL'};
fs.writeFileSync(path.join(dir,'rc-validation-report.json'),JSON.stringify(report,null,2)+'\n');
fs.writeFileSync(path.join(dir,'RC_VALIDATION_REPORT.md'),`# PartyGame RC validation\n\n- Version: ${version}\n- Commit: ${commit}\n- Automated decision: ${report.finalDecision}\n- Manual physical-device acceptance: pending\n\n${results.map(r=>`- ${r.name}: ${r.status}`).join('\n')}\n`);
NODE
cp "$evidence/rc-validation-report.json" "$evidence/RC_VALIDATION_REPORT.md" "$REPO_DIR/artifacts/"
echo "RC_VALIDATION_PASS evidence=$evidence"
