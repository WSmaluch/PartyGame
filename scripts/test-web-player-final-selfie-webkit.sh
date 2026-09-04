#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-webkit-final-selfie.XXXXXX")"
SERVER_PID=""
PLAYER_PID=""

cleanup() {
  local status=$?
  [[ -n "$PLAYER_PID" ]] && kill "$PLAYER_PID" 2>/dev/null || true
  [[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null || true
  [[ -n "$PLAYER_PID" ]] && wait "$PLAYER_PID" 2>/dev/null || true
  [[ -n "$SERVER_PID" ]] && wait "$SERVER_PID" 2>/dev/null || true
  if [[ $status -ne 0 ]]; then
    [[ -f "$RUN_DIR/server.log" ]] && tail -100 "$RUN_DIR/server.log" >&2 || true
    [[ -f "$RUN_DIR/player.log" ]] && tail -100 "$RUN_DIR/player.log" >&2 || true
  fi
  rm -rf "$RUN_DIR"
  return $status
}
trap cleanup EXIT

cd "$REPO_DIR"
dotnet build server/PartyGame.Api/PartyGame.Api.csproj --no-restore
# This deliberately serves the built bundle through Vite preview.  The test is
# against /play/, not Vite's development transform server.
VITE_API_BASE_URL=http://127.0.0.1:5055 npm --prefix apps/player-web run build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5055 \
ConnectionStrings__PartyGame="Data Source=$RUN_DIR/game.db" \
MediaStorage__RootPath="$RUN_DIR/media" \
Diagnostics__LogDirectory="$RUN_DIR/logs" \
Diagnostics__SupportBundleDirectory="$RUN_DIR/support-bundles" \
GameFlow__WorkerIntervalMilliseconds=100 \
dotnet server/PartyGame.Api/bin/Debug/net10.0/PartyGame.Api.dll >"$RUN_DIR/server.log" 2>&1 &
SERVER_PID=$!
for _ in {1..300}; do curl --silent --fail http://127.0.0.1:5055/health >/dev/null && break; sleep 0.1; done
curl --silent --fail http://127.0.0.1:5055/health >/dev/null
npm --prefix apps/player-web run preview -- --host 127.0.0.1 --port 5176 >"$RUN_DIR/player.log" 2>&1 &
PLAYER_PID=$!
for _ in {1..300}; do curl --silent --fail http://127.0.0.1:5176/play/ >/dev/null && break; sleep 0.1; done
curl --silent --fail http://127.0.0.1:5176/play/ >/dev/null
node scripts/test-web-player-final-selfie-webkit.mjs http://127.0.0.1:5176 http://127.0.0.1:5055
