# ADR-007: Dual-Channel Email Delivery with Independent Composers

**Status:** Accepted
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
