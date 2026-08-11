#!/usr/bin/env bash
# Shared safe primitives for PartyGame diagnostics scripts.  They intentionally
# never dereference an input symlink and never print secret values.
set -euo pipefail

diagnostics_die() { echo "PartyGame diagnostics: $*" >&2; exit 64; }

diagnostics_require_absolute_directory() {
  local value="$1" label="$2"
  [[ "$value" = /* ]] || diagnostics_die "$label must be an absolute path."
  [[ -d "$value" && ! -L "$value" ]] || diagnostics_die "$label must be an existing non-symlink directory."
}

diagnostics_redact_stream() {
  # Keep context while removing credentials, cookies, query values, host paths
  # and client addresses. Rules cover compact JSON as well as text logs.
  sed -E \
    -e 's/(Authorization["[:space:]:=]+)(Bearer[[:space:]]+)?[^,"[:space:]]+/\1[REDACTED]/Ig' \
    -e 's/(Bearer[[:space:]]+)[^,"[:space:]]+/\1[REDACTED]/Ig' \
    -e 's/((Set-)?Cookie["[:space:]:=]+)[^,"[:space:]]+/\1[REDACTED]/Ig' \
    -e 's/((access|reconnect|operator)[_-]?token["[:space:]:=]+)[^,"[:space:]]+/\1[REDACTED]/Ig' \
    -e 's#(/Users/)[^/[:space:]" ]+#\1[REDACTED]#g' \
    -e 's#(/home/)[^/[:space:]" ]+#\1[REDACTED]#g' \
    -e 's/([0-9]{1,3}\.){3}[0-9]{1,3}/[REDACTED-IP]/g' \
    -e 's/([?&][A-Za-z0-9_.-]+=)[^&[:space:]" ]+/\1[REDACTED]/g'
}

diagnostics_is_safe_file() {
  local root="$1" candidate="$2"
  [[ -f "$candidate" && ! -L "$candidate" ]] || return 1
  local resolved_root resolved_candidate
  resolved_root="$(cd -P "$root" && pwd)"
  resolved_candidate="$(cd -P "$(dirname "$candidate")" && pwd)/$(basename "$candidate")"
  [[ "$resolved_candidate" == "$resolved_root"/* ]]
}

diagnostics_assert_redacted() {
  local directory="$1"
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  [[ -d "$directory" && ! -L "$directory" ]] || diagnostics_die "secret audit directory must be a non-symlink directory."
  node "$script_dir/diagnostics-secret-audit.mjs" "$directory"
}
