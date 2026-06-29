#!/usr/bin/env python3
"""Generates server-bridge/eula.html from legal-source/ServerBridge-EULA.md.

The EULA's source of truth is MigratePro/legal/eula/ServerBridge-EULA.md (what the
app shows at first run). This repo keeps a synced copy at
server-bridge/legal-source/ServerBridge-EULA.md — whenever that .md changes in
MigratePro, copy the updated file here and rerun this script. Never hand-edit
server-bridge/eula.html directly; CI re-runs this script and fails the build if its
output doesn't match the committed file, so hand edits will be silently overwritten
and caught.
"""
import re
import sys
from pathlib import Path

import markdown

REPO_ROOT = Path(__file__).resolve().parent.parent
SOURCE_MD = REPO_ROOT / "server-bridge" / "legal-source" / "ServerBridge-EULA.md"
OUTPUT_HTML = REPO_ROOT / "server-bridge" / "eula.html"

PAGE_TEMPLATE = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>End User License Agreement — ServerBridge</title>
  <link rel="stylesheet" href="styles.css" />
  <link rel="icon" href="favicon.ico" sizes="any" />
  <link rel="apple-touch-icon" href="apple-touch-icon.png" />
</head>
<body>
  <header class="wrap topbar">
    <div class="brand"><span class="mark">S</span> ServerBridge</div>
    <a class="home" href="index.html">&larr; Back</a>
  </header>

  <main>
    <section class="wrap legal">
      <h1>End User License Agreement</h1>
      <p class="updated">Last updated: {effective_date} &middot; Version {version}</p>

{body}
    </section>
  </main>

  <footer>
    <div class="wrap">
      <span>&copy; <span id="yr"></span> Lee Crowe Software Solutions LLC</span>
      <span><a href="https://leecrowesoftware.com">leecrowesoftware.com</a></span>
    </div>
  </footer>

  <script>document.getElementById('yr').textContent = new Date().getFullYear();</script>
</body>
</html>
"""


def extract_metadata(md_text: str) -> tuple[str, str]:
    date_match = re.search(r"\*\*Effective Date:\*\*\s*(.+)", md_text)
    version_match = re.search(r"\*\*Version:\*\*\s*(.+)", md_text)
    if not date_match or not version_match:
        raise ValueError("Could not find Effective Date / Version in source .md")
    return date_match.group(1).strip(), version_match.group(1).strip()


def strip_header_block(md_text: str) -> str:
    """Drops the title, effective-date/version lines, and the intro blockquote —
    the page template renders the title and date/version itself, and the intro
    blockquote's content is folded into the opening paragraph by the source .md
    structure already (the '## 1. Definitions' heading is where real body starts)."""
    idx = md_text.find("## 1. Definitions")
    if idx == -1:
        raise ValueError("Could not find '## 1. Definitions' to anchor body start")
    # Keep the intro paragraph (the contract statement) that appears just before the
    # first blockquote ends and after it — re-extract it explicitly since it's the
    # operative "this is a contract between you and us" language.
    intro_match = re.search(
        r'> This End User License Agreement.*?(?=\n\n## 1\. Definitions)',
        md_text, re.DOTALL,
    )
    intro = ""
    if intro_match:
        intro = re.sub(r'^>\s?', '', intro_match.group(0), flags=re.MULTILINE).strip()
    return intro + "\n\n" + md_text[idx:]


def convert_links(html: str) -> str:
    """The source .md is plain text (the desktop app renders it in a bare TextBlock,
    no markdown parser, so it can't use markdown link syntax without showing literal
    brackets to users) — this turns its bare URLs into real <a> tags for the web page
    only, without ever touching the .md itself.
    """
    # Bare same-site URL referencing another page -> relative clickable link.
    html = re.sub(
        r'https://(?:www\.)?server-bridge\.com/([a-zA-Z0-9_-]+\.html)',
        r'<a href="\1">\1</a>',
        html,
    )
    # Bare site root URL (e.g. in the contact section) -> clickable link, relative path.
    html = re.sub(
        r'https://www\.server-bridge\.com/?(?=[\s.<])',
        r'<a href="index.html">server-bridge.com</a>',
        html,
    )
    # Bare email address -> mailto: link, matching the other legal pages' convention.
    html = re.sub(
        r'(?<![">@])([a-zA-Z0-9_.+-]+@leecrowesoftware\.com)',
        r'<a href="mailto:\1">\1</a>',
        html,
    )
    return html


def main() -> int:
    if not SOURCE_MD.exists():
        print(f"ERROR: {SOURCE_MD} not found.", file=sys.stderr)
        return 1

    md_text = SOURCE_MD.read_text(encoding="utf-8")
    effective_date, version = extract_metadata(md_text)
    body_md = strip_header_block(md_text)

    body_html = markdown.markdown(body_md, extensions=["extra", "sane_lists"])
    body_html = convert_links(body_html)
    # Re-indent for readability in the committed file.
    body_html = "\n".join(
        ("      " + line) if line.strip() else "" for line in body_html.splitlines()
    )

    output = PAGE_TEMPLATE.format(
        effective_date=effective_date,
        version=version,
        body=body_html,
    )
    OUTPUT_HTML.write_text(output, encoding="utf-8")
    print(f"Wrote {OUTPUT_HTML} ({len(output)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
