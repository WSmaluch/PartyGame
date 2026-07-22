#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPOSITORY_ROOT"

echo "Running the current ASP.NET Core + official SignalR client regression (includes PlayerSelection and reaches Completed)..."
dotnet test server/PartyGame.Tests/PartyGame.Tests.csproj \
  --filter FullyQualifiedName~PhotoAnswerMixedGameE2ETests.RealHostAndSignalRClient_RunExactTwoTwoTwoPlanToCompleted \
  --logger "console;verbosity=minimal"
