# Legal source files

`ServerBridge-EULA.md` here is a **synced copy**, not the source of truth. The real
source of truth is `desktop/legal/eula/ServerBridge-EULA.md` (a sibling repo) — it's
what the ServerBridge desktop app actually displays and requires acceptance of at
first run, and it's versioned via the `EulaVersion` constant in
`ServerBridge.Core/Services/Legal/EulaService.cs`.

`server-bridge/eula.html` is **generated** from the copy in this folder by
`scripts/generate_eula.py`. Never hand-edit `eula.html` — CI (`.github/workflows/check-eula-sync.yml`)
regenerates it on every push/PR that touches these files and fails the build if the
committed file doesn't match, so hand edits get silently overwritten and caught.

## Whenever the EULA changes in desktop

```bash
./scripts/sync_eula_from_serverbridge.sh
```

This copies the updated `.md` in from the sibling `desktop` checkout and regenerates
`eula.html`. Commit both the updated `legal-source/ServerBridge-EULA.md` and the
regenerated `eula.html` together.

## Why this exists

An earlier hand-written version of `eula.html` silently dropped several operative
clauses (a GDPR/CCPA/HIPAA compliance clause, a liability-cap qualifier, a risk-assumption
sentence, a warranty-disclaimer item, and termination-notice specifics) purely from
paraphrasing the real EULA into different prose. Generating the page directly from the
real document makes that class of error structurally impossible — the only way `eula.html`
can be wrong now is if `legal-source/ServerBridge-EULA.md` itself is out of sync with
desktop's copy, which `sync_eula_from_serverbridge.sh` exists to prevent.
