#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$(mktemp -d "${TMPDIR:-/private/tmp}/partygame-photo-upload-benchmark.XXXXXX")"
SERVER_PID=""
cleanup() {
  local status=$?
  [[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null || true
  [[ -n "$SERVER_PID" ]] && wait "$SERVER_PID" 2>/dev/null || true
  if [[ $status -ne 0 && -f "$RUN_DIR/server.log" ]]; then tail -100 "$RUN_DIR/server.log" >&2; fi
  rm -rf "$RUN_DIR"
  return $status
}
trap cleanup EXIT

cd "$REPO_DIR"
dotnet build server/PartyGame.Api/PartyGame.Api.csproj --no-restore
dotnet build scripts/PartyGame.PhotoUploadBenchmark/PartyGame.PhotoUploadBenchmark.csproj
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5056 \
ConnectionStrings__PartyGame="Data Source=$RUN_DIR/game.db" MediaStorage__RootPath="$RUN_DIR/media" \
Diagnostics__LogDirectory="$RUN_DIR/logs" Diagnostics__SupportBundleDirectory="$RUN_DIR/support-bundles" \
GameFlow__WorkerIntervalMilliseconds=100 GameFlow__PhotoAnswerRevealBaseSeconds=0 \
GameFlow__PhotoAnswerRevealPerPhotoSeconds=0 GameFlow__PhotoAnswerRevealMaximumSeconds=0 \
GameFlow__PhotoAnswerResultsSeconds=1 \
dotnet server/PartyGame.Api/bin/Debug/net10.0/PartyGame.Api.dll >"$RUN_DIR/server.log" 2>&1 &
SERVER_PID=$!
for _ in {1..300}; do curl --silent --fail http://127.0.0.1:5056/health >/dev/null && break; sleep 0.1; done
curl --silent --fail http://127.0.0.1:5056/health >/dev/null
PARTYGAME_BENCHMARK_URL=http://127.0.0.1:5056 dotnet scripts/PartyGame.PhotoUploadBenchmark/bin/Debug/net10.0/PartyGame.PhotoUploadBenchmark.dll
