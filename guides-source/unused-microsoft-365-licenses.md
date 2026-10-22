---
title: "Find and clean up unused Microsoft 365 licenses (free PowerShell)"
meta_description: "Find Microsoft 365 licenses nobody uses, priced in dollars, with a free read-only PowerShell scan. What counts as unused, the traps, and safe cleanup."
target_keyword: "unused Microsoft 365 licenses"
secondary_keywords: ["Microsoft 365 license cleanup", "find unused Office 365 licenses PowerShell", "reclaim M365 licenses"]
proposed_url: "https://leecrowesoftware.com/guides/unused-microsoft-365-licenses"
status: "DRAFT – not published"
---

# How to find and clean up unused Microsoft 365 licenses

I've been a Microsoft 365 and Windows admin for over 20 years. The licence bill is the one thing that gets cleaned up once and then quietly grows again. People leave, contractors finish, a mailbox gets parked "for now". Each of those seats keeps billing every month.

This page covers what "unused" really means, the traps that make a quick check give the wrong answer, and a free read-only way to get a dollar figure for your own tenant.

## How common is this?

CoreView's 2020 study looked at 5 million enterprise workers. It found that on average **56% of a business's Office 365 licenses were inactive, underutilized, oversized or unassigned**. It also estimated that better managing *inactive* licenses alone could cut total Office 365 costs by about 14% ([BusinessWire, April 2020](https://www.businesswire.com/news/home/20200407005154/en/56-of-Businesses-Microsoft-Office-365-Licenses-are-Not-Fully-Exploited-CoreView-Research-Finds)). That is a vendor's own data from 2020, so treat it as a sign that the problem is common, not as a forecast for your tenant. Your number could be far lower.

## What counts as "unused"

There are two kinds of seat worth looking at first:

1. **Licensed accounts with no activity for a long time.** Microsoft's own guidance says "a reasonable window for inactive user accounts is between 90 and 180 days" ([Microsoft Learn](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/howto-manage-inactive-user-accounts)).
2. **Disabled accounts that still hold a paid license.** These are usually leavers. The sign-in was blocked but the license was never removed.

## The traps

- **Last sign-in "attempts" are not activity.** In Microsoft Graph, `lastSignInDateTime` records the last interactive sign-in *attempt*, including failures ([Microsoft Learn: signInActivity](https://learn.microsoft.com/en-us/graph/api/resources/signinactivity)). An account that is being password-sprayed looks freshly active. Microsoft says to use `lastSuccessfulSignInDateTime` "if you need to determine when the account was truly accessed."
- **Phone-only users.** Someone who only uses Outlook or Teams on a phone may have no recent *interactive* sign-in at all. Their activity shows up as non-interactive sign-ins (`lastNonInteractiveSignInDateTime`). If you only look at interactive sign-ins, you could take a license from someone who uses it every day.
- **Sign-in data needs Entra ID P1 or P2.** Reading `lastSuccessfulSignInDateTime` through Graph needs a Microsoft Entra ID P1 or P2 license ([Microsoft Learn](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/howto-manage-inactive-user-accounts)). Without it, Microsoft 365 usage reports are the fallback. They cover the last 7, 30, 90 and 180 days ([Microsoft Learn: activity reports](https://learn.microsoft.com/en-us/microsoft-365/admin/activity-reports/activity-reports)).
- **Hidden names in reports.** Usage reports can conceal user, group and site names. A Global Administrator can turn that off under **Settings → Org settings → Services → Reports** ([Microsoft Learn](https://learn.microsoft.com/en-us/microsoft-365/admin/activity-reports/activity-reports)). While names are hidden, a report can't tell you *who* is inactive.

## A free, read-only scan

I built a free PowerShell module that handles those traps and prints the waste in dollars:

```powershell
Install-Module ServerBridge.LicenseScan -Scope CurrentUser
Invoke-LicenseScan
```

What it does, from the [project README](https://github.com/lbcrowe-del/ServerBridge-LicenseScan):

- Reads your subscribed SKUs and who holds each one.
- With Entra ID P1/P2, it uses the most recent of `lastSuccessfulSignInDateTime` and `lastNonInteractiveSignInDateTime`, and deliberately not `lastSignInDateTime`. Without P1, it switches to usage reports automatically and tells you it did.
- Flags accounts with no activity for **90 days** (change it with `-InactiveDays`) and **disabled accounts that still hold a paid license**.
- Prices the flagged seats at **list-price estimates**. You can edit the price table to match your contract. It then saves a per-user CSV.
- Skips free SKUs, and skips guests unless you add `-IncludeGuests`.

**What it can't change:** anything. It is read-only. It asks only for delegated read permissions (`User.Read.All`, `Organization.Read.All`, `AuditLog.Read.All`, `Reports.Read.All`), never reads mail, files or chats, and sends nothing anywhere except the calls to Microsoft Graph. **Global Reader** is enough to run it. The first time anyone in your tenant runs it, a Global Administrator has to approve the app.

It is MIT-licensed and on the [PowerShell Gallery](https://www.powershellgallery.com/packages/ServerBridge.LicenseScan).

## Cleaning up safely

The scan gives you a list. What to do with it is your call. A careful order:

1. **Check the list with a human.** People on long leave, seasonal staff and service accounts all look inactive. The scan errs toward "active" on purpose, but you know your people.
2. **For leavers with a mailbox someone still needs, convert it to a shared mailbox before removing the license.** Microsoft notes the mailbox needs a license *while* you convert it. After that, a shared mailbox under 50 GB doesn't need one. Don't delete the user account, because the shared mailbox needs it as an anchor ([Microsoft Learn](https://learn.microsoft.com/en-us/microsoft-365/admin/email/convert-user-mailbox-to-shared-mailbox)).
3. **Remove the license** ([Microsoft Learn: remove licenses from users](https://learn.microsoft.com/en-us/microsoft-365/admin/manage/remove-licenses-from-users)). Know what that does first: Microsoft notes that once the license is removed, the user's mailbox is no longer searchable with eDiscovery tools such as Content Search. If you have legal-hold or retention needs, sort those out before you remove anything.
4. **Reduce the seat count on the subscription.** Unassigning a license doesn't by itself shrink the bill. Microsoft's advice is to remove licenses you don't plan to reassign from the subscription "so that you're not paying for more licenses than you need". Some subscriptions can only be reduced or cancelled in a limited window after purchase or renewal (same page), so check your renewal date.
5. **Re-run it after the next round of leavers.**

## If you need more than a list

The scan is free and stays free. If you need to hand the result to whoever approves the spend, the paid [ServerBridge License Auditor](https://server-bridge.com/license-auditor.html?ct=laseo_unused) turns it into a PDF report with recommended actions, costed by SKU. Team and above re-run the audit on a schedule. It starts at $149/year for up to 50 users. If your scan finds less waste than that, the scan will say so. Keep the money.

*Prices in the scan are list-price estimates, not your invoice. Your contract price may differ.*
