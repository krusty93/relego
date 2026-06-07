# Feature Specification: 009 Email Delivery

**Feature Branch**: `009-email-delivery`
**Created**: 2026-06-07
**Status**: Draft
**ADR**: [ADR-007 — Dual-Channel Email Delivery with Independent Composers](../../docs/adr/007-dual-channel-email-delivery.md)
**Input**: User request: "Add an optional regular email delivery channel so users can receive recaps without owning a Kindle device. The Kindle delivery channel remains but becomes optional. Recaps are delivered as inline HTML email (not EPUB attachments) to the regular email address."

## User Scenarios & Testing

### User Story 1 — Configure Regular Email Delivery (Priority: P1)

A user sets a non-Kindle email address via the CLI or TUI. The server stores it as `delivery_email`. From that point forward, scheduled recaps are sent to this address as formatted HTML email.

**Why this priority**: This is the foundational configuration step. Without the ability to set a delivery email address, no regular email delivery can occur.

**Independent Test**: Configure a `delivery_email` via `PATCH /settings` (or the CLI/TUI) and verify the value is persisted and returned by `GET /settings`. Verify the TUI/CLI warning is updated accordingly.

**Acceptance Scenarios**:

1. **Given** the user runs `relego config set delivery-email user@example.com`, **When** the command completes, **Then** the server persists `delivery_email` as `user@example.com` and `GET /settings` returns the new value.
2. **Given** the user navigates to the TUI settings page, **When** they edit the "Delivery email" field and save, **Then** the new email is stored on the server and reflected in the TUI.
3. **Given** a `delivery_email` is set, **When** the TUI renders, **Then** no "email not configured" warning is shown (provided at least one email channel is configured).
4. **Given** the user provides an invalid email format for `delivery-email`, **When** they attempt to save, **Then** a validation error is returned and the value is not persisted.

---

### User Story 2 — Receive HTML Recap via Email (Priority: P1)

When a scheduled recap runs and `delivery_email` is configured, the user receives a professionally formatted HTML email with their highlights rendered inline in the email body. The email is readable in Gmail, Outlook, Apple Mail, and other common email clients. No EPUB attachment is included in the regular email channel.

**Why this priority**: This is the core value proposition of the feature — the actual delivery of recaps to a regular email inbox. Without this, the configuration is meaningless.

**Independent Test**: Configure `delivery_email`, trigger a recap (via `POST /recaps`), and verify the recipient receives an HTML email with highlights in the body and a plain-text fallback.

**Acceptance Scenarios**:

1. **Given** `delivery_email` is configured and a recap is triggered, **When** the server sends the email, **Then** the recipient receives a multipart/alternative email containing both HTML and plain-text versions.
2. **Given** the email is opened in Gmail, **When** the user views it, **Then** highlights are displayed inline in the email body with book title, author, and highlight text clearly separated.
3. **Given** the email is opened in a mobile email client, **When** the user views it, **Then** the layout adapts to the screen width without horizontal scrolling.
4. **Given** the email includes multiple books, **When** the user reads it, **Then** highlights are grouped by book with clear visual separation between books.
5. **Given** the email is sent, **When** the user checks, **Then** no EPUB attachment is present in the regular email channel.

---

### User Story 3 — Dual-Channel Delivery (Priority: P2)

A user has both `kindle_email` and `delivery_email` configured. Each scheduled recap triggers two independent deliveries: one EPUB to the Kindle address and one HTML email to the regular address. A failure in one channel does not prevent the other channel from succeeding.

**Why this priority**: Dual-channel delivery is a power-user feature that increases flexibility, but it builds on the two single-channel modes which are individually more critical.

**Independent Test**: Configure both `kindle_email` and `delivery_email`, trigger a recap, and verify two separate emails are sent — one EPUB and one HTML — to their respective addresses. Simulate an SMTP failure for one channel and verify the other still delivers.

**Acceptance Scenarios**:

