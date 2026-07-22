#!/usr/bin/env bash
set -euo pipefail

export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
export PATH="$DEVELOPER_DIR/usr/bin:/usr/local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin"
/usr/bin/xcrun --find simctl >/dev/null
/usr/bin/xcrun simctl list devices available >/dev/null

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INTEGRATION_TMP="$(mktemp -d "${TMPDIR:-/tmp}/partygame-ios-drawing-integration.XXXXXX")"
ACCESS_FILE="/private/tmp/partygame-ios-drawing-integration-access.json"
DESTINATION_ID="${IOS_DESTINATION_ID:-}"
SERVER_PID=""
DEMO_PID=""

cleanup() {
  local status=$?
  if [[ -n "$DEMO_PID" ]]; then kill "$DEMO_PID" 2>/dev/null || true; fi
  if [[ -n "$SERVER_PID" ]]; then kill "$SERVER_PID" 2>/dev/null || true; fi
  if [[ $status -ne 0 && -f "${INTEGRATION_TMP}/server.log" ]]; then
    grep -E -A12 -B3 "ERR|Error|Exception|exception|fail" "${INTEGRATION_TMP}/server.log" | tail -160 >&2 || true
  fi
  rm -f "$ACCESS_FILE"
  rm -rf "$INTEGRATION_TMP"
  return $status
}
trap cleanup EXIT

if [[ -z "$DESTINATION_ID" ]]; then
  DESTINATION_ID="$(/usr/bin/xcrun simctl list devices available | sed -nE 's/.*iPhone 17 Pro \(([0-9A-F-]{36})\) \(Shutdown\).*/\1/p' | head -1)"
fi
if [[ -z "$DESTINATION_ID" ]]; then echo "No available iPhone simulator." >&2; exit 1; fi

cd "$REPO_DIR"
rm -f "$ACCESS_FILE"
dotnet build server/PartyGame.Api/PartyGame.Api.csproj --no-restore
dotnet build scripts/PartyGame.DrawingDemoClient/PartyGame.DrawingDemoClient.csproj --no-restore

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5050 \
ConnectionStrings__PartyGame="Data Source=${INTEGRATION_TMP}/integration.db" \
MediaStorage__RootPath="${INTEGRATION_TMP}/media" \
GameFlow__WorkerIntervalMilliseconds=100 \
GameFlow__DrawingAnswerRevealBaseSeconds=0 \
GameFlow__DrawingAnswerRevealPerDrawingSeconds=1 \
GameFlow__DrawingAnswerRevealMaximumSeconds=3 \
GameFlow__DrawingAnswerResultsSeconds=1 \
dotnet server/PartyGame.Api/bin/Debug/net10.0/PartyGame.Api.dll >"${INTEGRATION_TMP}/server.log" 2>&1 &
SERVER_PID=$!

for _ in {1..100}; do
  if curl --silent --fail http://127.0.0.1:5050/health >/dev/null; then break; fi
  sleep 0.1
done
curl --silent --fail http://127.0.0.1:5050/health >/dev/null

PARTYGAME_DEMO_URL=http://127.0.0.1:5050 \
PARTYGAME_EXTERNAL_PLAYER_FILE="$ACCESS_FILE" \
dotnet scripts/PartyGame.DrawingDemoClient/bin/Debug/net10.0/PartyGame.DrawingDemoClient.dll &
DEMO_PID=$!

PARTYGAME_IOS_INTEGRATION_REQUIRED=1 \
xcodebuild -project apps/ios/PartyGame.xcodeproj -scheme PartyGame \
  -destination "platform=iOS Simulator,id=${DESTINATION_ID}" test \
  -only-testing:PartyGameTests/DrawingAnswerBackendIntegrationTests

wait "$DEMO_PID"
DEMO_PID=""
