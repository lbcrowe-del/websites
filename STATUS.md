# STATUS — websites repo

> Living state doc. **Read first each session; update at the end of any session that changes
> something here, then commit.** Architecture that's stable belongs in `CLAUDE.md`, not here.

_Last updated: 2026-07-04 (one correction 2026-09-17)_

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

## Licence data retention (deployed 2026-09-21)
`LicenseAnonymisation` timer function (03:20 UTC daily) erases refunded customers' contact details
120 days after deactivation — name, email and Brevo contact go; the licence key and the Stripe
payment linkage stay, so refund and dispute history still works. Deployed to
`serverbridge-licensing-fc` only.

**Verified in production, not just deployed:** a marked test row (`SB-ANON-TEST-…`, deactivated
200 days earlier) was seeded, the timer was triggered by hand, and the row came back with name and
email empty, `AnonymisedUtc` stamped, key and Stripe id intact. Test row deleted afterwards; a
count-only audit confirmed no other row was touched (11 rows, 2 active, 0 anonymised).

**Worth knowing:** rows that were deactivated before `DeactivatedUtc` existed have their clock
started at the pass that first sees them (deliberate — guessing a date risks erasing early), so the
legacy comp/refunded rows become eligible around **mid-January 2027**, not 120 days after they were
actually deactivated.

## Open items / decisions
- ⏰ **DMARC follow-up — revisit on/after ~2026-07-25** (≥3 weeks of `p=none` report data).
  `leecrowesoftware.com` DMARC is currently `p=none` (monitoring). Before tightening: (1) confirm
  DKIM is enabled and passing, (2) review the `rua` aggregate reports sent to
  `hello@leecrowesoftware.com` to confirm every legitimate sender passes SPF and/or aligned DKIM.
  Then tighten to `p=quarantine` — ideally ramping `pct=25 → 50 → 100` — and eventually `p=reject`.
  Do NOT jump straight to quarantine/reject: premature tightening silently spam-folders legit mail
  (e.g. support replies from `hello@`), which is invisible and hurts deliverability.
- ⏳ **Finish DKIM enable** for `leecrowesoftware.com` — config + CNAMEs are in place (see Current
  state); once M365 syncs the CNAMEs, run `Set-DkimSigningConfig -Identity leecrowesoftware.com
  -Enabled $true`.
- ~~Branding deferred~~ **Superseded 2026-09-11:** the API is live at `api.server-bridge.com` (see CLAUDE.md
  Azure topology). The `azurewebsites.net` host stays up for desktop builds ≤1.1.0.
- **Deploy publishes** the API framework-dependent (RID dropped 2026-06-28); takes effect next API deploy.

## Recent decisions (don't re-litigate)
- Client + Stripe should never target `server-bridge.com/api`. New clients use `api.server-bridge.com` (2026-09-11).
- Everything targets .NET 10; the SP-based stack automation keeps runtime + package in sync.