1. **Given** both `kindle_email` and `delivery_email` are configured, **When** a recap is triggered, **Then** the server sends an EPUB email to `kindle_email` AND an HTML email to `delivery_email`.
2. **Given** both channels are configured, **When** SMTP fails for the Kindle channel (EPUB), **Then** the regular email channel (HTML) still delivers successfully and the failure is logged.
3. **Given** both channels are configured, **When** SMTP fails for the regular email channel (HTML), **Then** the Kindle channel (EPUB) still delivers successfully and the failure is logged.
4. **Given** both channels are configured, **When** a recap runs, **Then** each delivery uses independent SMTP connections and neither blocks the other.

---

### User Story 4 — Kindle-Only Mode Preserved (Priority: P1)

Existing users who have only `kindle_email` configured (and no `delivery_email`) continue to receive recaps exactly as before — EPUB attachments to their Kindle address. No behavior changes, no migration steps, no disruption.

**Why this priority**: This is a backward-compatibility requirement. Breaking existing users' workflows would be unacceptable. Every existing Relego installation must continue functioning without changes.

**Independent Test**: With an existing database where only `kindle_email` is set (and `delivery_email` is null/empty), trigger a recap and verify an EPUB is sent to the Kindle address exactly as in the current version.

**Acceptance Scenarios**:

1. **Given** an existing user has `kindle_email` set and no `delivery_email`, **When** a recap runs, **Then** an EPUB is sent to `kindle_email` — identical behavior to the current version.
2. **Given** an existing user, **When** they upgrade to the new server version, **Then** their `kindle_email` value is preserved without any changes or data loss.
3. **Given** an existing user, **When** they run `GET /settings` after upgrade, **Then** `kindle_email` returns their existing value and `delivery_email` returns null/empty.
4. **Given** an existing `My Clippings.txt` workflow, **When** the user runs `relego sync`, **Then** the sync behavior is completely unchanged.

---

### User Story 5 — Email-Only Mode (Priority: P1)

A new user who does not own a Kindle device can use Relego fully by configuring only `delivery_email`. No Kindle email address is required. The recap is delivered as HTML email to their regular inbox. This opens Relego to a broader audience beyond Kindle owners.

**Why this priority**: This is the primary growth enabler — Relego becomes accessible to users without Kindle devices. It is the main reason feature 009 exists.

**Independent Test**: With a fresh installation, configure only `delivery_email` (leave `kindle_email` empty), trigger a recap, and verify an HTML email is received.

**Acceptance Scenarios**:

1. **Given** a fresh installation with only `delivery_email` configured, **When** a recap is triggered, **Then** an HTML email is sent to `delivery_email` and no EPUB is generated or sent.
2. **Given** only `delivery_email` is configured, **When** the TUI renders, **Then** no "email not configured" warning is displayed — the delivery email satisfies the requirement.
3. **Given** only `delivery_email` is configured, **When** the user runs `relego config show`, **Then** `kindle_email` is shown as empty and `delivery_email` is shown as configured.
4. **Given** only `delivery_email` is configured, **When** the user provides `My Clippings.txt` highlights and syncs, **Then** the sync, parsing, and storage flow is identical — only the delivery channel differs.

---

### User Story 6 — Test Email for Regular Channel (Priority: P2)

The existing `POST /settings/test-email` endpoint is extended so the user can send a test email to their configured `delivery_email`. This verifies that the SMTP configuration works for the regular email channel, not just Kindle.

**Why this priority**: Test email verification is important for user confidence, but it is a support function — the core delivery must work first. Users can also validate by triggering a real recap in Development mode.

**Independent Test**: Call the test-email endpoint with the `delivery_email` channel specified, and verify a plain-text test email (not a recap) is sent to that address.

**Acceptance Scenarios**:

1. **Given** `delivery_email` is configured and SMTP is functional, **When** the user triggers "Send test email" for the delivery channel, **Then** a plain-text test email is sent to `delivery_email` and a success response is returned.
2. **Given** `delivery_email` is not configured, **When** the user triggers a test email for the delivery channel, **Then** an actionable validation error is returned and no email is sent.
3. **Given** SMTP delivery fails for the test, **When** the user triggers the test email, **Then** an actionable error describing the SMTP failure is returned.
4. **Given** the test email is sent, **When** the user receives it, **Then** the email body is plain-text (not HTML, not a recap) and contains no attachments.
5. **Given** both channels are configured, **When** the user triggers a test email without specifying a channel, **Then** a test email is sent to BOTH addresses independently.

