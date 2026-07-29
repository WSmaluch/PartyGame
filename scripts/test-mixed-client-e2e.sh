#!/usr/bin/env bash
# A single full Mixed Client run owns every process it starts.  Runtime state is
# disposable; the evidence directory is deliberately durable and contains only
# public/safe diagnostics.
set -uo pipefail

export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
export PATH="$DEVELOPER_DIR/usr/bin:$PATH"

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DESTINATION_ID="${IOS_DESTINATION_ID:-86B8118B-E2A6-4947-A716-84F6FA0850D9}"
RUN_MODE="${PARTYGAME_E2E_RUN_MODE:-full}"
E2E_TMP="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-mixed-e2e.XXXXXX")"
COORDINATION_DIR="${E2E_TMP}/coordination"
XCODE_DIR="${E2E_TMP}/xcode"
XCODE_LOGS_DIR="${XCODE_DIR}/logs"
XCODE_DERIVED_DATA="${XCODE_DIR}/DerivedData"
XCODE_SOURCE_PACKAGES="${XCODE_DIR}/SourcePackages"
XCODE_PACKAGE_CACHE="${XCODE_DIR}/PackageCache"
IOS_SOURCE_ROOT="${E2E_TMP}/ios-source"
IOS_PROJECT_DIR="${IOS_SOURCE_ROOT}/apps/ios"
export E2E_TMP COORDINATION_DIR

PRIMARY_EXIT_CODE=0
CLEANUP_EXIT_CODE=0
STAGE="preflight"
LAST_KNOWN_MARKER="none"
RUN_STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
EVIDENCE_DIR=""
ROOM_CODE=""
XCTESTRUN_FILE=""

mkdir -p "$COORDINATION_DIR" "$XCODE_LOGS_DIR"

# Fixed names keep this compatible with the macOS-provided Bash 3.2, without
# associative arrays.  Each process field is emitted into process-exit-codes.
for process_name in backend vite playwright orchestrator xcodebuild main_script; do
  eval "P_${process_name}_pid=''"
  eval "P_${process_name}_started=false"
  eval "P_${process_name}_started_at=''"
  eval "P_${process_name}_exit_code=''"
  eval "P_${process_name}_reason=''"
  eval "P_${process_name}_expected_cleanup=false"
  eval "P_${process_name}_ended_at=''"
  eval "P_${process_name}_log_path=''"
done

process_key() { printf '%s' "${1//-/_}"; }
set_process_field() { local key; key="$(process_key "$1")"; eval "P_${key}_$2=\$3"; }
get_process_field() { local key; key="$(process_key "$1")"; eval "printf '%s' \"\${P_${key}_$2}\""; }

set_primary_failure() {
  local code="$1"
  [[ "$code" =~ ^[0-9]+$ ]] || code=1
  if [[ "$PRIMARY_EXIT_CODE" -eq 0 && "$code" -ne 0 ]]; then
    PRIMARY_EXIT_CODE="$code"
  fi
}

required_process_error() {
  local name="$1" pid="$2" exit_code="$3" log_path="$4"
  printf 'Required process failure: process=%s pid=%s exitCode=%s currentStage=%s lastKnownMarker=%s logPath=%s\n' \
    "$name" "${pid:-<empty>}" "$exit_code" "$STAGE" "$LAST_KNOWN_MARKER" "$log_path" >&2
  set_primary_failure "$exit_code"
  return 1
}

register_process() {
  local name="$1" pid="$2" log_path="$3"
  if [[ -z "$pid" ]]; then
    required_process_error "$name" "" 64 "$log_path"
    return 1
  fi
  set_process_field "$name" pid "$pid"
  set_process_field "$name" started true
  set_process_field "$name" started_at "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  set_process_field "$name" log_path "$log_path"
}

