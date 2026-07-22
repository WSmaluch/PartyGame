#!/usr/bin/env bash
set -euo pipefail

export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
export PATH="$DEVELOPER_DIR/usr/bin:/usr/local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin"
/usr/bin/xcrun --find simctl >/dev/null
/usr/bin/xcrun simctl list devices available >/dev/null

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INTEGRATION_TMP="$(mktemp -d "${TMPDIR:-/tmp}/partygame-ios-photo-integration.XXXXXX")"
ACCESS_FILE="/private/tmp/partygame-ios-integration-access.json"
DESTINATION_ID="${IOS_DESTINATION_ID:-86B8118B-E2A6-4947-A716-84F6FA0850D9}"
SERVER_PID=""
DEMO_PID=""

cleanup() {
  local status=$?
  if [[ -n "$DEMO_PID" ]]; then kill "$DEMO_PID" 2>/dev/null || true; fi
  if [[ -n "$SERVER_PID" ]]; then kill "$SERVER_PID" 2>/dev/null || true; fi
  rm -f "$ACCESS_FILE"
  rm -rf "$INTEGRATION_TMP"
  return $status
}
trap cleanup EXIT

cd "$REPO_DIR"
rm -f "$ACCESS_FILE"
dotnet build server/PartyGame.Api/PartyGame.Api.csproj --no-restore
dotnet build scripts/PartyGame.PhotoDemoClient/PartyGame.PhotoDemoClient.csproj --no-restore

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5050 \
ConnectionStrings__PartyGame="Data Source=${INTEGRATION_TMP}/integration.db" \
MediaStorage__RootPath="${INTEGRATION_TMP}/media" \
GameFlow__WorkerIntervalMilliseconds=100 \
dotnet server/PartyGame.Api/bin/Debug/net10.0/PartyGame.Api.dll >"${INTEGRATION_TMP}/server.log" 2>&1 &
SERVER_PID=$!

for _ in {1..100}; do
  if curl --silent --fail http://127.0.0.1:5050/health >/dev/null; then break; fi
  sleep 0.1
done
curl --silent --fail http://127.0.0.1:5050/health >/dev/null

PARTYGAME_DEMO_URL=http://127.0.0.1:5050 \
PARTYGAME_EXTERNAL_PLAYER_FILE="$ACCESS_FILE" \
dotnet scripts/PartyGame.PhotoDemoClient/bin/Debug/net10.0/PartyGame.PhotoDemoClient.dll &
DEMO_PID=$!

PARTYGAME_IOS_INTEGRATION_REQUIRED=1 \
xcodebuild -project apps/ios/PartyGame.xcodeproj -scheme PartyGame \
  -destination "id=${DESTINATION_ID}" test \
  -only-testing:PartyGameTests/PhotoAnswerBackendIntegrationTests

wait "$DEMO_PID"
DEMO_PID=""
