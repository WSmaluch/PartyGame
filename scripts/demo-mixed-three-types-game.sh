#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPOSITORY_ROOT"

echo "Running 3-player, 1-display, 1-round, exact 2/2/2 mixed E2E against a real ASP.NET Core host..."
dotnet test server/PartyGame.Tests/PartyGame.Tests.csproj \
  --filter FullyQualifiedName~PhotoAnswerMixedGameE2ETests.RealHostAndSignalRClient_RunExactTwoTwoTwoPlanToCompleted \
  --logger "console;verbosity=minimal"
