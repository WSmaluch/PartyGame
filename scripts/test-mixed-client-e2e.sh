#!/usr/bin/env bash
set -euo pipefail

export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
export PATH="$DEVELOPER_DIR/usr/bin:$PATH"

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DESTINATION_ID="${IOS_DESTINATION_ID:-86B8118B-E2A6-4947-A716-84F6FA0850D9}"
RUN_MODE="${PARTYGAME_E2E_RUN_MODE:-full}"
E2E_TMP="$(mktemp -d "${TMPDIR:-/tmp}/partygame-mixed-e2e.XXXXXX")"
COORDINATION_DIR="${E2E_TMP}/coordination"
XCODE_DIR="${E2E_TMP}/xcode"
XCODE_LOGS_DIR="${XCODE_DIR}/logs"
XCODE_DERIVED_DATA="${XCODE_DIR}/DerivedData"
XCODE_SOURCE_PACKAGES="${XCODE_DIR}/SourcePackages"
XCODE_PACKAGE_CACHE="${XCODE_DIR}/PackageCache"
IOS_SOURCE_ROOT="${E2E_TMP}/ios-source"
IOS_PROJECT_DIR="${IOS_SOURCE_ROOT}/apps/ios"
export E2E_TMP COORDINATION_DIR

API_PID=""
VITE_PID=""
ORCHESTRATOR_PID=""
IOS_PID=""
PLAYWRIGHT_PID=""
XCODE_PID=""
XCODE_PHASE=""
STAGE="preflight"

mkdir -p "$COORDINATION_DIR" "$XCODE_LOGS_DIR"

stop_process() {
  local pid="$1"
  [[ -z "$pid" ]] && return
  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
}

