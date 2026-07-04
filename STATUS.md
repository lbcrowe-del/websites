# STATUS — websites repo

> Living state doc. **Read first each session; update at the end of any session that changes
> something here, then commit.** Architecture that's stable belongs in `CLAUDE.md`, not here.

_Last updated: 2026-07-04_

## Current state
- 🚀 **ServerBridge v1.0.1 shipped (2026-07-04).** `download.html` now fetches releases from the
  **public `lbcrowe-del/ServerBridge-releases` repo** (the app source repo is private, so its
  releases API 404s anonymously — customers couldn't download). Verified: the live download page
  loads and pulls v1.0.1, and installers are anonymously downloadable. Don't repoint the fetch at
  the private `lbcrowe-del/ServerBridge` repo.
- **DNS hardening (2026-07-04):**
  - `www` added for **both** `server-bridge.com` and `leecrowesoftware.com` (previously NXDOMAIN):
    CNAME → the site's SWA default hostname + registered as an SWA custom domain; both certs
    `Ready`, both serve over HTTPS.
  - **DMARC** added for `leecrowesoftware.com` (`_dmarc` TXT = `v=DMARC1; p=none;
    rua=mailto:hello@leecrowesoftware.com`) — monitoring mode, safe. MX/SPF/autodiscover were
    already correct. Tighten to `p=quarantine` after DKIM is on and reports look clean.
  - **DKIM** for `leecrowesoftware.com`: `New-DkimSigningConfig` created (Exchange Online), and
    the two `selectorN._domainkey` CNAMEs published pointing at the **newer M365 target**
    `selectorN-leecrowesoftware-com._domainkey.LeeCroweSoftware.w-v1.dkim.mail.microsoft` (NOT the
    classic `._domainkey.<tenant>.onmicrosoft.com` pattern — M365 moved to `w-v1.dkim.mail.microsoft`;
    always create the config first via `New-DkimSigningConfig`, which prints the exact target).
    ⏳ Still `Enabled:False / Status:CnameMissing` — waiting on M365 validator to see the CNAMEs;
    finish with `Set-DkimSigningConfig -Identity leecrowesoftware.com -Enabled $true` once synced.
- **Licensing API live on .NET 10.** `migration/complete` endpoint shipped and verified in prod
  (returns `{"Recorded":false}` for an unknown key). `httpsOnly` enabled. Stack-flip to v10.0
  automated in `deploy-licensing-api.yml`.
- **Azurite-backed integration tests** for the API run in CI (`test-licensing-api.yml`).
- **Privacy Policy + DPA** updated to disclose the migration-completion data category.
- **Favicon + apple-touch-icon added**, wired into every page (PR #5).
- **Stripe payment pipeline fully verified and hardened (2026-06-29).** Webhook host fix
  confirmed end-to-end on a real $99 live purchase (`scripts/verify-first-sale.sh`: webhook
  delivered, license issued, `license/status` returned `Valid: true`). While verifying it, found
  and closed a real gap: refunds/chargebacks never deactivated the issued license.
  `StripeWebhookFunction` now also handles `charge.refunded` (full refunds only — partial
  refunds are logged, not deactivated) and `charge.dispute.created`, both via a shared
  `DeactivateLicenseForPaymentIntentAsync` helper keyed off the Stripe `payment_intent` (linked
  to the license at issuance time). Live webhook destination updated to listen for both new
  event types (and the two stale, unused `customer.subscription.*` events from the old
  subscription-model drafts were removed). Deployed and confirmed live. The one test license
  predating this fix (`SB-84LB4-D4WL9-W33XP-XETFJ`) was manually corrected to `Active: false` —
  note for future table edits: `az storage entity merge` writes bare `key=value` pairs as
  strings, not typed booleans, which 500'd the status API until fixed via the
  `azure-data-tables` Python SDK; use that SDK (or the C# typed model), not the Azure CLI, for
  any future direct edits to boolean/typed fields in this table.
- **New Stripe product (2026-06-29):** old product/price archived; new one uses lookup key
  `serverbridge_pro_onetime`. Same $99 + tax. `buy.html` updated to the new Payment Link.

## Open items / decisions
- **Branding deferred:** staying on the `azurewebsites.net` host for the API (no free TLS on
  Consumption). Revisit at launch (SWA Standard ~$9/mo would give `server-bridge.com/api` + free cert).
- **Deploy publishes** the API framework-dependent (RID dropped 2026-06-28); takes effect next API deploy.

## Recent decisions (don't re-litigate)
- Client + Stripe should target the `azurewebsites.net` host, not `server-bridge.com/api`.
- Everything targets .NET 10; the SP-based stack automation keeps runtime + package in sync.
