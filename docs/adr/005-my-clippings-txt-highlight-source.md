# ADR-005: `My Clippings.txt` as MVP Highlight Source

**Status:** Accepted
**Date:** 2026-03-31
**Updated:** 2026-11-19 — the `[my note] ` prefix handling was revised by ADR-009; notes are no longer imported as separate prefixed highlights.

## Context

Kindle highlights must be imported into the system. Amazon does not expose an official API for highlights. The alternatives investigated were:

- **Scraping `read.amazon.com`** — fragile. Amazon changed TLS fingerprinting in July 2023; sessions break without warning.
- **`kindle-api` (Node.js)** — cookie-based session via a local TLS proxy. Unofficial, undocumented, and may break with any Amazon update.
- **`My Clippings.txt`** — a plain-text file saved locally on every Kindle device when the user highlights text. Accessible by connecting the Kindle via USB. No Amazon account required.

## Decision

Use **`My Clippings.txt`** as the sole highlight source for MVP.

The client CLI reads this file when the user runs `relego sync`, parses all highlights and annotations, deduplicates (Kindle appends duplicates on re-highlight), and syncs new highlights to the server. How a reader's own note and its dot-prefixed markers are interpreted — and the retirement of the legacy `[my note] ` prefix this source used to carry — is decided in ADR-009.

## Consequences

- Zero dependency on Amazon's website, APIs, or account credentials.
- Works fully offline.
- The file format has been stable across Kindle generations for many years.
- No risk of breaking changes from Amazon infrastructure updates.
- The user must connect their Kindle via USB periodically to import new highlights — no automatic background sync.
- Post-MVP: web sources integration, web clipper, and scraping-based connectors can be added as optional source plugins, with a clear disclaimer to users that unofficial sources may break.
