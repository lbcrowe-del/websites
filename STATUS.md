# STATUS — websites repo

> Living state doc. **Read first each session; update at the end of any session that changes
> something here, then commit.** Architecture that's stable belongs in `CLAUDE.md`, not here.

_Last updated: 2026-06-28_

## Current state
- **Licensing API live on .NET 10.** `migration/complete` endpoint shipped and verified in prod
  (returns `{"Recorded":false}` for an unknown key). `httpsOnly` enabled. Stack-flip to v10.0
  automated in `deploy-licensing-api.yml`.
- **Azurite-backed integration tests** for the API run in CI (`test-licensing-api.yml`).
- **Privacy Policy + DPA** updated to disclose the migration-completion data category.

## Open items / decisions
- ⚠️ **Verify the Stripe webhook URL in the Stripe dashboard.** `server-bridge.com/api/webhooks/stripe`
  405s (Free SWA, no API backend); license issuance only works if Stripe points at
  `https://serverbridge-licensing.azurewebsites.net/api/webhooks/stripe`. The paid-tier rollout
  session verified the flow when the API was SWA-managed-functions; it has since moved to the
  standalone Function App (same `serverbridgelicenses` storage). A repointed Stripe endpoint issues
  a new signing secret that must match `STRIPE_WEBHOOK_SECRET` on the Function App. **Not yet confirmed/fixed.**
- **Branding deferred:** staying on the `azurewebsites.net` host for the API (no free TLS on
  Consumption). Revisit at launch (SWA Standard ~$9/mo would give `server-bridge.com/api` + free cert).
- **Legal pages** (`terms.html`/`privacy.html`/`refund.html`) still have date placeholders to fill
  after Justee review is finalized.
- **Deploy publishes** the API framework-dependent (RID dropped 2026-06-28); takes effect next API deploy.

## Recent decisions (don't re-litigate)
- Client + Stripe should target the `azurewebsites.net` host, not `server-bridge.com/api`.
- Everything targets .NET 10; the SP-based stack automation keeps runtime + package in sync.
