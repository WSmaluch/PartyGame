#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_DIR"
ALLOW_DIRTY=false
if [[ "${1:-}" == "--allow-dirty" ]]; then ALLOW_DIRTY=true; shift; fi
[[ $# -eq 0 ]] || { echo "Usage: scripts/build-release.sh [--allow-dirty]" >&2; exit 64; }

for tool in git dotnet node npm xcodebuild curl shasum; do command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 69; }; done
if [[ "$ALLOW_DIRTY" != true ]] && [[ -n "$(git status --porcelain)" ]]; then
  echo "Refusing release build from a dirty working tree. Use --allow-dirty only for local development." >&2
  exit 65
fi

SHORT_HASH="$(git rev-parse --short=12 HEAD)"
TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
VERSION="${PARTYGAME_RELEASE_VERSION:-0.8.1-$SHORT_HASH}"
RELEASE_DIR="$REPO_DIR/artifacts/release/$VERSION"
IOS_DERIVED_DATA="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-release-ios.XXXXXX")"
cleanup_ios_derived_data() { rm -rf "$IOS_DERIVED_DATA"; }
trap cleanup_ios_derived_data EXIT
rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"

dotnet restore PartyGame.sln
dotnet test PartyGame.sln --no-restore --configuration Release

dotnet publish server/PartyGame.Api/PartyGame.Api.csproj --no-restore --configuration Release --output "$RELEASE_DIR/api" \
  -p:Version="$VERSION" \
  -p:InformationalVersion="$VERSION" \
  -p:SourceRevisionId="$SHORT_HASH" \
  -p:PartyGameBuildTimestampUtc="$TIMESTAMP"

for app in display-web admin-web; do
  npm --prefix "apps/$app" ci
  PARTYGAME_BUILD_VERSION="$VERSION" npm --prefix "apps/$app" run lint
  PARTYGAME_BUILD_VERSION="$VERSION" npm --prefix "apps/$app" run test
  PARTYGAME_BUILD_VERSION="$VERSION" npm --prefix "apps/$app" run build
  cp -R "apps/$app/dist" "$RELEASE_DIR/${app%-web}"
done

node "$REPO_DIR/scripts/release-assets.mjs" config "$RELEASE_DIR/display/config.json" "${PARTYGAME_PUBLIC_BASE_URL:-}" "${PARTYGAME_DISPLAY_PUBLIC_URL:-}" "$VERSION"
node "$REPO_DIR/scripts/release-assets.mjs" config "$RELEASE_DIR/admin/config.json" "${PARTYGAME_PUBLIC_BASE_URL:-}" "${PARTYGAME_ADMIN_PUBLIC_URL:-}" "$VERSION"

export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
export PATH="$DEVELOPER_DIR/usr/bin:$PATH"
IOS_DESTINATION="${IOS_DESTINATION_ID:?IOS_DESTINATION_ID is required for the Release build-for-testing.}"
xcodebuild -project apps/ios/PartyGame.xcodeproj -scheme PartyGame -configuration Release \
  -destination "platform=iOS Simulator,id=$IOS_DESTINATION" \
  -derivedDataPath "$IOS_DERIVED_DATA" \
  build-for-testing

node "$REPO_DIR/scripts/release-assets.mjs" manifest "$RELEASE_DIR" "$VERSION" "$SHORT_HASH" "$TIMESTAMP" "$(dotnet --version)" "$(node --version)" "$(npm --version)"
bash "$REPO_DIR/scripts/smoke-release.sh" "$RELEASE_DIR"

if find "$RELEASE_DIR" \( -name node_modules -o -name DerivedData -o -name '*.db' -o -name '*.log' \) -print -quit | grep -q .; then
  echo "Release artifact contains a forbidden development or runtime file." >&2
  exit 1
fi
echo "Release build PASS: $RELEASE_DIR"
