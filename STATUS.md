# STATUS — websites repo

> Living state doc. **Read first each session; update at the end of any session that changes
> something here, then commit.** Architecture that's stable belongs in `CLAUDE.md`, not here.

_Last updated: 2026-06-29_

## Current state
- **Licensing API live on .NET 10.** `migration/complete` endpoint shipped and verified in prod
  (returns `{"Recorded":false}` for an unknown key). `httpsOnly` enabled. Stack-flip to v10.0
  automated in `deploy-licensing-api.yml`.
- **Azurite-backed integration tests** for the API run in CI (`test-licensing-api.yml`).
- **Privacy Policy + DPA** updated to disclose the migration-completion data category.
- **Favicon + apple-touch-icon added**, wired into every page (PR #5).
- ✅ **Stripe webhook verified end-to-end in production (2026-06-29).** Lee made a real $99 live
  purchase (`cs_live_a1jWl0...`, $104.94 with tax) specifically to test the webhook fix —
  `scripts/verify-first-sale.sh` confirmed all 4 checks: webhook delivered
  (`pending_webhooks: 0`), license `SB-84LB4-D4WL9-W33XP-XETFJ` issued in the `Licenses` table
  (`Tier: Pro`, matched Stripe customer ID), and `license/status` returned `Valid: true, Tier:
  Pro`. App Insights showed no request yet at check time — likely ingestion lag, not a failure,
  given the other 3 checks independently confirmed the request was processed. Lee then
  initiated a refund through the Stripe dashboard to close out the test purchase (refund itself
  not yet independently re-verified — would also exercise the refund-policy code path if/when
  checked).
- **New Stripe product (2026-06-29):** old product/price archived; new one uses lookup key
  `serverbridge_pro_onetime`. Same $99 + tax. `buy.html` updated to the new Payment Link.

## Open items / decisions
- **Branding deferred:** staying on the `azurewebsites.net` host for the API (no free TLS on
  Consumption). Revisit at launch (SWA Standard ~$9/mo would give `server-bridge.com/api` + free cert).
- ✅ **Legal pages dated (2026-06-27).** `terms.html`/`privacy.html`/`refund.html` all carry a
  live "Last updated" date; no placeholders remain. Lee is treating Justee AI's review as the
  final legal review for these pages (not an attorney engagement).
- **Deploy publishes** the API framework-dependent (RID dropped 2026-06-28); takes effect next API deploy.

## Recent decisions (don't re-litigate)
- Client + Stripe should target the `azurewebsites.net` host, not `server-bridge.com/api`.
- Everything targets .NET 10; the SP-based stack automation keeps runtime + package in sync.
