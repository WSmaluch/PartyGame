#!/usr/bin/env bash
set -euo pipefail
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
open "$REPO_DIR/apps/ios/PartyGame.xcodeproj"
