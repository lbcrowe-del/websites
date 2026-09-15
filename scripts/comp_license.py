#!/usr/bin/env python3
"""Issue, list and deactivate complimentary ("comp") license keys.

Comp keys go straight into the Licenses table: no Stripe payment, no welcome email, no Brevo contact.
They use the same key format and fields as a Stripe purchase, and CustomerName always starts with
"COMP - " so they're easy to find and never mistaken for sales. Every change is checked against the
live license/status API.

Setup (once):
    python3 -m venv .venv-comp && .venv-comp/bin/pip install azure-data-tables
    az login   # an account that can read the licensing storage account

Usage:
    .venv-comp/bin/python scripts/comp_license.py issue --tier Team --for "Jane D, r/PowerShell feedback"
    .venv-comp/bin/python scripts/comp_license.py issue --tier Pro --product ServerBridge --for "Beta tester"
    .venv-comp/bin/python scripts/comp_license.py list            # active comp keys
    .venv-comp/bin/python scripts/comp_license.py list --all      # include deactivated ones
    .venv-comp/bin/python scripts/comp_license.py deactivate SB-XXXXX-XXXXX-XXXXX-XXXXX

The connection string comes from LICENSE_TABLE_CONNECTION if set, otherwise from the Azure CLI
(subscription 4befc9c5-..., storage account serverbridgelicenses). The printed key is the only copy
you get; the table stores it as the row key.
"""
import argparse
import datetime
import json
import os
import secrets
import subprocess
import sys
import urllib.request

SUBSCRIPTION = "4befc9c5-1865-41cb-9b94-911ccb757a6c"
RESOURCE_GROUP = "websites_rg"
STORAGE_ACCOUNT = "serverbridgelicenses"
TABLE = "Licenses"
STATUS_URL = "https://api.server-bridge.com/api/license/status"
ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"  # same as LicenseKeyGenerator.cs (no 0/1/I/O)
TIERS = {"LicenseAuditor": ["Starter", "Team", "Enterprise"], "ServerBridge": ["Pro"]}
COMP_PREFIX = "COMP - "


def fail(message):
    print(f"Error: {message}", file=sys.stderr)
    sys.exit(1)


def table_client():
    try:
        from azure.data.tables import TableClient
    except ImportError:
        fail("the azure-data-tables package isn't installed. Run: python3 -m venv .venv-comp && "
             ".venv-comp/bin/pip install azure-data-tables")
    conn = os.environ.get("LICENSE_TABLE_CONNECTION")
    if not conn:
        result = subprocess.run(
            ["az", "storage", "account", "show-connection-string", "--subscription", SUBSCRIPTION,
             "--resource-group", RESOURCE_GROUP, "--name", STORAGE_ACCOUNT, "--query", "connectionString", "-o", "tsv"],
            capture_output=True, text=True)
        conn = result.stdout.strip()
        if result.returncode != 0 or not conn:
            fail("couldn't get the storage connection string from the Azure CLI. Run 'az login' first, "
                 "or set LICENSE_TABLE_CONNECTION.")
    return TableClient.from_connection_string(conn, table_name=TABLE)


def new_key():
    return "SB-" + "-".join("".join(secrets.choice(ALPHABET) for _ in range(5)) for _ in range(4))


def check_status(key, product):
    body = json.dumps({"LicenseKey": key, "Product": product}).encode()
    request = urllib.request.Request(STATUS_URL, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=20) as response:
        return json.load(response)


def mask(key):
    return key[:3] + "…" + key[-5:]


