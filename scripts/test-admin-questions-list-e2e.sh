#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$(mktemp -d)"; API_PORT="${PARTYGAME_E2E_API_PORT:-15051}"; ADMIN_PORT="${PARTYGAME_E2E_ADMIN_PORT:-15175}"; API_PID=""; ADMIN_PID=""
cleanup() { [[ -n "$ADMIN_PID" ]] && kill "$ADMIN_PID" 2>/dev/null || true; [[ -n "$API_PID" ]] && kill "$API_PID" 2>/dev/null || true; rm -rf "$RUN_DIR"; }
trap cleanup EXIT
ConnectionStrings__PartyGame="Data Source=$RUN_DIR/questions.db" ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://127.0.0.1:$API_PORT" dotnet run --no-launch-profile --project "$ROOT_DIR/server/PartyGame.Api" >"$RUN_DIR/api.log" 2>&1 & API_PID=$!
for _ in {1..120}; do curl -fsS "http://127.0.0.1:$API_PORT/health" >/dev/null 2>&1 && break; sleep 0.25; done
curl -fsS "http://127.0.0.1:$API_PORT/health" >/dev/null
VITE_API_BASE_URL="http://127.0.0.1:$API_PORT" npm --prefix "$ROOT_DIR/apps/admin-web" run dev -- --host 127.0.0.1 --port "$ADMIN_PORT" >"$RUN_DIR/admin.log" 2>&1 & ADMIN_PID=$!
for _ in {1..120}; do curl -fsS "http://127.0.0.1:$ADMIN_PORT/admin/content" >/dev/null 2>&1 && break; sleep 0.25; done
curl -fsS "http://127.0.0.1:$ADMIN_PORT/admin/content" >/dev/null
ADMIN_E2E_API_URL="http://127.0.0.1:$API_PORT" ADMIN_E2E_BASE_URL="http://127.0.0.1:$ADMIN_PORT" npm --prefix "$ROOT_DIR/apps/admin-web" run test:e2e:questions-list:playwright
