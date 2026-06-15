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
