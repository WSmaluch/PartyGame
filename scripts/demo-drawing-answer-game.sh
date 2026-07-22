#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEMO_TMP="$(mktemp -d "${TMPDIR:-/tmp}/partygame-drawing-demo.XXXXXX")"
DEMO_PORT=5004
SERVER_PID=""
cleanup() {
  local status=$?
  if [[ -n "$SERVER_PID" ]]; then kill "$SERVER_PID" 2>/dev/null || true; fi
  if [[ $status -ne 0 && -f "${DEMO_TMP}/server.log" ]]; then
    grep -E -A12 -B3 "ERR|Error|Exception|exception" "${DEMO_TMP}/server.log" | tail -160 >&2 || true
  fi
  rm -rf "$DEMO_TMP"
  return $status
}
trap cleanup EXIT

cd "$REPOSITORY_ROOT"
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://127.0.0.1:${DEMO_PORT}" \
ConnectionStrings__PartyGame="Data Source=${DEMO_TMP}/demo.db" \
MediaStorage__RootPath="${DEMO_TMP}/media" \
GameFlow__WorkerIntervalMilliseconds=100 \
GameFlow__DrawingAnswerRevealBaseSeconds=0 \
GameFlow__DrawingAnswerRevealPerDrawingSeconds=1 \
GameFlow__DrawingAnswerRevealMaximumSeconds=3 \
GameFlow__DrawingAnswerResultsSeconds=1 \
dotnet run --no-launch-profile --project server/PartyGame.Api --urls "http://127.0.0.1:${DEMO_PORT}" >"${DEMO_TMP}/server.log" 2>&1 &
SERVER_PID=$!

for _ in {1..100}; do
  if curl --silent --fail "http://127.0.0.1:${DEMO_PORT}/health" >/dev/null; then break; fi
  sleep 0.1
done
curl --silent --fail "http://127.0.0.1:${DEMO_PORT}/health" >/dev/null

PARTYGAME_DEMO_URL="http://127.0.0.1:${DEMO_PORT}" dotnet run --project scripts/PartyGame.DrawingDemoClient
