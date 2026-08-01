#!/usr/bin/env bash
# Shared, deliberately small primitives for the PartyGame single-host LAN deployment.
set -euo pipefail

LAN_REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LAN_DEPLOY_ROOT="${PARTYGAME_DEPLOY_ROOT:-}"
LAN_RELEASE_DIR="${PARTYGAME_RELEASE_DIR:-}"
LAN_HOST="${PARTYGAME_LAN_HOST:-}"
LAN_PORT="${PARTYGAME_LAN_PORT:-5050}"
LAN_WAIT_SECONDS="${PARTYGAME_LAN_WAIT_SECONDS:-30}"

lan_die() { echo "PartyGame LAN: $*" >&2; exit 64; }

lan_parse_arguments() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --deploy-root) LAN_DEPLOY_ROOT="${2:-}"; shift 2 ;;
      --release-dir) LAN_RELEASE_DIR="${2:-}"; shift 2 ;;
      --host) LAN_HOST="${2:-}"; shift 2 ;;
      --port) LAN_PORT="${2:-}"; shift 2 ;;
      *) lan_die "unknown option: $1" ;;
    esac
  done
  [[ -n "$LAN_DEPLOY_ROOT" ]] || lan_die "--deploy-root (or PARTYGAME_DEPLOY_ROOT) is required."
  [[ "$LAN_DEPLOY_ROOT" = /* ]] || lan_die "deploy root must be an absolute path."
  [[ "$LAN_PORT" =~ ^[1-9][0-9]{0,4}$ ]] && (( LAN_PORT <= 65535 )) || lan_die "port must be in 1..65535."
  LAN_DEPLOY_ROOT="${LAN_DEPLOY_ROOT%/}"
}

lan_runtime_dir() { printf '%s/runtime' "$LAN_DEPLOY_ROOT"; }
lan_pid_file() { printf '%s/pid/partygame-api.pid' "$(lan_runtime_dir)"; }
lan_pid_meta_file() { printf '%s/pid/partygame-api.meta' "$(lan_runtime_dir)"; }
lan_env_file() { printf '%s/config/partygame.env' "$LAN_DEPLOY_ROOT"; }
lan_current_link() { printf '%s/current' "$LAN_DEPLOY_ROOT"; }

lan_is_private_ipv4() {
  local ip="$1" a b c d
  IFS=. read -r a b c d <<< "$ip" || return 1
  [[ "$a" =~ ^[0-9]+$ && "$b" =~ ^[0-9]+$ && "$c" =~ ^[0-9]+$ && "$d" =~ ^[0-9]+$ ]] || return 1
  (( a <= 255 && b <= 255 && c <= 255 && d <= 255 )) || return 1
  (( a == 10 || (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168) ))
}

lan_detect_private_ipv4s() {
  if command -v ip >/dev/null 2>&1; then
    ip -4 -o addr show scope global 2>/dev/null | awk '{split($4, parts, "/"); print parts[1]}'
  elif command -v ifconfig >/dev/null 2>&1; then
    ifconfig 2>/dev/null | awk '/inet / {print $2}'
  fi | while IFS= read -r ip; do lan_is_private_ipv4 "$ip" && printf '%s\n' "$ip"; done
}

lan_resolve_host() {
  if [[ -n "$LAN_HOST" ]]; then
    [[ "$LAN_HOST" != "0.0.0.0" && "$LAN_HOST" != "127.0.0.1" ]] || lan_die "--host must be a reachable LAN address, not $LAN_HOST."
    lan_is_private_ipv4 "$LAN_HOST" || lan_die "--host must be a private IPv4 LAN address."
    return
  fi
  local -a candidates=()
  while IFS= read -r ip; do [[ -n "$ip" ]] && candidates+=("$ip"); done < <(lan_detect_private_ipv4s | sort -u)
  if (( ${#candidates[@]} == 1 )); then LAN_HOST="${candidates[0]}"; return; fi
  if (( ${#candidates[@]} == 0 )); then lan_die "no private IPv4 address found; pass --host <LAN-IP>."; fi
  lan_die "multiple private IPv4 addresses found (${candidates[*]}); pass --host <LAN-IP>."
}

lan_url() { printf 'http://%s:%s' "$LAN_HOST" "$LAN_PORT"; }

lan_prepare_runtime() {
  mkdir -p "$(lan_runtime_dir)"/{database,media,logs,pid,temp} "$LAN_DEPLOY_ROOT/config" "$LAN_DEPLOY_ROOT/releases"
}

lan_current_release() {
  local current; current="$(lan_current_link)"
  [[ -L "$current" ]] || lan_die "current release is not configured. Run deploy-lan.sh first."
  local release; release="$(cd "$current" && pwd -P)"
  [[ "$release" == "$LAN_DEPLOY_ROOT"/releases/* ]] || lan_die "current symlink points outside releases."
  printf '%s' "$release"
}

lan_assert_release_layout() {
  local release="$1"
  [[ -f "$release/manifest.json" && -f "$release/checksums.sha256" && -f "$release/api/PartyGame.Api.dll" ]] || lan_die "release is incomplete: $release"
  [[ -f "$release/display/index.html" && -f "$release/admin/index.html" ]] || lan_die "release is missing Display or Admin static files: $release"
}

lan_release_version() {
  node "$LAN_REPO_DIR/scripts/release-assets.mjs" version "$1/manifest.json"
}

lan_verify_release() {
  local release="$1"
  local checks="$release/checksums.sha256"
  lan_assert_release_layout "$release"
  node -e 'const fs=require("fs"); const m=JSON.parse(fs.readFileSync(process.argv[1])); if(!m.version||!m.checksums||!m.artifacts) process.exit(1)' "$release/manifest.json" || lan_die "invalid manifest: $release/manifest.json"
  (cd "$release" && shasum -a 256 -c "$(basename "$checks")") || lan_die "checksum validation failed for $release"
}

lan_verify_installed_release() {
  local release="$1"
  lan_assert_release_layout "$release"
  node -e 'const fs=require("fs"); const m=JSON.parse(fs.readFileSync(process.argv[1])); if(!m.version||!m.checksums||!m.artifacts) process.exit(1)' "$release/manifest.json" || lan_die "invalid manifest: $release/manifest.json"
  # config.json is deliberately substituted at deployment time; every other release file
  # remains byte-for-byte covered by the source manifest.
  (cd "$release" && grep -v -E '  (display|admin)/config\\.json$' checksums.sha256 | shasum -a 256 -c -) || lan_die "installed release checksum validation failed for $release"
}

lan_write_environment() {
  lan_prepare_runtime
  local file; file="$(lan_env_file)"
  local url; url="$(lan_url)"
  umask 077
  cat > "$file" <<EOF
# Generated by deploy-lan.sh. Edit host/port only through a new deploy or explicit options.
PARTYGAME_LAN_HOST=$LAN_HOST
PARTYGAME_LAN_PORT=$LAN_PORT
PARTYGAME_DEPLOY_ROOT=$LAN_DEPLOY_ROOT
PARTYGAME_URLS=http://0.0.0.0:$LAN_PORT
PARTYGAME_DATABASE_PATH=$(lan_runtime_dir)/database/partygame.db
PARTYGAME_MEDIA_ROOT=$(lan_runtime_dir)/media
PARTYGAME_LOG_LEVEL=Information
PARTYGAME_APPLY_MIGRATIONS=true
PARTYGAME_DEPLOYMENT_ENABLED=true
PARTYGAME_DISPLAY_ROOT=$(lan_current_link)/display
PARTYGAME_ADMIN_ROOT=$(lan_current_link)/admin
PARTYGAME_DISPLAY_PATH_BASE=/display
PARTYGAME_ADMIN_PATH_BASE=/admin
PARTYGAME_PUBLIC_BASE_URL=$url
PARTYGAME_DISPLAY_PUBLIC_URL=$url/display/
PARTYGAME_ADMIN_PUBLIC_URL=$url/admin/
PARTYGAME_ALLOWED_ORIGINS=$url
EOF
}

lan_load_environment() {
  local file; file="$(lan_env_file)"
  [[ -f "$file" ]] || lan_die "deployment configuration is missing: $file"
  # The file is generated locally by deploy-lan.sh and contains only shell assignments.
  set -a
  # shellcheck disable=SC1090
  source "$file"
  set +a
  LAN_HOST="${PARTYGAME_LAN_HOST:-$LAN_HOST}"
  LAN_PORT="${PARTYGAME_LAN_PORT:-$LAN_PORT}"
}

lan_pid_is_ours() {
  local pid="$1" release="$2" command
  kill -0 "$pid" 2>/dev/null || return 1
  command="$(ps -p "$pid" -o command= 2>/dev/null || true)"
  [[ "$command" == *"$release/api/PartyGame.Api.dll"* ]]
}

lan_pid_is_deployment_process() {
  local pid="$1" command
  kill -0 "$pid" 2>/dev/null || return 1
  command="$(ps -p "$pid" -o command= 2>/dev/null || true)"
  [[ "$command" == *"$LAN_DEPLOY_ROOT/releases/"*"/api/PartyGame.Api.dll"* ]]
}

lan_stop_deployment_pid() {
  local pid="$1"
  lan_pid_is_deployment_process "$pid" || return 1
  kill -TERM "$pid"
  local deadline=$((SECONDS + LAN_WAIT_SECONDS))
  while kill -0 "$pid" 2>/dev/null && (( SECONDS < deadline )); do sleep 1; done
  if kill -0 "$pid" 2>/dev/null; then kill -KILL "$pid"; sleep 1; fi
  ! kill -0 "$pid" 2>/dev/null
}

lan_read_pid() {
  local file; file="$(lan_pid_file)"
  [[ -f "$file" ]] || return 1
  local pid; pid="$(tr -d '[:space:]' < "$file")"
  [[ "$pid" =~ ^[1-9][0-9]*$ ]] || return 2
  printf '%s' "$pid"
}

lan_port_is_free() {
  if command -v lsof >/dev/null 2>&1; then ! lsof -nP -iTCP:"$LAN_PORT" -sTCP:LISTEN >/dev/null 2>&1
  elif command -v nc >/dev/null 2>&1; then ! nc -z 127.0.0.1 "$LAN_PORT" >/dev/null 2>&1
  else return 0; fi
}

lan_wait_ready() {
  local url="$1" deadline=$((SECONDS + LAN_WAIT_SECONDS))
  while (( SECONDS < deadline )); do
    if curl --fail --silent --show-error "$url/health/ready" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}

lan_print_urls() {
  local url; url="$(lan_url)"
  printf 'PartyGame LAN ready:\n  Display: %s/display/\n  Admin:   %s/admin/\n  API:     %s/api/\n' "$url" "$url" "$url"
}
