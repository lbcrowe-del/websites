---
title: "Find inactive users in Microsoft 365 and what leavers still hold"
meta_description: "Find inactive and disabled Microsoft 365 accounts and the licenses, groups and mailboxes they still hold. Free read-only PowerShell check."
target_keyword: "find inactive users Microsoft 365"
secondary_keywords: ["Microsoft 365 inactive users report", "offboarding checklist Microsoft 365", "disabled users with licenses"]
proposed_url: "https://leecrowesoftware.com/guides/find-inactive-users-microsoft-365"
status: "DRAFT – not published"
---

# Find inactive users in Microsoft 365, and what the leavers still hold

When someone leaves, the license is usually the only thing anyone remembers to remove. The account keeps its group memberships, and its mailbox is never converted to shared. Then it sits there until an audit finds it, or until someone signs in with it.

This page covers how to find those accounts and what to check on each one.

## Option 1: the Entra admin center (one-off, manual)

Microsoft documents a manual way to do this ([Microsoft Learn](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/howto-manage-inactive-user-accounts)):

1. Sign in to the Microsoft Entra admin center as at least a **Reports Reader**.
2. Go to **Entra ID → Users → Manage view → Edit columns**, and add **Last interactive sign-in time**.
3. Filter on that column for a date range.

It's fine for a quick look, but there are limits:

- It shows **interactive** sign-ins. People who only use Outlook or Teams on a phone sign in non-interactively, so they can look inactive when they aren't.
- Sign-in activity in Graph needs **Microsoft Entra ID P1 or P2** (same page).
- It tells you *who*, but not what they still hold: licenses, groups, mailbox type.

Microsoft's own guidance on the window: "In many organizations, a reasonable window for inactive user accounts is between 90 and 180 days." Leave, sabbaticals and seasonal work all look like inactivity.

## Option 2: a free read-only check that shows what each leaver still holds

I built a free PowerShell check for exactly this:

```powershell
Install-Module ServerBridge.LicenseScan -Scope CurrentUser
Invoke-OffboardingCheck
```

It finds accounts that **look like leavers**, meaning disabled or with no activity for 90 days (`-InactiveDays` changes it). It prints one row per person ([README](https://github.com/lbcrowe-del/ServerBridge-LicenseScan)):

| Column | What it tells you |
|---|---|
| `Reason` | `disabled`, or `inactive` |
| `Licenses` | paid licenses still assigned (free SKUs aren't counted) |
| `GroupCount` | how many groups and directory roles it still belongs to |
| `MailboxType` | `user` means nobody converted it to a shared mailbox |
| `AnnualCost` | what that account's paid licenses cost per year, at list-price estimates |

The full list goes to a CSV. It's read-only: delegated read permissions only, nothing in the tenant changes, and nothing leaves your machine except the calls to Microsoft Graph. **Global Reader** is enough to run it.

**Two honest limits**, both in the README:

- Mailbox type comes from Microsoft's mailbox usage report. A mailbox nobody has used can take a long time to appear there. In our own testing, one only showed up at around eighteen days old. Until then the check says `unknown` rather than guessing. To read Exchange directly, use `Invoke-OffboardingCheck -UseExchangeOnline`. It needs a second sign-in and the `ExchangeOnlineManagement` module.
- If your tenant conceals user names in reports, only disabled accounts can be checked. A Global Administrator can turn that setting off under **Settings → Org settings → Services → Reports** ([Microsoft Learn](https://learn.microsoft.com/en-us/microsoft-365/admin/activity-reports/activity-reports)).

## What to do with each leaver

Microsoft's [former-employee checklist](https://learn.microsoft.com/en-us/microsoft-365/admin/add-users/remove-former-employee) runs in this order:

1. **Block sign-in.**
2. **Save the mailbox contents** if someone needs them.
3. **Wipe and block their mobile device.**
4. **Forward their email or convert the mailbox to shared.** The mailbox needs a license *while* you convert it. Afterwards, a shared mailbox under 50 GB needs no license. Keep the user account, because the shared mailbox is anchored to it ([Microsoft Learn](https://learn.microsoft.com/en-us/microsoft-365/admin/email/convert-user-mailbox-to-shared-mailbox)).
5. **Give someone access to their OneDrive and Outlook data.**
6. **Remove the license.** Microsoft says that after you remove it, email, contacts and calendar are kept for 30 days and then deleted permanently.
7. **Delete the account** once you no longer need it.

Group memberships and directory roles aren't a billing problem, but they are an access problem. Clear them as part of step 1 or 7.

## Doing this every month without thinking about it

The free check is a snapshot you run when you remember to. The paid [ServerBridge License Auditor](https://server-bridge.com/license-auditor.html?ct=laseo_inactive) Team plan ($349/year, 51–300 users) re-runs the audit on a schedule, shows what changed since the last run, and includes the offboarding list. Starter is $149/year for up to 50 users. If the free check finds nothing worth paying for, it says so.