---

### User Story 7 — HTML Email Composition (Priority: P1)

The server composes a well-designed HTML email for the regular email channel. The email includes Relego branding, the recap date, and each highlight rendered with book title, author, and highlight text. The email uses responsive design that works on mobile and desktop email clients, with a plain-text fallback in multipart/alternative MIME format.

**Why this priority**: The email composition directly determines the user experience of receiving a recap. A poorly formatted email undermines the entire feature.

**Independent Test**: Trigger a recap and inspect the raw email source to verify: multipart/alternative MIME structure, HTML with inline styles (email-safe), responsive meta tags, and the plain-text part.

**Acceptance Scenarios**:

1. **Given** a recap is sent to `delivery_email`, **When** the email is composed, **Then** it uses multipart/alternative MIME with `text/html` and `text/plain` parts.
2. **Given** the HTML email, **When** it is rendered, **Then** it contains a header with the Relego brand name and logo (or text-based logotype compatible with email).
3. **Given** the HTML email, **When** it is rendered, **Then** it displays the recap date prominently.
4. **Given** the HTML email, **When** it is rendered, **Then** each highlight is displayed with the book title, author name, and the highlight text in a readable format.
5. **Given** the HTML email, **When** it is rendered, **Then** it contains a footer with "Sent by Relego" and a link to the project website.
6. **Given** the HTML email, **When** viewed on a mobile device (320px width), **Then** the layout adapts responsively without horizontal scrolling.
7. **Given** the HTML email, **When** viewed in Outlook desktop, **Then** it renders correctly using email-safe inline CSS (no external stylesheets, no CSS grid/flexbox as primary layout).
8. **Given** the HTML email, **When** the user's client blocks images, **Then** all content remains readable through text alternatives.
9. **Given** the plain-text email part, **When** the user's client displays it, **Then** all highlights are readable with clear book/author/highlight separation using text-only formatting.

---

### User Story 8 — CLI/TUI Settings Management (Priority: P2)

The CLI `config` command and the TUI settings screen expose the new `delivery-email` setting alongside `kindle-email`. The TUI warning displayed when no email is configured is updated to check both fields — only showing the warning when NEITHER email is set.

**Why this priority**: CLI/TUI integration makes the feature accessible and aligns with Relego's configuration UX, but the server-side delivery capability is more critical.

**Independent Test**: Run `relego config set delivery-email user@example.com` and verify it works. Open the TUI settings page and verify the `delivery-email` field is present and editable.

**Acceptance Scenarios**:

1. **Given** the user runs `relego config set delivery-email user@example.com`, **When** the command completes, **Then** the delivery email is persisted on the server.
2. **Given** the user runs `relego config show`, **When** the output is displayed, **Then** both `kindle-email` and `delivery-email` are shown with their current values.
3. **Given** the user navigates to the TUI settings page, **When** it renders, **Then** a "Delivery email" field is displayed alongside "Kindle email."
4. **Given** the TUI settings page, **When** the user edits and saves the delivery email, **Then** the value is validated (email format) and sent to the server.
5. **Given** NEITHER `kindle_email` nor `delivery_email` is configured, **When** the TUI renders, **Then** a persistent warning is shown: "No delivery email configured — recaps cannot be delivered."
6. **Given** at least ONE of `kindle_email` or `delivery_email` is configured, **When** the TUI renders, **Then** no email warning is displayed.
7. **Given** the user runs `relego config set delivery-email ""` (empty), **When** the command completes, **Then** the delivery email is cleared and the server no longer sends recaps to that channel.

---

### Edge Cases