save_failure_diagnostics() {
  local status="$1"
  local diagnostics_dir
  diagnostics_dir="$(mktemp -d "${TMPDIR:-/tmp}/partygame-mixed-e2e-failure.XXXXXX")"
  {
    printf 'stage=%s\n' "$STAGE"
    printf 'exit_code=%s\n' "$status"
    printf 'xcode_phase=%s\n' "$XCODE_PHASE"
    printf 'api_pid=%s\n' "$API_PID"
    printf 'vite_pid=%s\n' "$VITE_PID"
    printf 'orchestrator_pid=%s\n' "$ORCHESTRATOR_PID"
    printf 'ios_pid=%s\n' "$IOS_PID"
    printf 'playwright_pid=%s\n' "$PLAYWRIGHT_PID"
  } >"${diagnostics_dir}/summary.txt"
  [[ -f "${COORDINATION_DIR}/coordination.json" ]] && cp "${COORDINATION_DIR}/coordination.json" "${diagnostics_dir}/coordination.json"
  [[ -f "${COORDINATION_DIR}/outcome.json" ]] && cp "${COORDINATION_DIR}/outcome.json" "${diagnostics_dir}/outcome.json"
  [[ -d "$XCODE_LOGS_DIR" ]] && cp -R "$XCODE_LOGS_DIR" "${diagnostics_dir}/xcode-logs"
  for log in api vite orchestrator ios playwright; do
    [[ -f "${E2E_TMP}/${log}.log" ]] && tail -n 100 "${E2E_TMP}/${log}.log" >"${diagnostics_dir}/${log}-last.log"
  done
  printf 'Mixed Client E2E diagnostics: %s\n' "$diagnostics_dir" >&2
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  if [[ "$status" -ne 0 ]]; then save_failure_diagnostics "$status"; fi
  stop_process "$PLAYWRIGHT_PID"
  stop_process "$IOS_PID"
  stop_process "$XCODE_PID"
  stop_process "$ORCHESTRATOR_PID"
  stop_process "$VITE_PID"
  stop_process "$API_PID"
  rm -rf "$E2E_TMP"
  return "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

get_free_port() {
  python3 -c 'import socket; s=socket.socket(); s.bind(("", 0)); print(s.getsockname()[1]); s.close()'
}

prepare_ios_source() {
  mkdir -p "$IOS_SOURCE_ROOT"
  git -C "$REPO_DIR" archive --format=tar HEAD apps/ios | tar -xf - -C "$IOS_SOURCE_ROOT"

  local changed_path
  while IFS= read -r -d '' changed_path; do
    local source_path="${REPO_DIR}/${changed_path}"
    local target_path="${IOS_SOURCE_ROOT}/${changed_path}"
    if [[ -e "$source_path" ]]; then
      mkdir -p "$(dirname "$target_path")"
      cp -p "$source_path" "$target_path"
    else
      rm -f "$target_path"
    fi
  done < <(git -C "$REPO_DIR" diff --name-only -z HEAD -- apps/ios)

  while IFS= read -r -d '' changed_path; do
    mkdir -p "$(dirname "${IOS_SOURCE_ROOT}/${changed_path}")"
    cp -p "${REPO_DIR}/${changed_path}" "${IOS_SOURCE_ROOT}/${changed_path}"
  done < <(git -C "$REPO_DIR" ls-files --others --exclude-standard -z -- apps/ios)
}

configure_xctestrun() {
  XCTESTRUN_FILE="$(find "${XCODE_DERIVED_DATA}/Build/Products" -name '*.xctestrun' -print -quit)"
  [[ -n "$XCTESTRUN_FILE" ]] || {
    printf 'Missing .xctestrun after build-for-testing.\n' >&2
    return 1
  }

  # Xcode 16 writes the test target below TestConfigurations, while older
  # generated files use the target name at the plist root. Keep both layouts
  # configured so the UI-test runner, not only the launched app, receives E2E
  # configuration before it evaluates its guard.
  local target_path
  for target_path in \
    ':PartyGameUITests:EnvironmentVariables' \
    ':TestConfigurations:0:TestTargets:0:EnvironmentVariables'; do
    /usr/libexec/PlistBuddy -c "Add ${target_path} dict" "$XCTESTRUN_FILE" 2>/dev/null || true
  done
  local key
  for key in \
    PARTYGAME_E2E_MODE \
    PARTYGAME_E2E_BACKEND_URL \
    PARTYGAME_E2E_ROOM_CODE \
    PARTYGAME_E2E_PLAYER_NICKNAME \
    PARTYGAME_E2E_COORDINATION_DIR \
    PARTYGAME_E2E_REQUIRE_GAME_STARTED; do
    for target_path in \
      ':PartyGameUITests:EnvironmentVariables' \
      ':TestConfigurations:0:TestTargets:0:EnvironmentVariables'; do
      /usr/libexec/PlistBuddy -c "Delete ${target_path}:${key}" "$XCTESTRUN_FILE" 2>/dev/null || true
      /usr/libexec/PlistBuddy -c "Add ${target_path}:${key} string ${!key}" "$XCTESTRUN_FILE" 2>/dev/null || true
    done
  done
}

start_phase() {
  local phase="$1"
  shift
  local started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  local log_file="${XCODE_LOGS_DIR}/${phase}.log"
  local status_file="${XCODE_LOGS_DIR}/${phase}.status"
  XCODE_PHASE="$phase"
  STAGE="$phase"
  printf 'phase=%s\nstarted_at=%s\n' "$phase" "$started_at" >"$status_file"
  printf 'START %s %s\n' "$phase" "$started_at" >"$log_file"
  env NSUnbufferedIO=YES "$@" >>"$log_file" 2>&1 &
  XCODE_PID=$!
  printf 'pid=%s\n' "$XCODE_PID" >>"$status_file"
}

wait_for_phase() {
  local phase="$1"
  local seconds="$2"
  local status_file="${XCODE_LOGS_DIR}/${phase}.status"
  local log_file="${XCODE_LOGS_DIR}/${phase}.log"
  for ((elapsed = 0; elapsed < seconds; elapsed++)); do
    if ! kill -0 "$XCODE_PID" 2>/dev/null; then
      local exit_code=0
      wait "$XCODE_PID" || exit_code=$?
      printf 'ended_at=%s\nexit_code=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$exit_code" >>"$status_file"
      tail -n 40 "$log_file" >>"$status_file"
      XCODE_PID=""
      return "$exit_code"
    fi
    sleep 1
  done
  printf 'ended_at=%s\nexit_code=124\ntimeout_seconds=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$seconds" >>"$status_file"
  tail -n 40 "$log_file" >>"$status_file"
  printf 'Timeout in phase %s after %s seconds.\n' "$phase" "$seconds" >&2
  kill "$XCODE_PID" 2>/dev/null || true
  wait "$XCODE_PID" 2>/dev/null || true
  XCODE_PID=""
  return 124
}

run_phase() {
  local phase="$1"
  local seconds="$2"
  shift 2
  start_phase "$phase" "$@"
  wait_for_phase "$phase" "$seconds"
}

wait_for_http() {
  local url="$1"
  local description="$2"
  for _ in {1..100}; do
    if curl --silent --fail "$url" >/dev/null; then return; fi
    sleep 0.2
  done
  printf 'Timeout waiting for %s: %s\n' "$description" "$url" >&2
  return 1
}

wait_for_marker() {
  local marker="$1"
  local description="$2"
  local attempts="$3"
  for ((attempt = 0; attempt < attempts; attempt++)); do
    [[ -e "$marker" ]] && return
    if [[ -n "$IOS_PID" ]] && ! kill -0 "$IOS_PID" 2>/dev/null; then
      wait "$IOS_PID"
      return 1
    fi
    sleep 0.2
  done
  printf 'Timeout waiting for %s: %s\n' "$description" "$marker" >&2
  return 1
}

wait_for_process() {
  local pid="$1"
  local seconds="$2"
  local description="$3"
  for ((elapsed = 0; elapsed < seconds; elapsed++)); do
    if ! kill -0 "$pid" 2>/dev/null; then
      wait "$pid"
      return
    fi
    sleep 1
  done
  printf 'Timeout waiting for %s.\n' "$description" >&2
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  return 1
}

wait_for_display_attach() {
  local marker="${COORDINATION_DIR}/display-attached"
  for ((attempt = 0; attempt < 300; attempt++)); do
    [[ -e "$marker" ]] && return
    if [[ -n "$PLAYWRIGHT_PID" ]] && ! kill -0 "$PLAYWRIGHT_PID" 2>/dev/null; then
      local exit_code=0
      wait "$PLAYWRIGHT_PID" || exit_code=$?
      printf 'Display Playwright exited before initial attach (exit %s).\n' "$exit_code" >&2
      return "$exit_code"
    fi
    sleep 0.2
  done
  printf 'Timeout waiting for initial Display attach: %s\n' "$marker" >&2
  return 1
}

STAGE="xcode-preflight"
run_phase "preflight" 30 xcodebuild -version
run_phase "simulator-shutdown" 60 /usr/bin/xcrun simctl shutdown "$DESTINATION_ID" || true
run_phase "simulator-boot" 60 /usr/bin/xcrun simctl boot "$DESTINATION_ID" || true
run_phase "simulator-bootstatus" 60 /usr/bin/xcrun simctl bootstatus "$DESTINATION_ID" -b

STAGE="ios-source-snapshot"
prepare_ios_source >"${XCODE_LOGS_DIR}/ios-source-snapshot.log" 2>&1

run_phase "swiftpm-resolve" 180 xcodebuild \
  -project "${IOS_PROJECT_DIR}/PartyGame.xcodeproj" \
  -scheme PartyGame \
  -derivedDataPath "$XCODE_DERIVED_DATA" \
  -clonedSourcePackagesDirPath "$XCODE_SOURCE_PACKAGES" \
  -packageCachePath "$XCODE_PACKAGE_CACHE" \
  -scmProvider system \
  -onlyUsePackageVersionsFromResolvedFile \
  -skipPackageUpdates \
  -resolvePackageDependencies

run_phase "build-for-testing" 240 xcodebuild \
  -project "${IOS_PROJECT_DIR}/PartyGame.xcodeproj" \
  -scheme PartyGame \
  -destination "platform=iOS Simulator,id=${DESTINATION_ID}" \
  -derivedDataPath "$XCODE_DERIVED_DATA" \
  -clonedSourcePackagesDirPath "$XCODE_SOURCE_PACKAGES" \
  -packageCachePath "$XCODE_PACKAGE_CACHE" \
  -disableAutomaticPackageResolution \
  -onlyUsePackageVersionsFromResolvedFile \
  -resultBundlePath "${XCODE_DIR}/build-for-testing.xcresult" \
  build-for-testing -only-testing:PartyGameUITests/MixedGameClientE2ETests

API_PORT="$(get_free_port)"
VITE_PORT="$(get_free_port)"
export PLAYWRIGHT_API_URL="http://127.0.0.1:${API_PORT}"
export VITE_URL="http://127.0.0.1:${VITE_PORT}"

STAGE="backend-start"
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="$PLAYWRIGHT_API_URL" \
ConnectionStrings__PartyGame="Data Source=${E2E_TMP}/mixed-e2e.db" \
MediaStorage__RootPath="${E2E_TMP}/media" \
GameFlow__WorkerIntervalMilliseconds=100 \
dotnet run --project "${REPO_DIR}/server/PartyGame.Api/PartyGame.Api.csproj" --no-restore --no-launch-profile >"${E2E_TMP}/api.log" 2>&1 &
API_PID=$!
wait_for_http "${PLAYWRIGHT_API_URL}/health" "API health"

STAGE="package-and-room-setup"
PARTYGAME_MIXED_E2E_BACKEND_URL="$PLAYWRIGHT_API_URL" \
PARTYGAME_E2E_COORDINATION_DIR="$COORDINATION_DIR" \
dotnet run --project "${REPO_DIR}/scripts/PartyGame.MixedE2EOrchestrator/PartyGame.MixedE2EOrchestrator.csproj" >"${E2E_TMP}/orchestrator.log" 2>&1 &
ORCHESTRATOR_PID=$!
wait_for_marker "${COORDINATION_DIR}/coordination.json" "public coordination state" 300
jq -e \
  '.backendUrl != "" and .roomCode != "" and .contentPackageVersionId != "" and .iosNickname != "" and .displayExpected == true' \
  "${COORDINATION_DIR}/coordination.json" >/dev/null

ROOM_CODE="$(jq -r '.roomCode' "${COORDINATION_DIR}/coordination.json")"
IOS_NICKNAME="$(jq -r '.iosNickname' "${COORDINATION_DIR}/coordination.json")"
export PARTYGAME_E2E_MODE=1
export PARTYGAME_E2E_BACKEND_URL="$PLAYWRIGHT_API_URL"
export PARTYGAME_E2E_ROOM_CODE="$ROOM_CODE"
export PARTYGAME_E2E_PLAYER_NICKNAME="$IOS_NICKNAME"
export PARTYGAME_E2E_REQUIRE_GAME_STARTED=1
if [[ "$RUN_MODE" == "ios-only" ]]; then
  export PARTYGAME_E2E_REQUIRE_GAME_STARTED=0
elif [[ "$RUN_MODE" != "full" ]]; then
  printf 'Unsupported PARTYGAME_E2E_RUN_MODE: %s\n' "$RUN_MODE" >&2
  exit 2
fi
STAGE="xctestrun-configuration"
configure_xctestrun

STAGE="display-server-start"
cd "${REPO_DIR}/apps/display-web"
VITE_API_BASE_URL="$PLAYWRIGHT_API_URL" ./node_modules/.bin/vite --host 127.0.0.1 --port "$VITE_PORT" --strictPort >"${E2E_TMP}/vite.log" 2>&1 &
VITE_PID=$!
wait_for_http "$VITE_URL" "Vite"

STAGE="ios-test-without-building"
PROFILE_PHOTO_FIXTURE="${REPO_DIR}/apps/ios/PartyGameUITests/Fixtures/profile-photo.png"
test -s "$PROFILE_PHOTO_FIXTURE"
run_phase "profile-fixture-import" 60 /usr/bin/xcrun simctl addmedia "$DESTINATION_ID" "$PROFILE_PHOTO_FIXTURE"

start_phase "test-without-building" xcodebuild \
  -xctestrun "$XCTESTRUN_FILE" \
  -destination "platform=iOS Simulator,id=${DESTINATION_ID}" \
  -resultBundlePath "${XCODE_DIR}/test-without-building.xcresult" \
  test-without-building -only-testing:PartyGameUITests/MixedGameClientE2ETests
IOS_PID="$XCODE_PID"

wait_for_marker "${COORDINATION_DIR}/ios-launched" "XCUITest method entry" 900
wait_for_marker "${COORDINATION_DIR}/ios-profile-saved" "iOS profile save" 900

if [[ "$RUN_MODE" == "ios-only" ]]; then
  wait_for_marker "${COORDINATION_DIR}/ios-ready" "iOS Ready" 30
  wait_for_phase "test-without-building" 240
  IOS_PID=""
  XCODE_PID=""
  printf 'PASS: isolated iOS Mixed Client XCUITest completed for room %s.\n' "$ROOM_CODE"
  exit 0
fi

STAGE="display-attach"
PARTYGAME_E2E_COORDINATION_DIR="$COORDINATION_DIR" \
PLAYWRIGHT_OUTPUT_DIR="${E2E_TMP}/playwright-results" \
PLAYWRIGHT_ARTIFACTS_DIR="${E2E_TMP}/playwright-report" \
npm run test:e2e:mixed >"${E2E_TMP}/playwright.log" 2>&1 &
PLAYWRIGHT_PID=$!
wait_for_display_attach

STAGE="orchestration-validation"
wait_for_process "$ORCHESTRATOR_PID" 300 "orchestrator"
ORCHESTRATOR_PID=""
wait_for_marker "${COORDINATION_DIR}/ios-ready" "iOS Ready" 30
wait_for_marker "${COORDINATION_DIR}/ios-observed-game-start" "iOS game start" 60
wait_for_phase "test-without-building" 240
IOS_PID=""
XCODE_PID=""
wait_for_process "$PLAYWRIGHT_PID" 240 "Display Playwright"
PLAYWRIGHT_PID=""

STAGE="cleanup-pass"
jq -e \
  '.status == "passed"
    and .roomPhase == "Completed"
    and .roomStartedEvents == 1
    and .playedQuestionCount == 4
    and .uniqueQuestionIdCount == 4
    and .playerSelectionCount == 1
    and .textAnswerCount == 1
    and .photoAnswerCount == 1
    and .drawingAnswerCount == 1
    and .rankingCount == 3
    and .stateVersionMonotonic == true
    and .iosReconnectCount == 1
    and .iosSamePlayerRecovered == true
    and .iosVersionRegressionCount == 0
    and .displayReconnectCount == 1
    and .displayVersionRegressionCount == 0
    and .duplicateResponseCount == 0
    and .duplicateVoteCount == 0
    and .ios == "completed"
    and .display == "completed"
    and .scriptedPlayers == "completed"
    and (.questions | length == 4)' \
  "${COORDINATION_DIR}/outcome.json" >/dev/null
printf 'PASS: full Mixed Client E2E completed for room %s.\n' "$ROOM_CODE"
