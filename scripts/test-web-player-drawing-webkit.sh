#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-webkit-drawing.XXXXXX")"
ACCESS_FILE="$RUN_DIR/player-access.json"
SERVER_PID=""
VITE_PID=""
DEMO_PID=""

cleanup() {
  local status=$?
  [[ -n "$DEMO_PID" ]] && kill "$DEMO_PID" 2>/dev/null || true
  [[ -n "$VITE_PID" ]] && kill "$VITE_PID" 2>/dev/null || true
  [[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null || true
  [[ -n "$DEMO_PID" ]] && wait "$DEMO_PID" 2>/dev/null || true
  [[ -n "$VITE_PID" ]] && wait "$VITE_PID" 2>/dev/null || true
  [[ -n "$SERVER_PID" ]] && wait "$SERVER_PID" 2>/dev/null || true
  if [[ $status -ne 0 ]]; then
    [[ -f "$RUN_DIR/server.log" ]] && tail -100 "$RUN_DIR/server.log" >&2 || true
    [[ -f "$RUN_DIR/vite.log" ]] && tail -100 "$RUN_DIR/vite.log" >&2 || true
    [[ -f "$RUN_DIR/demo.log" ]] && tail -100 "$RUN_DIR/demo.log" >&2 || true
  fi
  rm -rf "$RUN_DIR"
  return $status
}
trap cleanup EXIT

cd "$REPO_DIR"
dotnet build server/PartyGame.Api/PartyGame.Api.csproj --no-restore
dotnet build scripts/PartyGame.DrawingDemoClient/PartyGame.DrawingDemoClient.csproj
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5054 \
ConnectionStrings__PartyGame="Data Source=$RUN_DIR/game.db" MediaStorage__RootPath="$RUN_DIR/media" \
GameFlow__WorkerIntervalMilliseconds=100 GameFlow__DrawingAnswerRevealBaseSeconds=0 \
GameFlow__DrawingAnswerRevealPerDrawingSeconds=1 GameFlow__DrawingAnswerRevealMaximumSeconds=3 \
GameFlow__DrawingAnswerResultsSeconds=1 \
dotnet server/PartyGame.Api/bin/Debug/net10.0/PartyGame.Api.dll >"$RUN_DIR/server.log" 2>&1 &
SERVER_PID=$!
for _ in {1..300}; do curl --silent --fail http://127.0.0.1:5054/health >/dev/null && break; sleep 0.1; done
curl --silent --fail http://127.0.0.1:5054/health >/dev/null
VITE_API_BASE_URL=http://127.0.0.1:5054 npm --prefix apps/player-web run dev -- --host 127.0.0.1 >"$RUN_DIR/vite.log" 2>&1 &
VITE_PID=$!
for _ in {1..300}; do curl --silent --fail http://127.0.0.1:5175/play/ >/dev/null && break; sleep 0.1; done
curl --silent --fail http://127.0.0.1:5175/play/ >/dev/null
PARTYGAME_DEMO_URL=http://127.0.0.1:5054 PARTYGAME_EXTERNAL_PLAYER_FILE="$ACCESS_FILE" PARTYGAME_EXTERNAL_PLAYER_INDEX=1 \
dotnet scripts/PartyGame.DrawingDemoClient/bin/Debug/net10.0/PartyGame.DrawingDemoClient.dll >"$RUN_DIR/demo.log" 2>&1 &
DEMO_PID=$!
node scripts/test-web-player-drawing-webkit.mjs http://127.0.0.1:5175 "$ACCESS_FILE"