- **Both emails empty or null**: When neither `kindle_email` nor `delivery_email` is configured, the recap scheduler skips delivery entirely (no SMTP connections are attempted). The TUI shows a persistent warning. The server logs a warning that no delivery channel is configured.
- **SMTP fails for one channel, succeeds for the other**: In dual-channel mode, each delivery is independent. If the Kindle/EPUB delivery fails, the regular/HTML delivery still proceeds (and vice versa). Both success and failure outcomes are logged separately.
- **`delivery_email` set to a `@kindle.com` address**: The regular email channel always sends HTML email regardless of the domain. If a user sets `delivery_email` to their `@kindle.com` address, they receive HTML email (not EPUB). The Kindle channel (`kindle_email`) remains the dedicated EPUB channel. This is documented behavior.
- **Empty recap (no highlights selected for the period)**: When the recap selection yields zero highlights, no email is sent to either channel. The server logs the event and does not attempt delivery.
- **Database migration — `delivery_email` column**: Existing databases without the `delivery_email` column are migrated automatically on server startup. The new column defaults to NULL, preserving zero-impact for existing users.
- **Very long highlight text**: Highlights exceeding a practical email line length (e.g., 2000+ characters) are truncated in the plain-text part and fully included in the HTML part with appropriate styling.
- **Unicode and special characters in highlights**: Book titles, author names, and highlight text containing Unicode characters (emoji, non-Latin scripts) are correctly encoded in both the HTML (UTF-8) and plain-text MIME parts.
- **Concurrent recap triggers**: If two recaps are triggered simultaneously (e.g., manual trigger during a scheduled run), each runs independently and may produce duplicate deliveries. This is consistent with existing Kindle-only behavior.

## Requirements

### Functional Requirements

- **FR-009-01**: The `users` table MUST support a new `delivery_email` column (nullable, same type and constraints as `kindle_email`).
- **FR-009-02**: Server MUST auto-migrate the database on startup — existing rows with no `delivery_email` column get the column added with a NULL default.
- **FR-009-03**: `GET /settings` MUST return both `kindle_email` and `delivery_email` fields.
- **FR-009-04**: `PATCH /settings` MUST accept and validate both `kindle_email` and `delivery_email` fields independently.
- **FR-009-05**: Server MUST validate that at least one of `kindle_email` or `delivery_email` has a valid email format when either is set — an empty string clears the field; a null/missing field leaves it unchanged.
- **FR-009-06**: Recap execution MUST check both `kindle_email` and `delivery_email` — if NEITHER is set, the recap is skipped with a logged warning.
- **FR-009-07**: Recap execution MUST send an EPUB email to `kindle_email` when `kindle_email` is configured (existing behavior, unchanged).
- **FR-009-08**: Recap execution MUST send an HTML email to `delivery_email` when `delivery_email` is configured (new behavior).
- **FR-009-09**: In dual-channel mode (both emails configured), each channel delivery MUST be independent — a failure in one channel MUST NOT prevent the other channel from attempting delivery.
- **FR-009-10**: The regular email channel MUST deliver highlights as inline HTML in the email body (no EPUB attachment).
- **FR-009-11**: The HTML email MUST use multipart/alternative MIME with both `text/html` and `text/plain` parts.
- **FR-009-12**: The HTML email MUST contain: a branded header with Relego name/logo, recap date, highlights grouped by book with title/author/text, and a footer with "Sent by Relego" and a project link.
- **FR-009-13**: The HTML email MUST use email-safe inline CSS (no external stylesheets, no JavaScript, limited CSS Grid/Flexbox usage) and be responsive for mobile and desktop clients.
- **FR-009-14**: The plain-text email part MUST render all highlights with clear text-only formatting (book title, author, highlight text separated by whitespace).
- **FR-009-15**: `POST /settings/test-email` MUST support an optional channel parameter to specify `kindle`, `delivery`, or `both` (default: `both` when both are configured, or whichever single channel is configured).
- **FR-009-16**: Test email for the delivery channel MUST send a plain-text email (not HTML, not a recap) to `delivery_email`.
- **FR-009-17**: The CLI `config set` command MUST accept `delivery-email` as a setting key alongside `kindle-email`.
- **FR-009-18**: The CLI `config show` command MUST display `delivery-email` alongside `kindle-email`.
- **FR-009-19**: The TUI settings page MUST expose a "Delivery email" field alongside the existing "Kindle email" field.
- **FR-009-20**: The TUI email warning MUST update to check both fields — displayed only when NEITHER `kindle_email` nor `delivery_email` is configured. The warning text MUST be generic: "No delivery email configured — recaps cannot be delivered."
- **FR-009-21**: The `delivery_email` field MUST share the same email format validation as `kindle_email`.
- **FR-009-22**: Each delivery outcome (success or failure, per channel) MUST be logged independently with channel identification.

