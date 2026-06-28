# websites — marketing sites + ServerBridge licensing backend

This repo holds two marketing sites **and** the licensing backend for the **ServerBridge**
desktop app (which lives in the separate `lbcrowe-del/ServerBridge` repo, on-disk folder
`MigratePro`). Cross-repo changes between the two are common.

- **GitHub:** `lbcrowe-del/websites`  ·  **Owner:** Lee Crowe Software Solutions LLC

## Layout
- `server-bridge/` — the `server-bridge.com` site (HTML/CSS) **plus**:
  - `api/` — the licensing **Azure Functions** app (isolated worker, **.NET 10**).
  - `api-tests/` — xUnit integration tests for the API (run against Azurite; `Category=Integration`).
  - `legal-source/ServerBridge-EULA.md` — synced copy of the EULA source of truth (real source is
    in the `MigratePro` repo), used to generate `eula.html`.
  - `eula.html` / `privacy.html` / `terms.html` / `refund.html` / `buy.html` / `download.html`.
- `leecrowesoftware/` — the `leecrowesoftware.com` company site.
- `scripts/` — `generate_eula.py`, `sync_eula_from_migratepro.sh`.

## Azure topology (verified 2026-06-28 — get this right, it has bitten us)
- **Subscription/tenant:** the licensing infra lives in subscription **`4befc9c5-…`** under tenant
  **`e6f2d8ca-…`** (the `lbcrowegmail` directory). There are **two subscriptions both named
  "Azure subscription 1"**; the *other* one (`a59ecd1a-…`, tenant `a7560271-…`) **denies access** —
  `az functionapp list` there returns AuthorizationFailed. Always `az account set --subscription
  4befc9c5-1865-41cb-9b94-911ccb757a6c` first.
- **Function App:** `serverbridge-licensing`, resource group **`websites_rg`** (underscore),
  **Windows Consumption (Dynamic)** plan, **.NET 10 isolated**, `httpsOnly` on. Reachable only at
  **`https://serverbridge-licensing.azurewebsites.net/api/`**. Functions: `LicenseActivate`,
  `LicenseStatus`, `MigrationComplete`, `StripeWebhook`, `StripeIssueLicense`.
- **Static Web Apps** (both **Free** SKU, RG `websites_rg`): `serverbridge-site` serves
  `server-bridge.com`; `leecrowesoftware-site` serves `leecrowesoftware.com`.
- **DNS:** Azure DNS zones `server-bridge.com` and `leecrowesoftware.com`, RG `websites_rg`. The
  apex A-record for `server-bridge.com` points at the Static Web App.

## ⚠️ `server-bridge.com/api` does NOT reach the Function App
**History (reconciled 2026-06-28):** the licensing API originally ran as **SWA managed functions**
at `server-bridge.com/api` (the paid-tier rollout session set `STRIPE_WEBHOOK_SECRET` /
`LICENSE_TABLE_CONNECTION` on the SWA and verified an end-to-end purchase there). It has since moved
to the **standalone `serverbridge-licensing` Function App** — the SWA workflow now has
`app_location: "server-bridge"` but **no `api_location`**, so the SWA no longer serves `/api`. The
standalone app uses the **same storage account `serverbridgelicenses`**, so the licenses table is the
same one. The SWA still has the three licensing app settings, but they are **inert leftovers**.

The marketing domain is served by the **Free** Static Web App, which only allows GET/HEAD/OPTIONS
and **405s every POST**. Free SWA **cannot** link an external API backend (needs Standard SKU). So:
- The desktop client's `ApiBaseUrl` must be the **`azurewebsites.net`** host (it is, as of
  2026-06-28 — was previously pointed at `server-bridge.com/api` and 405'd on everything).
- **The Stripe webhook must also target the `azurewebsites.net` host.** If it's set to
  `server-bridge.com/api/webhooks/stripe` it 405s and **license issuance silently fails** — verify
  the URL in the Stripe dashboard. A repointed/new Stripe endpoint issues a **new signing secret**
  that must match `STRIPE_WEBHOOK_SECRET` on the `serverbridge-licensing` Function App.
- A branded `api.server-bridge.com` would need a TLS cert (no free managed cert on Consumption) or
  an SWA Standard upgrade (~$9/mo). Decision so far: stay on `azurewebsites.net`.

## CI / deploy workflows
- `deploy-licensing-api.yml` — on push to `main` under `server-bridge/api/**` (or manual). Builds,
  then **`azure/login` (SP `sb-licensing-deploy`, secret `AZURE_CREDENTIALS`, Contributor on
  `websites_rg`) → `az functionapp config set --net-framework-version v10.0` → publish.** The
  stack-flip is in the same run as the publish so the runtime stack and package never drift.
- `test-licensing-api.yml` — starts Azurite and runs the `api-tests` integration tests on
  `server-bridge/api/**` or `server-bridge/api-tests/**` changes.
- `check-eula-sync.yml` — fails the build if `eula.html` drifts from the generator output.
- `azure-static-web-apps-*.yml` — deploy the two marketing sites.

## EULA generation (do not hand-edit eula.html)
`eula.html` is **generated** from `server-bridge/legal-source/ServerBridge-EULA.md` (itself a synced
copy of the real source in the `MigratePro` repo). To change it: edit the `.md` in `MigratePro`,
then run `scripts/sync_eula_from_migratepro.sh` (syncs + runs `generate_eula.py`). Never edit
`eula.html` by hand — CI enforces this. Legal review is via **Justee AI**, not an attorney.

## Build / test / run locally
```bash
dotnet build server-bridge/api/ServerBridge.LicensingApi.csproj -c Release
# integration tests need the Azure Storage emulator (Azurite) on :10002:
azurite --silent --location /tmp/azurite &
dotnet test server-bridge/api-tests/ServerBridge.LicensingApi.Tests.csproj --filter "Category=Integration"
# local Functions host: create server-bridge/api/local.settings.json with
#   AzureWebJobsStorage / LICENSE_TABLE_CONNECTION = "UseDevelopmentStorage=true"
# Post-deploy smoke test: server-bridge/api/scripts/smoke-migration-complete.sh
```

## See also
- `STATUS.md` — current state, recent decisions, open items (read first each session).
- `MigratePro` repo's `CLAUDE.md` — desktop app + the canonical M365 tenant/signing details.
