"""Build server-bridge.com/guides/ from guides-source/*.md (outside the deployed folder, so drafts and
keyword notes are not published).

    python3 scripts/gen_guides.py

Each source keeps the front matter it was drafted with (title, meta_description, ...). The page
title and description come from it; the "status" and keyword fields are not published. Links in
the drafts that point at leecrowesoftware.com/guides/... are rewritten to the pages here, because
the guides live on server-bridge.com (DECISIONS 2026-10-16). Needs pandoc.
"""
import html, pathlib, re, subprocess

ROOT = pathlib.Path(__file__).resolve().parent.parent / "server-bridge"
SRC, OUT = ROOT.parent / "guides-source", ROOT / "guides"
SITE = "https://server-bridge.com"

def front(md):
    m = re.match(r"---\n(.*?)\n---\n", md, re.S)
    meta = dict(re.findall(r'^(\w+):\s*"(.*)"\s*$', m.group(1), re.M)) if m else {}
    return meta, md[m.end():] if m else md

def page(title, desc, canonical, body):
    return f'''<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{html.escape(title)}</title>
  <meta name="description" content="{html.escape(desc)}" />
  <meta property="og:title" content="{html.escape(title)}" />
  <meta property="og:description" content="{html.escape(desc)}" />
  <meta property="og:type" content="article" />
  <meta property="og:url" content="{canonical}" />
  <meta property="og:image" content="{SITE}/og-image.png" />
  <link rel="stylesheet" href="/styles.css" />
  <link rel="icon" href="/favicon.ico" sizes="any" />
  <link rel="apple-touch-icon" href="/apple-touch-icon.png" />
  <link rel="canonical" href="{canonical}" />
</head>
<body>
  <header class="wrap topbar">
    <div class="brand"><a href="/license-auditor.html" style="color:inherit;text-decoration:none;display:flex;align-items:center;gap:10px"><span class="mark">S</span> ServerBridge</a></div>
    <a class="home" href="/guides/">&larr; All guides</a>
  </header>

  <main>
    <article class="wrap legal guide">
{body}
    </article>
  </main>

  <footer>
    <div class="wrap">
      <span>&copy; <span id="yr"></span> Lee Crowe Software Solutions LLC</span>
      <span><a href="/license-auditor.html">License Auditor</a> &middot; <a href="/license-auditor-privacy.html">Privacy</a> &middot; <a href="https://leecrowesoftware.com">leecrowesoftware.com</a></span>
    </div>
  </footer>

  <script>document.getElementById('yr').textContent = new Date().getFullYear();</script>
  <!-- Cloudflare Web Analytics -->
  <script defer src='https://static.cloudflareinsights.com/beacon.min.js' data-cf-beacon='{{"token": "1dd7b99606b2470b86ee6e97cafd2ab3"}}'></script>
  <!-- End Cloudflare Web Analytics -->
  <!-- PostHog website analytics (cookieless) — see /js/site-analytics.js -->
  <script src="/assets/posthog.js" defer></script>
  <script src="/js/site-analytics.js" data-site="server-bridge" defer></script>
</body>
</html>
'''

def main():
    OUT.mkdir(exist_ok=True)
    index = []
    # Reading order for the index: the general guide first, then the price change, then leavers.
    ORDER = ["unused-microsoft-365-licenses", "microsoft-365-price-increase-july-2026",
             "find-inactive-users-microsoft-365"]
    sources = sorted(SRC.glob("*.md"), key=lambda p: ORDER.index(p.stem) if p.stem in ORDER else 99)
    for src in sources:
        meta, md = front(src.read_text())
        slug = src.stem
        md = re.sub(r"https://leecrowesoftware\.com/guides/([a-z0-9-]+)", r"/guides/\1.html", md)
        body = subprocess.run(["pandoc", "-f", "gfm", "-t", "html", "--wrap=preserve"],
                              input=md, capture_output=True, text=True, check=True).stdout
        canonical = f"{SITE}/guides/{slug}.html"
        (OUT / f"{slug}.html").write_text(page(meta["title"], meta["meta_description"], canonical, body))
        h1 = re.search(r"^# (.+)$", md, re.M).group(1)
        index.append((slug, h1, meta["meta_description"]))
        print("wrote", slug)
    items = "\n".join(
        f'      <li><a href="/guides/{s}.html">{html.escape(h)}</a><br />{html.escape(d)}</li>' for s, h, d in index)
    body = f'''      <h1>Microsoft 365 licence guides</h1>
      <p class="updated">Practical, read-only ways to find what you are paying for and not using.</p>
      <ul class="guide-list">
{items}
      </ul>'''
    (OUT / "index.html").write_text(page("Microsoft 365 licence guides — ServerBridge",
        "Guides to finding unused Microsoft 365 licences, inactive users and the July 2026 price changes.",
        f"{SITE}/guides/", body))
    print("wrote index")

main()
