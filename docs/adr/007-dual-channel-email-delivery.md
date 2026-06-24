# ADR-007: Dual-Channel Email Delivery with Independent Composers

**Status:** Accepted — Amended 2026-06-22
**Date:** 2026-06-07

## Context

Relego currently delivers recaps exclusively as EPUB attachments to a Kindle email address (`kindle_email` on the `users` table). Feature 009 must add an optional regular email delivery channel so users without a Kindle device can receive recaps as inline HTML email. The Kindle channel must remain fully functional and unchanged for existing users.

Key tensions to resolve:

1. **Database schema**: Rename `kindle_email` to a generic field, or add a separate `delivery_email` column?
2. **Channel model**: Unified channel with smart format detection, or two independent channels?
3. **Recap format for non-Kindle**: EPUB attachment (reuse existing composer), inline HTML email, or both?
4. **Composition**: Single composer with format parameter, or channel-specific composers?
5. **Test email**: New endpoint, or extend existing `POST /settings/test-email`?

## Decision

### 1. Database: Add `delivery_email` column, keep `kindle_email`

A new nullable `delivery_email TEXT` column is added to the `users` table. `kindle_email` remains unchanged.

**Rationale**: Renaming `kindle_email` would require migration logic for all existing rows, breaking the zero-impact upgrade promise. A separate column is additive, self-documenting (the column name carries semantic meaning about the channel), and allows both channels to coexist without ambiguity.

### 2. Channel Model: Two independent delivery channels

Kindle (`kindle_email` → EPUB) and Regular (`delivery_email` → HTML) are fully independent. Each channel delivery succeeds or fails on its own. A failure in one channel does not block the other.

**Rationale**: Independent channels prevent cascading failures. If SMTP has a transient issue with one recipient domain, the other still delivers. This also simplifies logging, retry logic, and debugging — each channel outcome is a discrete event. A unified channel would require error-handling complexity (partial success semantics) for no user benefit.

### 3. Regular Channel Format: Inline HTML email (multipart/alternative)

The regular email channel sends highlights as inline HTML in the email body with a plain-text fallback, using `multipart/alternative` MIME. No EPUB attachment is included in the regular channel.

**Rationale**: EPUB attachments are meaningless in standard email clients (Gmail, Outlook, Apple Mail) — users cannot open them without an e-reader app. Inline HTML renders immediately in any email client and provides a superior reading experience. The plain-text fallback ensures accessibility. The Kindle channel remains the dedicated EPUB path for e-ink devices.

### 4. Composition: Channel-specific composers

`EpubComposer` (existing, unchanged) handles the Kindle channel. A new `HtmlEmailComposer` handles the regular channel. Each composer produces the format appropriate to its channel.

**Rationale**: The two output formats have fundamentally different constraints. EPUB is a zipped XML document bundle targeting e-ink renderers. HTML email is a single MIME part constrained by email client CSS support (no external stylesheets, limited flexbox/grid, inline styles). Forcing a single composer to handle both would create a confusing abstraction with format-switching logic. Separate composers keep each implementation focused and testable.

### 5. Test Email: Extend existing endpoint with channel parameter

`POST /settings/test-email` gains an optional `channel` body parameter (`kindle`, `delivery`, or `both`). Omitting the parameter defaults to `both` when both channels are configured, or the single active channel.

**Rationale**: A net-new endpoint would fragment the API surface and require duplicate validation logic. The existing endpoint already handles SMTP connectivity, error reporting, and the plain-text test email body. Adding a channel discriminator is a minimal, backward-compatible extension. Callers unaware of the new parameter continue to work unchanged.

## Consequences

- **Zero breaking changes** for existing users: `kindle_email` is untouched, the `PATCH /settings` schema is additive, and the test-email endpoint is backward-compatible.
- **New `HtmlEmailComposer`** must handle email-client-safe HTML (inline CSS, table-based layout fallbacks, no JavaScript, no external resources beyond a linked image with `alt` text).
- **Dual-channel delivery** doubles the SMTP connection count per recap when both channels are active. This is acceptable because recaps are infrequent (daily/weekly) and SMTP connections are short-lived.
- **Auto-migration** on server startup uses `ALTER TABLE users ADD COLUMN delivery_email TEXT` with SQLite's built-in NULL default — no data migration script needed.
- Future channels (e.g., Pushover, Telegram) can follow the same pattern: a new column on `users`, a channel-specific composer, and an independent delivery path in `RecapService`.

---

## Amendment — 2026-06-22: Optional destinations with at-least-one enforcement

### Context

After feature 009 shipped, early users reported friction: the model still treated `kindle_email` as a conceptual required field (seeded as empty string, prominent in the TUI), while the dual-channel intention allows users with only a regular inbox to use Relego without a Kindle. The original spec also failed silently when neither destination was configured — recaps were queued but never delivered.

### Additional decisions

**A. Both destinations are fully optional.** `kindle_email` and `delivery_email` are semantically equivalent peers. Neither is required in isolation. The system does not assign a default "primary" channel.

**B. At-least-one enforcement at delivery time, not at settings-save time.** `PATCH /settings` continues to accept any combination (including clearing both); it validates only format, not presence. This preserves flexibility — users can temporarily clear both during re-configuration without hitting a validation error. Enforcement occurs at two points:

- `POST /recaps`: returns HTTP 422 with a clear, actionable message if neither destination is configured at trigger time.
- `RecapService.ExecuteAsync`: defense-in-depth guard; marks the job failed with a human-readable reason if neither is set at execution time.

**C.** ~~Server logs a WARNING at startup~~ — **Removed.** The startup warning was found to add noise with no actionable value; the 422 guard on `POST /recaps` and the TUI persistent warning (see E) are sufficient to surface the misconfiguration.

**D. CLI `config email kindle|inbox` replaces the old `config kindle-email`/`config delivery-email` commands.** The new surface makes the peer relationship explicit. `relego config email kindle <addr>` sets the Send-to-Kindle address; `relego config email inbox <addr>` sets the inbox address (pass `""` to clear). `relego recap trigger` performs a client-side pre-check and returns exit code 1 with a clear message before calling the server if neither address is set.

**E. TUI StatusChrome warning fires when *neither* destination is configured.** Text: `⚠ No recap delivery destination configured`. The previous Kindle-only warning is replaced. The TUI Settings screen groups both fields under a "Recap Delivery Settings" entry that opens a "Deliver recap to…" popup with independent **Send to Kindle** and **Send to inbox** fields.

**F. No DB migration.** `kindle_email` remains `NOT NULL` in the schema and is seeded as `''`; the empty-string-as-unset convention is preserved. No column type change is needed.
