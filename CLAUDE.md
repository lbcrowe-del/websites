# websites — marketing sites + ServerBridge licensing backend

This repo holds two marketing sites **and** the licensing backend for the **ServerBridge**
desktop app (which lives in the separate `lbcrowe-del/ServerBridge` repo, on-disk folder
`desktop`). Cross-repo changes between the two are common.

- **GitHub:** `lbcrowe-del/websites`  ·  **Owner:** Lee Crowe Software Solutions LLC

## Layout
- `server-bridge/` — the `server-bridge.com` site (HTML/CSS) **plus**:
  - `api/` — the licensing **Azure Functions** app (isolated worker, **.NET 10**).
  - `api-tests/` — xUnit integration tests for the API (run against Azurite; `Category=Integration`).
  - `legal-source/ServerBridge-EULA.md` — synced copy of the EULA source of truth (real source is
    in the ServerBridge repo), used to generate `eula.html`.
  - `eula.html` / `privacy.html` / `terms.html` / `refund.html` / `buy.html` / `download.html`.
- `leecrowesoftware/` — the `leecrowesoftware.com` company site.
- `scripts/` — `generate_eula.py`, `sync_eula_from_serverbridge.sh`.

## Azure topology (verified 2026-06-28 — get this right, it has bitten us)
- **Subscription/tenant:** the licensing infra lives in subscription **`4befc9c5-…`** under tenant
  **`e6f2d8ca-…`** (the `lbcrowegmail` directory). There are **two subscriptions both named
  "Azure subscription 1"**; the *other* one (`a59ecd1a-…`, tenant `a7560271-…`) **denies access** —
  `az functionapp list` there returns AuthorizationFailed. Always `az account set --subscription
  4befc9c5-1865-41cb-9b94-911ccb757a6c` first.
- **Function App:** `serverbridge-licensing`, resource group **`websites_rg`** (underscore),
  **Windows Consumption (Dynamic)** plan, **.NET 10 isolated**, `httpsOnly` on. Reachable at
  **`https://api.server-bridge.com/api/`** (preferred — see below) and at its origin hostname
  **`https://serverbridge-licensing.azurewebsites.net/api/`** (legacy, still load-bearing for
  shipped desktop clients — do **not** delete it). Functions: `LicenseActivate`,
  `LicenseStatus`, `MigrationComplete`, `StripeWebhook`, `StripeIssueLicense`.
- **Custom domain `api.server-bridge.com` (added 2026-09-11).** Cloudflare-proxied (orange cloud)
  CNAME → `serverbridge-licensing.azurewebsites.net`, plus an `asuid.api` TXT record holding the
  subscription's `customDomainVerificationId`. Azure holds a hostname binding with **no
  certificate** (`sslState=null`): **Y1 classic Consumption cannot hold one** (App Service managed
  certs require Basic+ or Flex Consumption), so **Cloudflare terminates TLS**. That forced two
  **Configuration Rules** on the `server-bridge.com` zone, both scoped to `api.server-bridge.com`
  only:
  1. **SSL → Full** — the zone is **Full (strict)**, which rejects the `*.azurewebsites.net`
     wildcard Azure serves on this hostname. Scoped so the marketing site keeps Full (strict).
  2. **Browser Integrity Check → Off** — BIC 403s legitimate API clients on browser-signature
     heuristics (`error code: 1010`, hit live during setup). An API must not sit behind browser
     checks; the failure mode is a silent 403 on the Stripe webhook.
  **Both rules are scaffolding, not architecture.** They exist solely because of Y1. When this app
  moves to **Flex Consumption** it gets a real free managed certificate (the mechanism is proven —
  see `api.gethalera.com`); at that point delete both rules and turn the proxy off. Verified
  2026-09-11: all five routes return byte-identical responses via both hostnames.
- **Never point a client at `server-bridge.com/api`** — the Free Static Web App reserves `/api` and
  405s every POST. This has bitten twice. Use the `api.` subdomain.
- **Static Web Apps** (both **Free** SKU, RG `websites_rg`): `serverbridge-site` serves
  `server-bridge.com` (default host `ashy-stone-0a8502f10.7.azurestaticapps.net`);
  `leecrowesoftware-site` serves `leecrowesoftware.com` (default host
  `lemon-pond-0912d7510.7.azurestaticapps.net`). Each has **both** apex and `www` registered as
  custom domains (www added 2026-07-04 — was previously NXDOMAIN).
- **DNS:** Azure DNS zones `server-bridge.com` and `leecrowesoftware.com`, RG `websites_rg`. Apex
  is an A-record → the SWA; `www` is a CNAME → the SWA default host (add www as an SWA custom
  domain first, then it auto-validates + issues the cert). `leecrowesoftware.com` also carries M365
  email records (MX/SPF/autodiscover), **DMARC** (`_dmarc` TXT, `p=none` monitoring, 2026-07-04),
  and **DKIM** `selectorN._domainkey` CNAMEs → `…w-v1.dkim.mail.microsoft` (see STATUS.md; DKIM
  enable pending M365 sync).
- **App installers are NOT in this repo's domain** — `download.html` fetches releases from the
  public `lbcrowe-del/ServerBridge-releases` repo (the app source repo is private). See the
  ServerBridge desktop repo's CLAUDE.md for the release-publishing topology.

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
copy of the real source in the ServerBridge repo). To change it: edit the `.md` in the ServerBridge repo,
then run `scripts/sync_eula_from_serverbridge.sh` (syncs + runs `generate_eula.py`). Never edit
`eula.html` by hand — CI enforces this. Legal review is via **Justee AI**, not an attorney.

## Build / test / run locally
**After any significant change to the API, run `scripts/preflight.sh` and report the result before
finishing the turn** — don't claim it works without running it. Use `--smoke` (with `LICENSE_KEY`
set) to also hit the live Function App after a deploy. This is a standing instruction for every
session. The script builds the API and runs the Azurite-backed integration tests (starting/stopping
the emulator itself).
```bash
scripts/preflight.sh                 # build + Azurite integration tests
scripts/preflight.sh --smoke         # also smoke-test the live Function App (needs LICENSE_KEY)

# Manual equivalents:
dotnet build server-bridge/api/ServerBridge.LicensingApi.csproj -c Release
azurite --silent --location /tmp/azurite &   # Table emulator on :10002
dotnet test server-bridge/api-tests/ServerBridge.LicensingApi.Tests.csproj --filter "Category=Integration"
# local Functions host: create server-bridge/api/local.settings.json with
#   AzureWebJobsStorage / LICENSE_TABLE_CONNECTION = "UseDevelopmentStorage=true"
```

## See also
- `STATUS.md` — current state, recent decisions, open items (read first each session).
- The ServerBridge repo's `CLAUDE.md` — desktop app + the canonical M365 tenant/signing details.
