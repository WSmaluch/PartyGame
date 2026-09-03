#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=lib/manual-common.sh
source "$SCRIPT_DIR/lib/manual-common.sh"
manual_parse_port "$@"
manual_detect_host

if manual_stop_port; then
  echo "Port $MANUAL_PORT is free."
fi

for tool in git dotnet node npm curl lsof ps; do command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 69; }; done

short_hash="$(git -C "$REPO_DIR" rev-parse --short=12 HEAD)"
version="$(tr -d '[:space:]' < "$REPO_DIR/release/VERSION")"
manual_root="/private/tmp/partygame-manual-$short_hash"
runtime_root="/private/tmp/partygame-manual-runtime-$MANUAL_PORT"
build_stage="$(mktemp -d /private/tmp/partygame-manual-build.XXXXXX)"
cleanup_stage() { rm -rf "$build_stage"; }
trap cleanup_stage EXIT INT TERM

echo "Building PartyGame $version from $short_hash..."
dotnet restore "$REPO_DIR/PartyGame.sln"
dotnet publish "$REPO_DIR/server/PartyGame.Api/PartyGame.Api.csproj" --no-restore --configuration Release --output "$build_stage/api" \
  -p:PartyGameReleaseVersion="$version" -p:Version="$version" -p:InformationalVersion="$version" -p:SourceRevisionId="$short_hash"

for app in display-web admin-web player-web; do
  case "$app" in display-web) base=/display/ ;; admin-web) base=/admin/ ;; player-web) base=/play/ ;; esac
  npm --prefix "$REPO_DIR/apps/$app" ci
  PARTYGAME_BUILD_VERSION="$version" PARTYGAME_WEB_BASE_PATH="$base" npm --prefix "$REPO_DIR/apps/$app" run build
  target="${app%-web}"
  cp -R "$REPO_DIR/apps/$app/dist" "$build_stage/$target"
done

base_url="http://$MANUAL_HOST:$MANUAL_PORT"
node "$REPO_DIR/scripts/release-assets.mjs" config "$build_stage/display/config.json" "$base_url" "$base_url/display/" "$version" "$short_hash"
node "$REPO_DIR/scripts/release-assets.mjs" config "$build_stage/admin/config.json" "$base_url" "$base_url/admin/" "$version" "$short_hash"
node "$REPO_DIR/scripts/release-assets.mjs" config "$build_stage/player/config.json" "$base_url" "$base_url/play/" "$version" "$short_hash"

[[ "$manual_root" == /private/tmp/partygame-manual-* ]] || { echo "Unsafe manual build path." >&2; exit 70; }
rm -rf "$manual_root"
mv "$build_stage" "$manual_root"
trap - EXIT INT TERM
mkdir -p "$runtime_root"/{database,media,logs}
operator_token="$(node -e 'process.stdout.write(require("crypto").randomBytes(32).toString("hex"))')"
log_file="$runtime_root/logs/partygame-$(date -u +%Y%m%dT%H%M%SZ).log"

(
  cd "$manual_root/api"
  export ASPNETCORE_ENVIRONMENT=Production
  export PARTYGAME_URLS="http://0.0.0.0:$MANUAL_PORT"
  export PARTYGAME_DATABASE_PATH="$runtime_root/database/partygame.db"
  export PARTYGAME_MEDIA_ROOT="$runtime_root/media"
  export PARTYGAME_PUBLIC_BASE_URL="$base_url"
  export PARTYGAME_DISPLAY_PUBLIC_URL="$base_url/display/"
  export PARTYGAME_ADMIN_PUBLIC_URL="$base_url/admin/"
  export PARTYGAME_PLAYER_PUBLIC_URL="$base_url/play/"
  export PARTYGAME_ALLOWED_ORIGINS="$base_url"
  export PARTYGAME_ALLOW_INSECURE_LAN_HTTP=true
  export PARTYGAME_APPLY_MIGRATIONS=true
  export PARTYGAME_OPERATOR_TOKEN="$operator_token"
  export PARTYGAME_DEPLOYMENT_ENABLED=true
  export PARTYGAME_DISPLAY_ROOT="$manual_root/display"
  export PARTYGAME_ADMIN_ROOT="$manual_root/admin"
  export PARTYGAME_PLAYER_ROOT="$manual_root/player"
  export PARTYGAME_DISPLAY_PATH_BASE=/display
  export PARTYGAME_ADMIN_PATH_BASE=/admin
  export PARTYGAME_PLAYER_PATH_BASE=/play
  exec nohup dotnet "$manual_root/api/PartyGame.Api.dll"
) >> "$log_file" 2>&1 &
server_pid=$!

deadline=$((SECONDS + 35))
until curl --noproxy '*' --fail --silent "$base_url/health/ready" >/dev/null 2>&1; do
  if ! kill -0 "$server_pid" 2>/dev/null || (( SECONDS >= deadline )); then
    kill -TERM "$server_pid" 2>/dev/null || true
    echo "PartyGame failed readiness. Log: $log_file" >&2
    exit 1
  fi
  sleep 1
done
for path in /display/ /play/ /admin/; do curl --noproxy '*' --fail --silent "$base_url$path" >/dev/null; done

printf '\nPartyGame READY\n\nServer:\n%s\n\nDisplay:\n%s/display/\n\nWeb Player:\n%s/play/\n\nAdmin:\n%s/admin/\n\niOS server URL:\n%s\n\nLog:\n%s\n' \
  "$base_url" "$base_url" "$base_url" "$base_url" "$base_url" "$log_file"