record_process_exit() {
  local name="$1" exit_code="$2" reason="$3" expected_cleanup="$4"
  set_process_field "$name" exit_code "$exit_code"
  set_process_field "$name" reason "$reason"
  set_process_field "$name" expected_cleanup "$expected_cleanup"
  set_process_field "$name" ended_at "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}

process_is_running() {
  local pid
  pid="$(get_process_field "$1" pid)"
  [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null
}

observe_early_exit() {
  local name="$1" pid log_path exit_code=0
  pid="$(get_process_field "$name" pid)"
  log_path="$(get_process_field "$name" log_path)"
  if [[ -z "$pid" ]]; then
    required_process_error "$name" "" 64 "$log_path"
    return 1
  fi
  if process_is_running "$name"; then return 0; fi
  wait "$pid" || exit_code=$?
  record_process_exit "$name" "$exit_code" completed false
  # A supporting client may legitimately finish before another client reaches
  # its final marker.  Its successful completion is still a healthy state;
  # callers continue to wait for the process they explicitly own.
  [[ "$exit_code" -eq 0 ]] && return 0
  required_process_error "$name" "$pid" "$exit_code" "$log_path"
}

require_running() {
  local name
  for name in "$@"; do observe_early_exit "$name" || return 1; done
}

wait_for_marker() {
  local marker="$1" description="$2" attempts="$3"
  shift 3
  local attempt
  for ((attempt = 0; attempt < attempts; attempt++)); do
    if [[ -e "$marker" ]]; then LAST_KNOWN_MARKER="$description"; return 0; fi
    require_running "$@" || return 1
    sleep 0.2
  done
  printf 'Timeout waiting for %s: %s\n' "$description" "$marker" >&2
  set_primary_failure 124
  return 1
}

wait_for_http() {
  local url="$1" description="$2" process="$3" attempt
  for ((attempt = 0; attempt < 100; attempt++)); do
    if curl --silent --fail "$url" >/dev/null; then LAST_KNOWN_MARKER="$description"; return 0; fi
    require_running "$process" || return 1
    sleep 0.2
  done
  printf 'Timeout waiting for %s: %s\n' "$description" "$url" >&2
  set_primary_failure 124
  return 1
}

wait_for_process() {
  local name="$1" seconds="$2" description="$3"
  shift 3
  local elapsed pid exit_code=0
  pid="$(get_process_field "$name" pid)"
  [[ -n "$pid" ]] || { required_process_error "$name" "" 64 "$(get_process_field "$name" log_path)"; return 1; }
  for ((elapsed = 0; elapsed < seconds; elapsed++)); do
    require_running "$@" || return 1
    if ! process_is_running "$name"; then
      wait "$pid" || exit_code=$?
      record_process_exit "$name" "$exit_code" completed false
      if [[ "$exit_code" -ne 0 ]]; then
        required_process_error "$name" "$pid" "$exit_code" "$(get_process_field "$name" log_path)"
        return 1
      fi
      LAST_KNOWN_MARKER="$description"
      return 0
    fi
    sleep 1
  done
  printf 'Timeout waiting for %s.\n' "$description" >&2
  set_primary_failure 124
  return 1
}

start_background() {
  local name="$1" log_path="$2"
  shift 2
  # Keep the recorded PID bound to the launched program rather than an
  # intermediate shell, so lifecycle checks cannot mistake a wrapper exit for
  # completion while the actual E2E worker is still running.
  (
    exec "$@"
  ) >"$log_path" 2>&1 &
  local pid=$!
  register_process "$name" "$pid" "$log_path"
}

start_vite() {
  local log_path="$1"
  (
    cd "${REPO_DIR}/apps/display-web" || exit 1
    exec env VITE_API_BASE_URL="$PLAYWRIGHT_API_URL" ./node_modules/.bin/vite --host 127.0.0.1 --port "$VITE_PORT" --strictPort
  ) >"$log_path" 2>&1 &
  register_process vite "$!" "$log_path"
}

start_xcode_phase() {
  local phase="$1"; shift
  local log_path="${XCODE_LOGS_DIR}/${phase}.log"
  STAGE="$phase"
  printf 'START %s %s\n' "$phase" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >"$log_path"
  start_background xcodebuild "$log_path" env NSUnbufferedIO=YES "$@"
}

run_xcode_phase() {
  local phase="$1" seconds="$2"; shift 2
  start_xcode_phase "$phase" "$@"
  wait_for_process xcodebuild "$seconds" "$phase" || return 1
}

# Simulator shutdown/boot are deliberately idempotent.  Their non-zero status
# (already shut down/already booted) is recorded, but is not a test failure.
run_optional_xcode_phase() {
  local phase="$1" seconds="$2"; shift 2
  local pid elapsed exit_code=0
  start_xcode_phase "$phase" "$@"
  pid="$(get_process_field xcodebuild pid)"
  for ((elapsed = 0; elapsed < seconds; elapsed++)); do
    if ! process_is_running xcodebuild; then
      wait "$pid" || exit_code=$?
      record_process_exit xcodebuild "$exit_code" completed false
      return 0
    fi
    sleep 1
  done
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  record_process_exit xcodebuild 124 timeout false
  set_primary_failure 124
  return 1
}

stop_process() {
  local name="$1" pid exit_code=0
  pid="$(get_process_field "$name" pid)"
  [[ -n "$pid" ]] || return 0
  if process_is_running "$name"; then
    kill "$pid" 2>/dev/null || CLEANUP_EXIT_CODE=1
    if ! wait "$pid" 2>/dev/null; then exit_code=$?; fi
    record_process_exit "$name" "$exit_code" cleanup-signal true
  elif [[ -z "$(get_process_field "$name" exit_code)" ]]; then
    if ! wait "$pid" 2>/dev/null; then exit_code=$?; fi
    record_process_exit "$name" "$exit_code" completed false
  fi
}

process_json() {
  local name="$1" started pid started_at exit_code reason expected ended log_path
  started="$(get_process_field "$name" started)"; pid="$(get_process_field "$name" pid)"
  started_at="$(get_process_field "$name" started_at)"; exit_code="$(get_process_field "$name" exit_code)"
  reason="$(get_process_field "$name" reason)"; expected="$(get_process_field "$name" expected_cleanup)"
  ended="$(get_process_field "$name" ended_at)"; log_path="$(get_process_field "$name" log_path)"
  jq -n --argjson started "$started" --arg pid "$pid" --arg startedAt "$started_at" \
    --arg exitCode "$exit_code" --arg reason "$reason" --argjson expected "$expected" \
    --arg endedAt "$ended" --arg logPath "$log_path" \
    '{started:$started, pid:(if $pid == "" then null else ($pid|tonumber) end), startTimestampUtc:(if $startedAt == "" then null else $startedAt end), exitCode:(if $exitCode == "" then null else ($exitCode|tonumber) end), terminationReason:(if $reason == "" then null else $reason end), expectedCleanupTermination:$expected, endTimestampUtc:(if $endedAt == "" then null else $endedAt end), logPath:(if $logPath == "" then null else $logPath end)}'
}

write_process_exit_codes() {
  local target="$1" main_json
  set_process_field main-script pid "$$"
  set_process_field main-script started true
  set_process_field main-script started_at "$RUN_STARTED_AT"
  set_process_field main-script exit_code "$PRIMARY_EXIT_CODE"
  set_process_field main-script reason completed
  set_process_field main-script expected_cleanup false
  set_process_field main-script ended_at "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  jq -n --argjson backend "$(process_json backend)" --argjson vite "$(process_json vite)" \
    --argjson playwright "$(process_json playwright)" --argjson orchestrator "$(process_json orchestrator)" \
    --argjson xcodebuild "$(process_json xcodebuild)" --argjson main "$(process_json main-script)" \
    --argjson cleanup "$CLEANUP_EXIT_CODE" --argjson primary "$PRIMARY_EXIT_CODE" \
    '{schemaVersion:1, processes:{backend:$backend,vite:$vite,playwright:$playwright,orchestrator:$orchestrator,xcodebuild:$xcodebuild,"main-script":$main}, cleanupExitCode:$cleanup, primaryExitCode:$primary}' \
    >"$target.tmp" && mv "$target.tmp" "$target"
}

copy_evidence_file() {
  local source="$1" destination="$2"
  [[ -f "$source" ]] || return 0
  cp "$source" "${destination}.tmp" && mv "${destination}.tmp" "$destination" || CLEANUP_EXIT_CODE=1
}

mask_room() {
  [[ -n "$ROOM_CODE" ]] || { printf '%s' '<unavailable>'; return; }
  printf '%s***' "${ROOM_CODE:0:2}"
}

write_run_summary() {
  local target="$1" outcome="${COORDINATION_DIR}/outcome.json" ledger="${COORDINATION_DIR}/state-version-ledger.json"
  local result="FAIL"; [[ "$PRIMARY_EXIT_CODE" -eq 0 ]] && result="PASS"
  {
    printf 'Result: %s\nPrimaryExitCode: %s\nCleanupExitCode: %s\nCurrentStage: %s\nLastKnownMarker: %s\nRoomCode: %s\nStartedAtUtc: %s\nFinishedAtUtc: %s\nEvidenceDirectory: %s\n' \
      "$result" "$PRIMARY_EXIT_CODE" "$CLEANUP_EXIT_CODE" "$STAGE" "$LAST_KNOWN_MARKER" "$(mask_room)" "$RUN_STARTED_AT" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$EVIDENCE_DIR"
    printf 'OutcomeJson: %s\nStateVersionLedger: %s\nProcessExitCodes: %s\n' "$EVIDENCE_DIR/outcome.json" "$EVIDENCE_DIR/state-version-ledger.json" "$EVIDENCE_DIR/process-exit-codes.json"
    if [[ -f "$outcome" ]]; then
      jq -r '"RoomStartedCount: \(.roomStartedEvents // "n/a")\nPlayedQuestionCount: \(.playedQuestionCount // "n/a")\nRoomPhase: \(.roomPhase // "n/a")\nRankingCount: \(.rankingCount // "n/a")\nIosObservationCount: \(.iosObservationCount // "n/a")\nDisplayObservationCount: \(.displayObservationCount // "n/a")\nScriptedPlayerAObservationCount: \(.scriptedPlayerAObservationCount // "n/a")\nScriptedPlayerBObservationCount: \(.scriptedPlayerBObservationCount // "n/a")\nBackendObservationCount: \(.backendObservationCount // "n/a")\nStateVersionLedgerPassed: \(.stateVersionLedgerPassed // "n/a")"' "$outcome"
    fi
  } >"$target.tmp" && mv "$target.tmp" "$target"
}

preserve_evidence() {
  local kind="pass"; [[ "$PRIMARY_EXIT_CODE" -ne 0 ]] && kind="failure"
  EVIDENCE_DIR="$(mktemp -d "/private/tmp/partygame-mixed-e2e-${kind}.XXXXXX")" || { CLEANUP_EXIT_CODE=1; return; }
  write_process_exit_codes "${EVIDENCE_DIR}/process-exit-codes.json" || CLEANUP_EXIT_CODE=1
  copy_evidence_file "${COORDINATION_DIR}/outcome.json" "${EVIDENCE_DIR}/outcome.json"
  copy_evidence_file "${COORDINATION_DIR}/state-version-ledger.json" "${EVIDENCE_DIR}/state-version-ledger.json"
  copy_evidence_file "${E2E_TMP}/api.log" "${EVIDENCE_DIR}/backend.log"
  copy_evidence_file "${E2E_TMP}/vite.log" "${EVIDENCE_DIR}/vite.log"
  copy_evidence_file "${E2E_TMP}/playwright.log" "${EVIDENCE_DIR}/playwright.log"
  copy_evidence_file "${E2E_TMP}/orchestrator.log" "${EVIDENCE_DIR}/orchestrator.log"
  if [[ -f "${XCODE_LOGS_DIR}/test-without-building.log" ]]; then
    copy_evidence_file "${XCODE_LOGS_DIR}/test-without-building.log" "${EVIDENCE_DIR}/xcodebuild.log"
  else
    local latest_xcode_log
    latest_xcode_log="$(find "$XCODE_LOGS_DIR" -type f -name '*.log' -print | tail -1)"
    [[ -n "$latest_xcode_log" ]] && copy_evidence_file "$latest_xcode_log" "${EVIDENCE_DIR}/xcodebuild.log"
  fi
  local observation
  for observation in "${COORDINATION_DIR}"/{ios,display,scripted-player-a,scripted-player-b,backend}-observation-*.json; do
    [[ -f "$observation" ]] && copy_evidence_file "$observation" "${EVIDENCE_DIR}/$(basename "$observation")"
  done
  for diagnostic in "${COORDINATION_DIR}"/drawing-diagnostic-*.json; do
    [[ -f "$diagnostic" ]] && copy_evidence_file "$diagnostic" "${EVIDENCE_DIR}/$(basename "$diagnostic")"
  done
  write_run_summary "${EVIDENCE_DIR}/run-summary.txt" || CLEANUP_EXIT_CODE=1
  if [[ "$PRIMARY_EXIT_CODE" -eq 0 ]] && { [[ ! -s "${EVIDENCE_DIR}/outcome.json" ]] || [[ ! -s "${EVIDENCE_DIR}/state-version-ledger.json" ]]; }; then
    printf 'PASS run is missing required outcome evidence.\n' >&2
    CLEANUP_EXIT_CODE=1
  fi
}

cleanup() {
  stop_process playwright
  stop_process xcodebuild
  stop_process orchestrator
  stop_process vite
  stop_process backend
  preserve_evidence
  rm -rf "$E2E_TMP" || CLEANUP_EXIT_CODE=1
}

on_exit() {
  local shell_code=$?
  trap - EXIT INT TERM
  set_primary_failure "$shell_code"
  cleanup
  # The process file must contain the final cleanup result as well.
  [[ -n "$EVIDENCE_DIR" ]] && write_process_exit_codes "${EVIDENCE_DIR}/process-exit-codes.json" || true
  printf 'E2E_EVIDENCE_DIR=%s\nOUTCOME_JSON=%s\nSTATE_VERSION_LEDGER=%s\nPROCESS_EXIT_CODES=%s\nRUN_SUMMARY=%s\n' \
    "$EVIDENCE_DIR" "$EVIDENCE_DIR/outcome.json" "$EVIDENCE_DIR/state-version-ledger.json" "$EVIDENCE_DIR/process-exit-codes.json" "$EVIDENCE_DIR/run-summary.txt"
  exit "$PRIMARY_EXIT_CODE"
}

run_lifecycle_self_test() {
  local root="$(mktemp -d /private/tmp/partygame-mixed-e2e-lifecycle.XXXXXX)" pid rc=0
  PRIMARY_EXIT_CODE=0; set_primary_failure 7; set_primary_failure 9; [[ "$PRIMARY_EXIT_CODE" -eq 7 ]] || rc=1
  PRIMARY_EXIT_CODE=0; CLEANUP_EXIT_CODE=1; [[ "$PRIMARY_EXIT_CODE" -eq 0 && "$CLEANUP_EXIT_CODE" -eq 1 ]] || rc=1
  PRIMARY_EXIT_CODE=0; register_process playwright "" "$root/playwright.log" && rc=1; [[ "$PRIMARY_EXIT_CODE" -eq 64 ]] || rc=1
  PRIMARY_EXIT_CODE=0; sh -c 'exit 7' >"$root/playwright.log" 2>&1 & pid=$!; register_process playwright "$pid" "$root/playwright.log"; sleep 0.1; observe_early_exit playwright && rc=1; [[ "$PRIMARY_EXIT_CODE" -eq 7 ]] || rc=1
  mkdir -p "$root/runtime/coordination"; printf '{"status":"passed"}\n' >"$root/runtime/coordination/outcome.json"; printf '{"schemaVersion":1}\n' >"$root/runtime/coordination/state-version-ledger.json"; printf 'log\n' >"$root/runtime/api.log"
  local old_tmp="$E2E_TMP" old_coord="$COORDINATION_DIR" old_evidence="$EVIDENCE_DIR"
  E2E_TMP="$root/runtime"; COORDINATION_DIR="$root/runtime/coordination"; PRIMARY_EXIT_CODE=0; CLEANUP_EXIT_CODE=0; EVIDENCE_DIR="$(mktemp -d /private/tmp/partygame-mixed-e2e-pass.XXXXXX)"; write_process_exit_codes "$EVIDENCE_DIR/process-exit-codes.json"; copy_evidence_file "$COORDINATION_DIR/outcome.json" "$EVIDENCE_DIR/outcome.json"; copy_evidence_file "$COORDINATION_DIR/state-version-ledger.json" "$EVIDENCE_DIR/state-version-ledger.json"; copy_evidence_file "$E2E_TMP/api.log" "$EVIDENCE_DIR/backend.log"; write_run_summary "$EVIDENCE_DIR/run-summary.txt"; [[ -f "$EVIDENCE_DIR/outcome.json" && -f "$EVIDENCE_DIR/state-version-ledger.json" && -f "$EVIDENCE_DIR/process-exit-codes.json" && -f "$EVIDENCE_DIR/run-summary.txt" ]] || rc=1
  PRIMARY_EXIT_CODE=7; EVIDENCE_DIR="$(mktemp -d /private/tmp/partygame-mixed-e2e-failure.XXXXXX)"; write_process_exit_codes "$EVIDENCE_DIR/process-exit-codes.json"; copy_evidence_file "$COORDINATION_DIR/outcome.json" "$EVIDENCE_DIR/outcome.json"; [[ -f "$EVIDENCE_DIR/outcome.json" && -f "$EVIDENCE_DIR/process-exit-codes.json" ]] || rc=1
  E2E_TMP="$old_tmp"; COORDINATION_DIR="$old_coord"; EVIDENCE_DIR="$old_evidence"; rm -rf "$root"
  [[ "$rc" -eq 0 ]] && printf 'Lifecycle self-test: PASS\n' || printf 'Lifecycle self-test: FAIL\n' >&2
  return "$rc"
}

if [[ "${1:-}" == "--lifecycle-self-test" ]]; then
  trap - EXIT INT TERM
  run_lifecycle_self_test
  exit $?
fi
trap on_exit EXIT
trap 'set_primary_failure 130; exit 130' INT
trap 'set_primary_failure 143; exit 143' TERM

get_free_port() { python3 -c 'import socket; s=socket.socket(); s.bind(("", 0)); print(s.getsockname()[1]); s.close()'; }

prepare_ios_source() {
  mkdir -p "$IOS_SOURCE_ROOT"
  git -C "$REPO_DIR" archive --format=tar HEAD apps/ios | tar -xf - -C "$IOS_SOURCE_ROOT"
  local changed_path
  while IFS= read -r -d '' changed_path; do
    local source_path="${REPO_DIR}/${changed_path}" target_path="${IOS_SOURCE_ROOT}/${changed_path}"
    if [[ -e "$source_path" ]]; then mkdir -p "$(dirname "$target_path")"; cp -p "$source_path" "$target_path"; else rm -f "$target_path"; fi
  done < <(git -C "$REPO_DIR" diff --name-only -z HEAD -- apps/ios)
  while IFS= read -r -d '' changed_path; do mkdir -p "$(dirname "${IOS_SOURCE_ROOT}/${changed_path}")"; cp -p "${REPO_DIR}/${changed_path}" "${IOS_SOURCE_ROOT}/${changed_path}"; done < <(git -C "$REPO_DIR" ls-files --others --exclude-standard -z -- apps/ios)
}

configure_xctestrun() {
  XCTESTRUN_FILE="$(find "${XCODE_DERIVED_DATA}/Build/Products" -name '*.xctestrun' -print -quit)"
  [[ -n "$XCTESTRUN_FILE" ]] || { printf 'Missing .xctestrun after build-for-testing.\n' >&2; return 1; }
  local target_path key
  for target_path in ':PartyGameUITests:EnvironmentVariables' ':TestConfigurations:0:TestTargets:0:EnvironmentVariables'; do /usr/libexec/PlistBuddy -c "Add ${target_path} dict" "$XCTESTRUN_FILE" 2>/dev/null || true; done
  for key in PARTYGAME_E2E_MODE PARTYGAME_E2E_BACKEND_URL PARTYGAME_E2E_ROOM_CODE PARTYGAME_E2E_PLAYER_NICKNAME PARTYGAME_E2E_COORDINATION_DIR PARTYGAME_E2E_REQUIRE_GAME_STARTED; do
    for target_path in ':PartyGameUITests:EnvironmentVariables' ':TestConfigurations:0:TestTargets:0:EnvironmentVariables'; do /usr/libexec/PlistBuddy -c "Delete ${target_path}:${key}" "$XCTESTRUN_FILE" 2>/dev/null || true; /usr/libexec/PlistBuddy -c "Add ${target_path}:${key} string ${!key}" "$XCTESTRUN_FILE" 2>/dev/null || true; done
  done
}

run_xcode_phase preflight 30 xcodebuild -version || exit "$PRIMARY_EXIT_CODE"
run_optional_xcode_phase simulator-shutdown 60 /usr/bin/xcrun simctl shutdown "$DESTINATION_ID" || exit "$PRIMARY_EXIT_CODE"
run_optional_xcode_phase simulator-boot 60 /usr/bin/xcrun simctl boot "$DESTINATION_ID" || exit "$PRIMARY_EXIT_CODE"
run_xcode_phase simulator-bootstatus 60 /usr/bin/xcrun simctl bootstatus "$DESTINATION_ID" -b || exit "$PRIMARY_EXIT_CODE"
# Each run must start without a saved reconnect token from a prior isolated
# room.  The app is reinstalled by xcodebuild before the UI test; absence is
# harmless and therefore this phase is intentionally idempotent.
run_optional_xcode_phase simulator-uninstall-party-game 60 /usr/bin/xcrun simctl uninstall "$DESTINATION_ID" com.partygame.app || exit "$PRIMARY_EXIT_CODE"
STAGE="ios-source-snapshot"; prepare_ios_source >"${XCODE_LOGS_DIR}/ios-source-snapshot.log" 2>&1 || { set_primary_failure $?; exit "$PRIMARY_EXIT_CODE"; }
run_xcode_phase swiftpm-resolve 180 xcodebuild -project "${IOS_PROJECT_DIR}/PartyGame.xcodeproj" -scheme PartyGame -derivedDataPath "$XCODE_DERIVED_DATA" -clonedSourcePackagesDirPath "$XCODE_SOURCE_PACKAGES" -packageCachePath "$XCODE_PACKAGE_CACHE" -scmProvider system -onlyUsePackageVersionsFromResolvedFile -skipPackageUpdates -resolvePackageDependencies || exit "$PRIMARY_EXIT_CODE"
run_xcode_phase build-for-testing 240 xcodebuild -project "${IOS_PROJECT_DIR}/PartyGame.xcodeproj" -scheme PartyGame -destination "platform=iOS Simulator,id=${DESTINATION_ID}" -derivedDataPath "$XCODE_DERIVED_DATA" -clonedSourcePackagesDirPath "$XCODE_SOURCE_PACKAGES" -packageCachePath "$XCODE_PACKAGE_CACHE" -disableAutomaticPackageResolution -onlyUsePackageVersionsFromResolvedFile -resultBundlePath "${XCODE_DIR}/build-for-testing.xcresult" build-for-testing -only-testing:PartyGameUITests/MixedGameClientE2ETests || exit "$PRIMARY_EXIT_CODE"

API_PORT="$(get_free_port)"; VITE_PORT="$(get_free_port)"; PLAYWRIGHT_API_URL="http://127.0.0.1:${API_PORT}"; VITE_URL="http://127.0.0.1:${VITE_PORT}"; export PLAYWRIGHT_API_URL VITE_URL
STAGE="backend-start"; start_background backend "${E2E_TMP}/api.log" env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$PLAYWRIGHT_API_URL" ConnectionStrings__PartyGame="Data Source=${E2E_TMP}/mixed-e2e.db" MediaStorage__RootPath="${E2E_TMP}/media" GameFlow__WorkerIntervalMilliseconds=100 dotnet run --project "${REPO_DIR}/server/PartyGame.Api/PartyGame.Api.csproj" --no-restore --no-launch-profile || exit "$PRIMARY_EXIT_CODE"
wait_for_http "${PLAYWRIGHT_API_URL}/health" "API health" backend || exit "$PRIMARY_EXIT_CODE"
STAGE="package-and-room-setup"; start_background orchestrator "${E2E_TMP}/orchestrator.log" env PARTYGAME_MIXED_E2E_BACKEND_URL="$PLAYWRIGHT_API_URL" PARTYGAME_E2E_COORDINATION_DIR="$COORDINATION_DIR" dotnet run --project "${REPO_DIR}/scripts/PartyGame.MixedE2EOrchestrator/PartyGame.MixedE2EOrchestrator.csproj" || exit "$PRIMARY_EXIT_CODE"
wait_for_marker "${COORDINATION_DIR}/coordination.json" "public coordination state" 300 backend orchestrator || exit "$PRIMARY_EXIT_CODE"
jq -e '.backendUrl != "" and .roomCode != "" and .contentPackageVersionId != "" and .iosNickname != "" and .displayExpected == true' "${COORDINATION_DIR}/coordination.json" >/dev/null || { set_primary_failure $?; exit "$PRIMARY_EXIT_CODE"; }
ROOM_CODE="$(jq -r '.roomCode' "${COORDINATION_DIR}/coordination.json")"; IOS_NICKNAME="$(jq -r '.iosNickname' "${COORDINATION_DIR}/coordination.json")"
export PARTYGAME_E2E_MODE=1 PARTYGAME_E2E_BACKEND_URL="$PLAYWRIGHT_API_URL" PARTYGAME_E2E_ROOM_CODE="$ROOM_CODE" PARTYGAME_E2E_PLAYER_NICKNAME="$IOS_NICKNAME" PARTYGAME_E2E_COORDINATION_DIR="$COORDINATION_DIR" PARTYGAME_E2E_REQUIRE_GAME_STARTED=1
if [[ "$RUN_MODE" == "ios-only" ]]; then export PARTYGAME_E2E_REQUIRE_GAME_STARTED=0; elif [[ "$RUN_MODE" != "full" ]]; then set_primary_failure 2; exit "$PRIMARY_EXIT_CODE"; fi
STAGE="xctestrun-configuration"; configure_xctestrun || { set_primary_failure $?; exit "$PRIMARY_EXIT_CODE"; }
STAGE="display-server-start"; start_vite "${E2E_TMP}/vite.log" || exit "$PRIMARY_EXIT_CODE"
wait_for_http "$VITE_URL" "Vite" vite || exit "$PRIMARY_EXIT_CODE"
STAGE="ios-test-without-building"; [[ -s "${REPO_DIR}/apps/ios/PartyGameUITests/Fixtures/profile-photo.png" ]] || { set_primary_failure 1; exit "$PRIMARY_EXIT_CODE"; }
run_xcode_phase profile-fixture-import 60 /usr/bin/xcrun simctl addmedia "$DESTINATION_ID" "${REPO_DIR}/apps/ios/PartyGameUITests/Fixtures/profile-photo.png" || exit "$PRIMARY_EXIT_CODE"
start_xcode_phase test-without-building xcodebuild -xctestrun "$XCTESTRUN_FILE" -destination "platform=iOS Simulator,id=${DESTINATION_ID}" -resultBundlePath "${XCODE_DIR}/test-without-building.xcresult" test-without-building -only-testing:PartyGameUITests/MixedGameClientE2ETests
wait_for_marker "${COORDINATION_DIR}/ios-launched" "XCUITest method entry" 900 backend vite orchestrator xcodebuild || exit "$PRIMARY_EXIT_CODE"
wait_for_marker "${COORDINATION_DIR}/ios-profile-saved" "iOS profile save" 900 backend vite orchestrator xcodebuild || exit "$PRIMARY_EXIT_CODE"
if [[ "$RUN_MODE" == "ios-only" ]]; then wait_for_marker "${COORDINATION_DIR}/ios-ready" "iOS Ready" 30 backend vite orchestrator xcodebuild || exit "$PRIMARY_EXIT_CODE"; wait_for_process xcodebuild 240 "iOS XCUITest" backend vite orchestrator || exit "$PRIMARY_EXIT_CODE"; exit 0; fi
STAGE="display-attach"; start_background playwright "${E2E_TMP}/playwright.log" env PARTYGAME_E2E_COORDINATION_DIR="$COORDINATION_DIR" PLAYWRIGHT_OUTPUT_DIR="${E2E_TMP}/playwright-results" PLAYWRIGHT_ARTIFACTS_DIR="${E2E_TMP}/playwright-report" npm --prefix "${REPO_DIR}/apps/display-web" run test:e2e:mixed || exit "$PRIMARY_EXIT_CODE"
wait_for_marker "${COORDINATION_DIR}/display-attached" "display-attached" 300 backend vite orchestrator playwright xcodebuild || exit "$PRIMARY_EXIT_CODE"
STAGE="orchestration-validation"; wait_for_process orchestrator 300 "orchestrator completed" backend vite playwright xcodebuild || exit "$PRIMARY_EXIT_CODE"
wait_for_marker "${COORDINATION_DIR}/ios-ready" "iOS Ready" 30 backend vite playwright xcodebuild || exit "$PRIMARY_EXIT_CODE"
wait_for_marker "${COORDINATION_DIR}/ios-observed-game-start" "iOS game start" 60 backend vite playwright xcodebuild || exit "$PRIMARY_EXIT_CODE"
wait_for_process xcodebuild 240 "XCUITest completed" backend vite playwright || exit "$PRIMARY_EXIT_CODE"
wait_for_process playwright 240 "Display Playwright completed" backend vite || exit "$PRIMARY_EXIT_CODE"
STAGE="outcome-validation"; jq -e '.status == "passed" and .roomPhase == "Completed" and .roomStartedEvents == 1 and .playedQuestionCount == 4 and .uniqueQuestionIdCount == 4 and .playerSelectionCount == 1 and .textAnswerCount == 1 and .photoAnswerCount == 1 and .drawingAnswerCount == 1 and .rankingCount == 3 and .stateVersionMonotonic == true and .iosReconnectCount == 1 and .iosSamePlayerRecovered == true and .iosVersionRegressionCount == 0 and .displayReconnectCount == 1 and .displayVersionRegressionCount == 0 and .iosObservationCount > 0 and .displayObservationCount > 0 and .scriptedPlayerAObservationCount > 0 and .scriptedPlayerBObservationCount > 0 and .backendObservationCount > 0 and .scriptedPlayerAVersionRegressionCount == 0 and .scriptedPlayerBVersionRegressionCount == 0 and .backendVersionRegressionCount == 0 and .stateVersionLedgerPassed == true and .stateVersionLedgerFailureCount == 0 and .ios == "completed" and .display == "completed" and .scriptedPlayers == "completed" and (.questions | length == 4)' "${COORDINATION_DIR}/outcome.json" >/dev/null || { set_primary_failure $?; exit "$PRIMARY_EXIT_CODE"; }
[[ -s "${COORDINATION_DIR}/outcome.json" && -s "${COORDINATION_DIR}/state-version-ledger.json" ]] || { set_primary_failure 1; exit "$PRIMARY_EXIT_CODE"; }
STAGE="completed"; LAST_KNOWN_MARKER="outcome validated"; printf 'PASS: full Mixed Client E2E completed for room %s.\n' "$(mask_room)"
exit 0
