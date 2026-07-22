#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
E2E_TMP="$(mktemp -d "${TMPDIR:-/tmp}/partygame-display-e2e.XXXXXX")"
export E2E_TMP

API_PID=""
VITE_PID=""

cleanup() {
  local status=$?
  if [[ -n "$VITE_PID" ]]; then kill "$VITE_PID" 2>/dev/null || true; fi
  if [[ -n "$API_PID" ]]; then kill "$API_PID" 2>/dev/null || true; fi
  if [[ $status -ne 0 ]]; then
    echo "================ API LOGS ================"
    tail -n 100 "${E2E_TMP}/api.log" || true
    echo "================ VITE LOGS ================"
    tail -n 100 "${E2E_TMP}/vite.log" || true
  fi
  rm -rf "$E2E_TMP"
  return $status
}
trap cleanup EXIT

# Get random free ports
function get_free_port() {
  python3 -c 'import socket; s=socket.socket(); s.bind(("", 0)); print(s.getsockname()[1]); s.close()'
}

API_PORT=$(get_free_port)
VITE_PORT=$(get_free_port)
export PLAYWRIGHT_API_URL="http://127.0.0.1:${API_PORT}"
export VITE_URL="http://127.0.0.1:${VITE_PORT}"
export PARTYGAME_E2E_BACKEND_URL="$PLAYWRIGHT_API_URL"

echo "Using API_PORT=${API_PORT}, VITE_PORT=${VITE_PORT}, TMP=${E2E_TMP}"

cd "$REPO_DIR"
# Start API
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="${PLAYWRIGHT_API_URL}" \
ConnectionStrings__PartyGame="Data Source=${E2E_TMP}/e2e.db" \
MediaStorage__RootPath="${E2E_TMP}/media" \
GameFlow__WorkerIntervalMilliseconds=100 \
GameFlow__DrawingAnswerRevealBaseSeconds=8 \
GameFlow__DrawingAnswerRevealPerDrawingSeconds=0 \
GameFlow__DrawingAnswerRevealMaximumSeconds=8 \
GameFlow__DrawingAnswerResultsSeconds=4 \
dotnet run --project server/PartyGame.Api/PartyGame.Api.csproj --no-restore --no-build --no-launch-profile >"${E2E_TMP}/api.log" 2>&1 &
API_PID=$!

echo "Waiting for API..."
for _ in {1..150}; do
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

# Run tests
npm run test:e2e:drawing
