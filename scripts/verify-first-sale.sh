#!/usr/bin/env bash
#
# Verify a real (live-mode) Stripe sale flowed end-to-end through the licensing pipeline:
# Stripe webhook -> Function App -> Licenses table -> license/status API.
#
# Run this after the FIRST real sale (or any sale you want to spot-check). Takes the
# Stripe Checkout Session ID or the license key as input — whichever you have on hand.
#
# Usage:
#   scripts/verify-first-sale.sh --session cs_live_...
#   scripts/verify-first-sale.sh --license SB-XXXXX-XXXXX-XXXXX-XXXXX
#
# Requires: az (logged in), stripe CLI (logged in, live mode key), jq.
set -euo pipefail

SUBSCRIPTION_ID="4befc9c5-1865-41cb-9b94-911ccb757a6c"
RESOURCE_GROUP="websites_rg"
FUNCTION_APP="serverbridge-licensing"
STORAGE_ACCOUNT="serverbridgelicenses"
TABLE_NAME="Licenses"
API_BASE="https://serverbridge-licensing.azurewebsites.net/api"

SESSION_ID=""
LICENSE_KEY=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --session) SESSION_ID="$2"; shift 2 ;;
    --license) LICENSE_KEY="$2"; shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$SESSION_ID" && -z "$LICENSE_KEY" ]]; then
  echo "Pass --session <checkout session id> or --license <license key>." >&2
  exit 2
fi

az account set --subscription "$SUBSCRIPTION_ID"
STORAGE_CONN=$(az storage account show-connection-string \
  --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --query connectionString -o tsv 2>/dev/null)

echo "=== 1. Stripe — checkout session + webhook delivery ==="
if [[ -n "$SESSION_ID" ]]; then
  stripe checkout sessions retrieve "$SESSION_ID" --live \
    | jq '{id, payment_status, customer, customer_details, amount_total, currency}'
  EVENT_ID=$(stripe events list --live --type checkout.session.completed --limit 20 \
    | jq -r --arg sid "$SESSION_ID" '.data[] | select(.data.object.id == $sid) | .id' | head -1)
  if [[ -n "${EVENT_ID:-}" ]]; then
    echo "--- matching event: $EVENT_ID ---"
    stripe events retrieve "$EVENT_ID" --live | jq '{id, type, created}'
  else
    echo "No matching checkout.session.completed event found in the last 20 live events."
  fi
else
  echo "(no --session given, skipping Stripe-side check)"
fi
echo

echo "=== 2. Function App — recent invocations (Application Insights) ==="
APP_ID=$(az monitor app-insights component show \
  --app "$FUNCTION_APP" --resource-group "$RESOURCE_GROUP" --query appId -o tsv 2>/dev/null | tail -1)
az monitor app-insights query \
  --app "$APP_ID" \
  --analytics-query "requests | where timestamp > ago(1h) | where name has 'Stripe' or name has 'License' | project timestamp, name, resultCode, success, duration | order by timestamp desc | take 20" \
  -o table
echo

echo "=== 3. Licenses table — matching row ==="
if [[ -n "$LICENSE_KEY" ]]; then
  az storage entity show \
    --account-name "$STORAGE_ACCOUNT" --table-name "$TABLE_NAME" --connection-string "$STORAGE_CONN" \
    --partition-key "license" --row-key "$LICENSE_KEY" \
    -o json 2>/dev/null | jq '{RowKey, Tier, Active, StripeCustomerId, EulaVersion, EulaAcceptedUtc}' \
    || echo "No row found for license key $LICENSE_KEY yet."
else
  echo "Most recent 5 rows in the table (no --license given, so showing recent activity):"
  az storage entity query \
    --account-name "$STORAGE_ACCOUNT" --table-name "$TABLE_NAME" --connection-string "$STORAGE_CONN" \
    --query "items[-5:]" -o json | jq '.[] | {RowKey, Tier, Active, StripeCustomerId, EulaAcceptedUtc}'
fi
echo

if [[ -n "$LICENSE_KEY" ]]; then
  echo "=== 4. license/status round-trip against the live API ==="
  curl -s -X POST "$API_BASE/license/status" \
    -H "Content-Type: application/json" \
    -d "{\"LicenseKey\":\"$LICENSE_KEY\"}" | jq .
  echo
fi

echo "=== Done. Cross-check: webhook event (1) -> Function invocation (2) -> table row (3) -> status API (4) should all agree. ==="
