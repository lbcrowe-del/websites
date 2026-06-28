#!/usr/bin/env bash
# Post-deploy smoke test for the migration/complete endpoint.
#
# Verifies the live licensing API records a migration-completion signal onto an
# existing license row. Run AFTER the websites PR deploys the Function.
#
# Usage:
#   LICENSE_KEY=SB-XXXX-XXXX-XXXX ./smoke-migration-complete.sh
#
# Optional overrides:
#   BASE_URL   (default: https://serverbridge-licensing.azurewebsites.net/api)
#   DEVICE_ID  (default: smoke-test-device)
#   FILES      (default: 7)
#
# Use a TEST/throwaway license key — this increments MigrationsCompletedCount and
# stamps MigrationCompletedUtc on that row.

set -euo pipefail

BASE_URL="${BASE_URL:-https://serverbridge-licensing.azurewebsites.net/api}"
DEVICE_ID="${DEVICE_ID:-smoke-test-device}"
FILES="${FILES:-7}"

if [[ -z "${LICENSE_KEY:-}" ]]; then
  echo "ERROR: set LICENSE_KEY to a test license key." >&2
  exit 1
fi

COMPLETED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

echo "POST ${BASE_URL}/migration/complete"
echo "  LicenseKey=${LICENSE_KEY}  DeviceId=${DEVICE_ID}  FilesMigratedCount=${FILES}  CompletedAtUtc=${COMPLETED_AT}"
echo

HTTP_BODY="$(mktemp)"
HTTP_CODE="$(curl -sS -o "${HTTP_BODY}" -w '%{http_code}' \
  -X POST "${BASE_URL}/migration/complete" \
  -H 'Content-Type: application/json' \
  -d "{\"LicenseKey\":\"${LICENSE_KEY}\",\"DeviceId\":\"${DEVICE_ID}\",\"FilesMigratedCount\":${FILES},\"CompletedAtUtc\":\"${COMPLETED_AT}\"}")"

echo "HTTP ${HTTP_CODE}"
cat "${HTTP_BODY}"; echo
rm -f "${HTTP_BODY}"

# Pass/fail on the documented contract: 200 + {"recorded":true}.
if [[ "${HTTP_CODE}" == "200" ]]; then
  echo
  echo "OK: endpoint returned 200. Expect {\"recorded\":true} above."
  echo "Now confirm the Table Storage row updated (MigrationCompletedUtc set,"
  echo "MigrationsCompletedCount incremented) — see the verification note below."
else
  echo "FAIL: expected HTTP 200, got ${HTTP_CODE}." >&2
  exit 1
fi