def cmd_issue(args):
    if args.tier not in TIERS[args.product]:
        fail(f"{args.product} tiers are: {', '.join(TIERS[args.product])}")
    if args.days is not None and args.days < 1:
        fail("--days must be 1 or more")
    days = args.days if args.days is not None else (365 if args.product == "LicenseAuditor" else None)

    key = new_key()
    entity = {
        "PartitionKey": "license",
        "RowKey": key,
        "Tier": args.tier,
        "Product": args.product,
        "Active": True,
        "CustomerName": COMP_PREFIX + args.recipient,
        "MigrationsCompletedCount": 0,
    }
    if days is not None:
        entity["ExpiresAtUtc"] = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=days)).replace(microsecond=0)
    if args.email:
        entity["CustomerEmail"] = args.email

    table_client().create_entity(entity)
    status = check_status(key, args.product)
    if not status.get("Valid") or status.get("Tier") != args.tier:
        fail(f"the key was written but the licensing API doesn't accept it yet: {status}. "
             f"Deactivate it with: comp_license.py deactivate {key}")

    expires = entity.get("ExpiresAtUtc")
    print(f"Issued {args.product} {args.tier} key for: {args.recipient}")
    print(f"  Key:     {key}")
    print(f"  Expires: {expires.date().isoformat() if expires else 'never'}")
    print("  Checked: the licensing API says it's valid.")
    if args.product == "LicenseAuditor":
        print(f"  Run:     licenseauditor --report audit.pdf --license-key {key}")
        print("  Download: https://github.com/lbcrowe-del/ServerBridge-LicenseAuditor-releases/releases/latest")


def cmd_list(args):
    rows = [r for r in table_client().query_entities("PartitionKey eq 'license'")
            if str(r.get("CustomerName") or "").startswith(COMP_PREFIX) and (args.all or r.get("Active"))]
    if not rows:
        print("No comp keys found." if args.all else "No active comp keys. Use --all to include deactivated ones.")
        return
    rows.sort(key=lambda r: r.metadata.get("timestamp") or datetime.datetime.min.replace(tzinfo=datetime.timezone.utc))
    for r in rows:
        expires = r.get("ExpiresAtUtc")
        print(f"{mask(r['RowKey'])}  {r.get('Product', 'ServerBridge'):<14} {r.get('Tier', ''):<10} "
              f"{'active  ' if r.get('Active') else 'inactive'}  expires {expires.date().isoformat() if expires else 'never':<10}  "
              f"{r['CustomerName'][len(COMP_PREFIX):]}")


def cmd_deactivate(args):
    table = table_client()
    try:
        entity = table.get_entity("license", args.key)
    except Exception:
        fail("no license with that key.")
    if not str(entity.get("CustomerName") or "").startswith(COMP_PREFIX):
        fail("that key isn't a comp key. Paid keys are deactivated by a refund or chargeback, not this script.")
    from azure.data.tables import UpdateMode
    table.update_entity({"PartitionKey": "license", "RowKey": args.key, "Active": False}, mode=UpdateMode.MERGE)
    status = check_status(args.key, entity.get("Product", "ServerBridge"))
    if status.get("Valid"):
        fail(f"the row is marked inactive but the licensing API still accepts the key: {status}")
    print(f"Deactivated {mask(args.key)} ({entity['CustomerName'][len(COMP_PREFIX):]}). "
          "The licensing API now rejects it.")


def main():
    parser = argparse.ArgumentParser(description="Issue, list and deactivate comp license keys.")
    sub = parser.add_subparsers(dest="command", required=True)

    issue = sub.add_parser("issue", help="create a comp key")
    issue.add_argument("--tier", required=True, help="Starter, Team or Enterprise (License Auditor); Pro (ServerBridge)")
    issue.add_argument("--for", dest="recipient", required=True, help="who it's for and why, e.g. 'Jane D, r/PowerShell feedback'")
    issue.add_argument("--product", default="LicenseAuditor", choices=sorted(TIERS))
    issue.add_argument("--days", type=int, help="days until it expires (License Auditor default 365; ServerBridge never)")
    issue.add_argument("--email", help="optional contact email, stored on the row (no email is sent)")
    issue.set_defaults(func=cmd_issue)

    listing = sub.add_parser("list", help="show comp keys (keys are masked)")
    listing.add_argument("--all", action="store_true", help="include deactivated keys")
    listing.set_defaults(func=cmd_list)

    deactivate = sub.add_parser("deactivate", help="turn off a comp key")
    deactivate.add_argument("key")
    deactivate.set_defaults(func=cmd_deactivate)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
