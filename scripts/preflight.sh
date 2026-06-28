#!/usr/bin/env bash
#
# Preflight test ladder for the ServerBridge licensing API (websites repo).
#
# Usage:
#   scripts/preflight.sh            # build API + Azurite-backed integration tests — default before any API change ships
#   scripts/preflight.sh --smoke    # also run the post-deploy smoke test against the LIVE Function App
#
# --smoke needs LICENSE_KEY set to a throwaway test license key.
set -euo pipefail
cd "$(dirname "$0")/.."

RUN_SMOKE=false
for a in "$@"; do
  case "$a" in
    --smoke) RUN_SMOKE=true ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "unknown arg: $a" >&2; exit 2 ;;
  esac
done

API=server-bridge/api/ServerBridge.LicensingApi.csproj
TESTS=server-bridge/api-tests/ServerBridge.LicensingApi.Tests.csproj

table_up() { (exec 3<>/dev/tcp/127.0.0.1/10002) 2>/dev/null; }

echo "==> build API"
dotnet build "$API" -c Release

STARTED_AZURITE=false
AZ_PID=""
if ! table_up; then
  command -v azurite >/dev/null || { echo "azurite not installed — run: npm i -g azurite" >&2; exit 1; }
  echo "==> starting Azurite (Table emulator)"
  AZ_DIR="$(mktemp -d)"
  azurite --silent --location "$AZ_DIR" >/dev/null 2>&1 &
  AZ_PID=$!
  STARTED_AZURITE=true
  for _ in $(seq 1 30); do table_up && break; sleep 1; done
fi
cleanup() { if $STARTED_AZURITE && [ -n "$AZ_PID" ]; then kill "$AZ_PID" 2>/dev/null || true; fi; }
trap cleanup EXIT

echo "==> integration tests (Azurite-backed)"
dotnet test "$TESTS" --filter "Category=Integration"

if $RUN_SMOKE; then
  echo "==> prod smoke test (live Function App)"
  : "${LICENSE_KEY:?set LICENSE_KEY to a throwaway test license key to run --smoke}"
  server-bridge/api/scripts/smoke-migration-complete.sh
fi

echo "PREFLIGHT OK"
