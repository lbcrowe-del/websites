# Lee Crowe Software — websites

Static marketing sites, deployed via **Azure Static Web Apps**. No build step — plain HTML/CSS.

| Folder | Site | Domain |
| ------ | ---- | ------ |
| [`leecrowesoftware/`](leecrowesoftware/) | Parent company site (ServerBridge pinned) | leecrowesoftware.com |
| [`server-bridge/`](server-bridge/) | ServerBridge product "coming soon" page | server-bridge.com |

## Preview locally

```bash
# from this folder
python3 -m http.server 8731 --directory server-bridge      # http://localhost:8731
python3 -m http.server 8732 --directory leecrowesoftware    # http://localhost:8732
```

## Deploy — Azure Static Web Apps

Two domains → two Static Web App resources, both pointing at this one repo (different subfolders).
For **each** site, in the Azure portal → **Create a resource → Static Web App**:

1. **Plan type:** Free.
2. **Source:** GitHub → authorize → pick this repo, branch `main`.
3. **Build details:** Build preset **Custom**, and set:
   - ServerBridge → **App location** `/server-bridge`
   - Lee Crowe Software → **App location** `/leecrowesoftware`
   - **Api location** and **Output location**: leave blank.
4. **Create.** Azure commits a deploy workflow to `.github/workflows/` and publishes to a
   `*.azurestaticapps.net` URL within a couple of minutes. Every push to `main` redeploys.

### Custom domains + DNS

In each Static Web App → **Custom domains → Add**:
- Add the domain (`server-bridge.com` / `leecrowesoftware.com`).
- Azure shows a **TXT** record (validation) and a target hostname. At your DNS registrar:
  - **Subdomain (e.g. www):** add a **CNAME** to the SWA's `*.azurestaticapps.net` hostname.
  - **Apex/root (`server-bridge.com`):** use your DNS provider's **ALIAS/ANAME** (or Azure DNS
    alias record) pointing to the SWA hostname — apex domains can't use a plain CNAME. Add the
    TXT validation record too.
- Click **Validate**. Azure provisions a free TLS cert automatically.

> Tip: hosting the DNS zones in **Azure DNS** makes apex domains easiest (native alias records).

## Licensing API (`server-bridge/api/`)

An Azure Functions app (.NET 8, isolated worker) co-located with the `server-bridge.com` Static
Web App via the deploy workflow's `api_location: "server-bridge/api"`. No separate Azure resource
or cost — it deploys as part of the same SWA Free-tier resource.

Backs the ServerBridge desktop app's license activation/status checks (`ILicenseService` in
`ServerBridge.Core`), and supports **two payment processors side by side** so they can be compared
before picking one:

- **Stripe** — self-issued license keys (`SB-XXXXX-XXXXX-XXXXX-XXXXX`), stored in Azure Table
  Storage. `StripeWebhookFunction` listens for `checkout.session.completed` /
  `customer.subscription.updated` / `customer.subscription.deleted` and issues/activates/
  deactivates keys accordingly. `StripeIssueLicenseFunction` backs the Payment Link's
  `success_url` so the customer sees their key right after paying.
- **Lemon Squeezy** — uses Lemon Squeezy's own hosted **License Keys API** (no local storage);
  it issues, emails, and validates the key itself, and is the merchant of record for sales tax.
  `LemonSqueezyWebhookFunction` just verifies signatures for visibility — there's no license state
  to sync.

Both providers return the same JSON shape from `/api/license/activate` and `/api/license/status`:
`{ valid, tier, provider, expiresAtUtc, message }`.

### Required configuration (Function App → Configuration → Application settings)

| Setting | Used for |
| ------- | -------- |
| `AzureWebJobsStorage` | Functions runtime storage (also used for the license Table by default) |
| `LICENSE_TABLE_CONNECTION` | Optional — separate storage account for the `Licenses` table, if not reusing `AzureWebJobsStorage` |
| `STRIPE_WEBHOOK_SECRET` | Verifying `Stripe-Signature` on incoming webhooks |
| `LEMONSQUEEZY_WEBHOOK_SECRET` | Verifying `X-Signature` on incoming Lemon Squeezy webhooks |

Copy `server-bridge/api/local.settings.json.example` to `local.settings.json` (git-ignored) for
local development with the Azure Functions Core Tools.

### Dashboard setup

- **Stripe**: create a Payment Link for the subscription product, set its **after payment**
  redirect to `https://server-bridge.com/api/license/issue?session_id={CHECKOUT_SESSION_ID}`.
  Add a webhook endpoint at `https://server-bridge.com/api/webhooks/stripe` subscribed to
  `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`.
- **Lemon Squeezy**: enable **License Keys** on the product (Product → License keys). Add a
  webhook endpoint at `https://server-bridge.com/api/webhooks/lemonsqueezy` (any events — used
  only for visibility, not required for licensing to function).

### Local preview

```bash
cd server-bridge/api
func start   # requires Azure Functions Core Tools + local.settings.json
```
