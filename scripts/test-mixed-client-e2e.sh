#!/usr/bin/env bash
set -euo pipefail

export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
export PATH="$DEVELOPER_DIR/usr/bin:$PATH"

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
E2E_TMP="$(mktemp -d "${TMPDIR:-/tmp}/partygame-mixed-e2e.XXXXXX")"
export E2E_TMP

API_PID=""
VITE_PID=""
IOS_PID=""

cleanup() {
  local status=$?
  if [[ -n "$VITE_PID" ]]; then kill "$VITE_PID" 2>/dev/null || true; fi
  if [[ -n "$API_PID" ]]; then kill "$API_PID" 2>/dev/null || true; fi
  if [[ -n "$IOS_PID" ]]; then kill "$IOS_PID" 2>/dev/null || true; fi
  rm -rf "$E2E_TMP"
  return $status
}
trap cleanup EXIT

function get_free_port() {
  python3 -c 'import socket; s=socket.socket(); s.bind(("", 0)); print(s.getsockname()[1]); s.close()'
}

API_PORT=$(get_free_port)
VITE_PORT=$(get_free_port)
export PLAYWRIGHT_API_URL="http://127.0.0.1:${API_PORT}"
export VITE_URL="http://127.0.0.1:${VITE_PORT}"

# Start API
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="${PLAYWRIGHT_API_URL}" \
ConnectionStrings__PartyGame="Data Source=${E2E_TMP}/mixed-e2e.db" \
MediaStorage__RootPath="${E2E_TMP}/media" \
GameFlow__WorkerIntervalMilliseconds=100 \
dotnet run --project "${REPO_DIR}/server/PartyGame.Api/PartyGame.Api.csproj" --no-restore --no-build --no-launch-profile >"${E2E_TMP}/api.log" 2>&1 &
API_PID=$!

echo "Waiting for API..."
for _ in {1..50}; do
  if curl --silent --fail "${PLAYWRIGHT_API_URL}/health" >/dev/null; then break; fi
  sleep 0.2
done
curl --silent --fail "${PLAYWRIGHT_API_URL}/health" >/dev/null

# Start Vite
cd "${REPO_DIR}/apps/display-web"
VITE_API_BASE_URL="${PLAYWRIGHT_API_URL}" ./node_modules/.bin/vite --host 127.0.0.1 --port "${VITE_PORT}" --strictPort >"${E2E_TMP}/vite.log" 2>&1 &
VITE_PID=$!

echo "Waiting for Vite..."
for _ in {1..50}; do
  if curl --silent --fail "${VITE_URL}" >/dev/null; then break; fi
  sleep 0.2
done
curl --silent --fail "${VITE_URL}" >/dev/null

# Start iOS Client
DESTINATION_ID="${IOS_DESTINATION_ID:-$(/usr/bin/xcrun simctl list devices available | sed -nE 's/.*iPhone 17 Pro \(([0-9A-F-]{36})\) \((Booted|Shutdown)\).*/\1/p' | head -1)}"
if [[ -z "$DESTINATION_ID" ]]; then echo "No available iPhone 17 Pro simulator." >&2; exit 1; fi
PROFILE_PHOTO_FIXTURE="${REPO_DIR}/apps/ios/PartyGameUITests/Fixtures/profile-photo.png"
test -f "$PROFILE_PHOTO_FIXTURE"
test -s "$PROFILE_PHOTO_FIXTURE"
/usr/bin/xcrun simctl boot "$DESTINATION_ID" 2>/dev/null || true
/usr/bin/xcrun simctl bootstatus "$DESTINATION_ID" -b
echo "Simulator: $DESTINATION_ID"
echo "Profile fixture: $PROFILE_PHOTO_FIXTURE"
/usr/bin/xcrun simctl addmedia "$DESTINATION_ID" "$PROFILE_PHOTO_FIXTURE"
echo "Profile fixture import: PASS"
PARTYGAME_E2E_MODE=1 PARTYGAME_E2E_BACKEND_URL="${PLAYWRIGHT_API_URL}" \
PARTYGAME_E2E_ROOM_CODE="${PARTYGAME_E2E_ROOM_CODE:-}" \
xcodebuild -project "${REPO_DIR}/apps/ios/PartyGame.xcodeproj" -scheme PartyGame \
  -destination "platform=iOS Simulator,id=${DESTINATION_ID}" test \
  -only-testing:PartyGameUITests/MixedGameClientE2ETests >"${E2E_TMP}/ios.log" 2>&1 &
IOS_PID=$!

# Run Playwright Mixed Game Spec
npm run test:e2e:mixed
wait "$IOS_PID"
IOS_PID=""
