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
