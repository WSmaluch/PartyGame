#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet test server/PartyGame.Tests/PartyGame.Tests.csproj \
  --filter "FullyQualifiedName~LobbyFlow_StartsOnceAndSupportsDisconnectAndReconnect" \
  --logger "console;verbosity=minimal"