### Key Entities

- **User (extended)**: The existing `users` table gains a `delivery_email` column (nullable string, email-validated). The `kindle_email` column remains unchanged. A user may have zero, one, or two delivery channels active.
- **HtmlEmailComposer**: A new service that constructs a multipart/alternative MIME email from selected highlights. It produces both the HTML part (with inline styles, branding, responsive layout) and the plain-text part (text-only formatting).
- **Recap Delivery (redefined)**: A recap delivery is no longer a single email action — it is a set of independent channel deliveries. The recap engine iterates over active channels (Kindle → EPUB, Regular → HTML) and delivers each independently, logging each outcome.
- **Delivery Channel**: An abstract concept representing a delivery target. Two concrete channels exist: `kindle` (EPUB attachment to `kindle_email`) and `email` (HTML inline to `delivery_email`). Each channel has its own composer, delivery method, and failure handling.

## Success Criteria

### Measurable Outcomes

- **SC-009-01**: A user who only sets `delivery_email` (no Kindle) can receive a complete HTML recap email within the same scheduled interval as the existing Kindle-only flow.
- **SC-009-02**: Existing users with only `kindle_email` set experience zero behavioral change after upgrading — all existing tests pass without modification to test assertions about Kindle delivery.
- **SC-009-03**: The HTML email renders correctly in Gmail (web and mobile), Apple Mail (desktop and iOS), and Outlook (desktop and web) — validated by manual inspection or email testing tools.
- **SC-009-04**: In dual-channel mode, a failure in one channel has zero impact on the other channel's delivery success rate.
- **SC-009-05**: The `PATCH /settings` endpoint accepts `delivery_email` and returns it in `GET /settings` within 200ms under normal conditions.
- **SC-009-06**: The CLI `config set delivery-email` and `config show` commands complete in under 1 second for a reachable server.
- **SC-009-07**: The TUI email warning correctly reflects the combined state of both email fields — warning shown only when both are empty, hidden when at least one is set.
- **SC-009-08**: Database migration from a pre-009 schema to the new schema completes automatically on server startup with zero data loss and zero manual steps.
- **SC-009-09**: The plain-text email part is readable in any email client that cannot render HTML — all highlight content is preserved.
- **SC-009-10**: Test email endpoint correctly routes to the specified channel(s) — `kindle` sends EPUB-free plain-text, `delivery` sends EPUB-free plain-text, `both` sends to both.

## Assumptions

- SMTP configuration (host, port, credentials) is already handled by the existing `MailDeliveryService` and does not need changes for the new channel — the same SMTP connection is used for both delivery types.
- The existing MailKit integration (`MailDeliveryService`) can be extended to support HTML email body composition without architectural changes.
- The Kindle channel always sends EPUB attachments to `kindle_email` and never HTML; the regular channel always sends HTML inline to `delivery_email` and never EPUB. Channels do not cross-compose.
- The `POST /settings/test-email` endpoint extension accepts an optional `channel` field — callers omitting it get auto-detect behavior (test all configured channels).
- The Relego brand assets (logo, colors) are available for embedding in HTML emails as defined in `BRAND_COLORS.md`.
- Email client compatibility testing (Gmail, Outlook, Apple Mail) will be done via manual QA or an email testing service (e.g., Litmus, Email on Acid); not automated in CI.
- The CLI `config` command changes are additive — no existing command syntax is removed or broken.
- TUI changes for feature 009 build on the Terminal.Gui v2 implementation from feature 007.
