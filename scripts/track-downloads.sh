#!/usr/bin/env bash
#
# track-downloads.sh — poll GitHub release download counts for ServerBridge and
# report the delta since the last run.
#
# Downloads are the authoritative "how many people grabbed the installer" metric.
# GitHub exposes a per-asset `download_count` on every release; this script snapshots
# those totals to a local history file each run and prints what changed since last time,
# so you can see download *velocity*, not just a running total.
#
# Usage:
#   ./track-downloads.sh              # print current totals + delta since last snapshot, then save a snapshot
#   ./track-downloads.sh --no-save    # print only; don't write a snapshot (safe to run ad hoc)
#   ./track-downloads.sh --history    # dump the full snapshot history and exit
#
# Data file: websites/scripts/.download-history.jsonl  (one JSON snapshot per line)
# Notes:
#   - Counts every fetch, including your own test downloads and CI. It's downloads, not unique users.
#   - Velopack update metadata (*.nupkg, RELEASES*) is excluded from the "installer" total.
#   - Public repo, so no auth needed. Set GITHUB_TOKEN to raise the rate limit if you hit 403s.

set -euo pipefail

REPO="lbcrowe-del/ServerBridge-releases"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HISTORY_FILE="$SCRIPT_DIR/.download-history.jsonl"

MODE="run"
for arg in "$@"; do
  case "$arg" in
    --no-save) MODE="nosave" ;;
    --history) MODE="history" ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown arg: $arg" >&2; exit 2 ;;
  esac
done

if [[ "$MODE" == "history" ]]; then
  if [[ -f "$HISTORY_FILE" ]]; then cat "$HISTORY_FILE"; else echo "No history yet at $HISTORY_FILE"; fi
  exit 0
fi

AUTH_HEADER=()
if [[ -n "${GITHUB_TOKEN:-}" ]]; then
  AUTH_HEADER=(-H "Authorization: Bearer $GITHUB_TOKEN")
fi

RESP="$(curl -sf "${AUTH_HEADER[@]+${AUTH_HEADER[@]}}" \
  -H "Accept: application/vnd.github+json" \
  "https://api.github.com/repos/$REPO/releases")" || {
  echo "Failed to fetch releases from GitHub." >&2; exit 1;
}

HISTORY_FILE="$HISTORY_FILE" MODE="$MODE" python3 - "$RESP" <<'PY'
import json, os, sys, datetime

data = json.loads(sys.argv[1])
if isinstance(data, dict):
    print("GitHub API error:", data.get("message")); sys.exit(1)

history_file = os.environ["HISTORY_FILE"]
mode = os.environ["MODE"]

def is_installer(name):
    n = name.lower()
    if n.startswith("releases"):            # RELEASES, RELEASES-osx-arm64, ...
        return False
    if n.endswith(".nupkg"):                # Velopack update payloads
        return False
    return True

# Build current snapshot: per-asset counts keyed "tag/asset".
per_asset = {}
installer_total = 0
all_total = 0
for rel in data:
    tag = rel["tag_name"]
    for a in rel["assets"]:
        dc = a["download_count"]
        per_asset[f"{tag}/{a['name']}"] = dc
        all_total += dc
        if is_installer(a["name"]):
            installer_total += dc

now = datetime.datetime.now(datetime.timezone.utc).isoformat()

# Load last snapshot for delta.
prev = None
if os.path.exists(history_file):
    with open(history_file) as f:
        lines = [ln for ln in f if ln.strip()]
    if lines:
        prev = json.loads(lines[-1])

# Report.
print(f"ServerBridge downloads — {now}")
print(f"  Installer downloads (excl. update metadata): {installer_total}")
print(f"  All asset downloads:                          {all_total}")

if prev:
    dt = prev["at"]
    d_inst = installer_total - prev["installer_total"]
    d_all = all_total - prev["all_total"]
    print(f"\nSince last snapshot ({dt}):")
    print(f"  Installer downloads: {d_inst:+d}")
    print(f"  All assets:          {d_all:+d}")
    # Per-asset movers.
    movers = []
    for key, cur in per_asset.items():
        before = prev["per_asset"].get(key, 0)
        if cur != before:
            movers.append((key, cur - before, cur))
    if movers:
        print("\n  Per-asset changes:")
        for key, delta, cur in sorted(movers, key=lambda x: -x[1]):
            print(f"    {delta:+d}  (now {cur})  {key}")
    else:
        print("\n  No per-asset changes.")
else:
    print("\n(No previous snapshot — this is the baseline.)")
    print("\n  Current per-asset installer counts:")
    for key, cur in sorted(per_asset.items()):
        if is_installer(key.split("/", 1)[1]) and cur:
            print(f"    {cur:>5}  {key}")

# Save snapshot unless --no-save.
if mode == "run":
    snap = {
        "at": now,
        "installer_total": installer_total,
        "all_total": all_total,
        "per_asset": per_asset,
    }
    with open(history_file, "a") as f:
        f.write(json.dumps(snap) + "\n")
    print(f"\nSnapshot saved to {history_file}")
else:
    print("\n(--no-save: snapshot not written)")
PY
