---
title: "Microsoft 365 price increase July 2026: old vs new prices"
meta_description: "Microsoft 365 list prices rose July 1, 2026. Old and new prices for E3, E5, Business and Frontline, when you pay them, and checking unused seats."
target_keyword: "Microsoft 365 price increase July 2026"
secondary_keywords: ["Microsoft 365 E3 price 2026", "Business Standard price increase", "M365 renewal price increase"]
proposed_url: "https://leecrowesoftware.com/guides/microsoft-365-price-increase-july-2026"
status: "DRAFT – not published. Publish after free scan 1.6.0 ships with the July 2026 price table (issue #23), or remove the 'the scan uses these prices' line."
---

# The July 2026 Microsoft 365 price increase: what changed

On July 1, 2026, Microsoft raised list prices on several Microsoft 365 commercial suites. If your renewal is coming up, the price rise matters less than the question of how many seats you actually need.

## The new list prices (USD per user per month, suites with Teams)

From Microsoft's own [pricing and packaging update](https://www.microsoft.com/en-us/licensing/news/2026-m365-packaging-pricing-updates):

| Plan | Before July 1, 2026 | From July 1, 2026 | Change |
|---|---|---|---|
| Microsoft 365 Business Basic | $6.00 | $7.00 | 16% |
| Microsoft 365 Business Standard | $12.50 | $14.00 | 12% |
| Microsoft 365 Business Premium | $22.00 | $22.00 | none |
| Office 365 E1 | $10.00 | $10.00 | none |
| Office 365 E3 | $23.00 | $26.00 | 13% |
| Office 365 E5 | $38.00 | $41.00 | 8% |
| Microsoft 365 E3 | $36.00 | $39.00 | 8% |
| Microsoft 365 E5 | $57.00 | $60.00 | 5% |
| Microsoft 365 F1 | $2.25 | $3.00 | 33% |
| Microsoft 365 F3 | $8.00 | $10.00 | 25% |

Microsoft also lists versions without Teams, standalone components such as Entra ID P1 ($6 → $7) and EMS E3 ($10.60 → $12), and nonprofit and government pricing on the same page. These list prices "may vary by country and currency", and your agreement or reseller price may differ.

## When you actually pay it

Microsoft says **existing customers remain on current pricing until renewal**. New purchases pay the new price now. Existing subscriptions move to it at their next renewal.

The price change came with new features, which roll out by August 1, 2026. Microsoft 365 E3, for example, gains Defender for Office 365 Plan 1, Intune Plan 2, Intune Remote Help and Intune Advanced Analytics. The full list is on [Microsoft's page](https://www.microsoft.com/en-us/licensing/news/2026-m365-packaging-pricing-updates).

## Why unused seats matter more after a rise

An unused seat used to cost you the old price. After renewal, it costs you the new one. A simple illustration using Microsoft's list prices, not a figure from any real tenant:

- **10 unused Microsoft 365 E3 seats:** $4,320/year at the old $36, and $4,680/year at the new $39.
- **5 unused Business Standard seats:** $750/year at the old $12.50, and $840/year at the new $14.

So the price rise on unused seats is small. The real cost is the seats themselves. Renewal is when you can lower the seat count, so it's the time to check. Some subscriptions can only be reduced in a limited window after purchase or renewal ([Microsoft Learn](https://learn.microsoft.com/en-us/microsoft-365/admin/manage/remove-licenses-from-users)).

## Check your tenant before renewal

I wrote a free, read-only PowerShell scan that lists licensed accounts with no activity in 90 days and disabled accounts that still hold a paid license, then prices them:

```powershell
Install-Module ServerBridge.LicenseScan -Scope CurrentUser
Invoke-LicenseScan
```

- **Read-only:** delegated read permissions only. It never changes licenses, users or settings, and never reads mail, files or chats.
- **Global Reader** can run it. A Global Administrator approves the app once.
- **Prices are list-price estimates** taken from Microsoft's July 2026 list. If you haven't renewed yet, you are probably still paying the old price. Edit the price table to match your contract for exact figures.
- Source and docs are on [GitHub](https://github.com/lbcrowe-del/ServerBridge-LicenseScan). It is MIT-licensed.

How it decides whether someone is "inactive" (sign-in data with Entra ID P1/P2, usage reports without it) is covered in [How to find and clean up unused Microsoft 365 licenses](https://leecrowesoftware.com/guides/unused-microsoft-365-licenses).

## If someone has to sign off the renewal

The paid [ServerBridge License Auditor](https://server-bridge.com/license-auditor.html?ct=laseo_price2026) turns the same findings into a PDF with recommended actions, costed by SKU, for whoever approves the spend. It starts at $149/year for up to 50 users. If the free scan finds less waste than that, it tells you the paid tool wouldn't pay for itself.
