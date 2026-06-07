# Research: Email Delivery

**Feature**: 009-email-delivery
**Phase**: 0 — Research
**Date**: 2026-06-07

---

## Research Tasks & Findings

### 1. HTML Email Composition in .NET with MimeKit

**Decision**: Use MimeKit's `BodyBuilder` class to construct `multipart/alternative` MIME messages with `text/html` and `text/plain` parts. Build HTML with a static string composition approach using `StringBuilder` — no template engine.

**Rationale**:

- MimeKit is already a project dependency (used by `MailDeliveryService` and `DevMailDeliveryService`). The `BodyBuilder` class provides built-in support for `multipart/alternative` construction:

  ```csharp
  var builder = new BodyBuilder();
  builder.TextBody = plainTextContent;
  builder.HtmlBody = htmlContent;
  message.Body = builder.ToMessageBody();
  ```

- No additional NuGet packages needed. MimeKit handles MIME encoding, character sets (UTF-8), and content transfer encoding automatically.
- A static `StringBuilder`-based HTML composer (similar to `EpubComposer`'s static XHTML builder) keeps the approach consistent with the existing codebase. No Razor, Scriban, or other template engines — those would add dependencies and complexity for a single email template.
- Email HTML has unique constraints (inline CSS, table-based layout, no external stylesheets) that make general-purpose template engines less valuable. The HTML structure is simple enough (header, date, book groups, highlights, footer) that a dedicated builder method is maintainable.

**Key implementation details**:

- HTML must use inline styles exclusively — no `<link>`, no `<style>` in `<head>` (some clients strip `<head>`).
- Layout uses `<table>` with `max-width: 600px` for the container, `<td>` for content cells.
- System font stack: `font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif`.
- Responsive via `viewport` meta tag and percentage-based widths, not media queries (Gmail strips `<style>` blocks).
- Brand colors from `Relego.Core.Branding.BrandColors` — reused in HTML email via hex values.
- Plain-text part uses text-only formatting: book title underlined with `=`, highlight text indented with `>`, author on separate line.

**Alternatives considered**:

- **Razor templating**: Would require `Microsoft.AspNetCore.Mvc.Razor` or `RazorEngine.NetCore`. Overkill for a single email template. Adds compilation overhead and dependency.
- **Scriban/Liquid templates**: External template engines. Powerful but unnecessary. Add NuGet dependencies and file I/O for template loading.
- **HTML in resource file (.resx)**: Embedding HTML as a resource string. Works but makes iteration harder (rebuild to see changes). The static builder approach keeps everything in one place and is trivially testable.
- **MJML framework**: Requires Node.js toolchain. Violates "C# only" tech constraint. Rejected.

---

### 2. Email Client Compatibility

**Decision**: Target Gmail, Outlook (desktop and web), and Apple Mail with a conservative, table-based HTML layout using inline CSS only.

**Rationale**:

- **Gmail**: Strips `<style>` blocks from `<head>` (even `<style>` in `<body>` is inconsistent). Inline styles are required. Supports `max-width` on tables. Does not support `display: grid` or `display: flex` reliably. Media queries are stripped.
- **Outlook (desktop, Windows)**: Uses Word's HTML rendering engine (not a browser engine). Does not support CSS `float`, `flexbox`, `grid`, `border-radius` (in some versions), or `max-width` on block elements. Table-based layout with `width` attributes is the safest approach. Outlook ignores the `<head>` entirely — all styles must be inline.
- **Apple Mail / iOS Mail**: Modern WebKit-based rendering. Supports most CSS including flexbox, `@media` queries, and web fonts. Most forgiving client.
- **Strategy**: Design for the lowest common denominator (Outlook desktop). Use `<table>` for layout, inline styles for all visual properties, `width` attributes on `<td>`, and avoid CSS that Outlook doesn't support. Apple Mail and Gmail will render the same HTML correctly. No JavaScript (all clients block it).

**Key practices**:

- Use 6-digit hex colors (Outlook sometimes mishandles 3-digit shorthand).
- All images must have `alt` text (shown when images are blocked).
- `margin` on `<body>` is unreliable — use `<table>` with `cellpadding` and `cellspacing` instead.
- Link styling: wrap in `<span>` with inline color; `<a>` tag styling is inconsistent.
- Test with Litmus or Email on Acid (manual verification acceptable for MVP).

**Alternatives considered**:

- **Modern CSS-only layout (flexbox/grid)**: Fails in Outlook desktop. While Relego targets consumers, we cannot guarantee which client they use. Conservative approach is safer.
- **Single-column text-only**: Simplest but does not meet FR-009-12 (branded header, structured layout).

---

### 3. Dual-Channel Delivery Patterns

**Decision**: In `RecapService.ExecuteAsync()`, compose both the EPUB and HTML MIME message upfront (if their respective channels are configured). Then execute each delivery independently with its own try-catch and logging. Each channel uses a fresh retry policy instance so that Kindle retries do not interfere with Email retries.

**Rationale**:

- Independence is the core requirement (FR-009-09): a failure in one channel MUST NOT prevent the other. Sequential execution with per-channel exception handling satisfies this cleanly.
- Composing both messages upfront (before any SMTP calls) ensures that an HTML composition error does not leave the EPUB undelivered. If composition fails for one channel, the other can still proceed.
- Each channel gets its own `AsyncRetryPolicy` (instantiated fresh per invocation). This avoids shared retry state between channels.
- SMTP connections are independent per `SendEmailAsync` call — `MailDeliveryService` creates a new `SmtpClient` per send (existing behavior). No shared connection pooling issues.
- Logging uses structured properties: `{Channel = "Kindle"}`, `{Channel = "Email"}` for easy filtering in Serilog.

**Error handling** (pseudocode):

```csharp
bool kindleOk = false, emailOk = false;

// Compose EPUB upfront (if Kindle channel is active)
byte[]? epub = null;
if (hasKindle) {
    try { epub = EpubComposer.Compose(candidates, recapDate, cadence); }
    catch (Exception ex) { _logger.LogError(ex, "EPUB composition failed"); }
}

// Compose HTML upfront (if Email channel is active)
MimeMessage? htmlMessage = null;
if (hasEmail) {
    try { htmlMessage = HtmlEmailComposer.Compose(candidates, recapDate, cadence, user.DeliveryEmail, fromAddress); }
    catch (Exception ex) { _logger.LogError(ex, "HTML email composition failed"); }
}

// Deliver each channel independently — failures do not cascade
if (hasKindle && epub is not null) {
    try { await SendEpubWithRetry(user.KindleEmail, epub, fileName); kindleOk = true; }
    catch (Exception ex) { _logger.LogError(ex, "Kindle delivery failed after retries"); }
}
if (hasEmail && htmlMessage is not null) {
    try { await SendHtmlWithRetry(htmlMessage); emailOk = true; }
    catch (Exception ex) { _logger.LogError(ex, "Email delivery failed after retries"); }
}

// Recap is successful if at least one active channel delivered
bool anyDelivered = kindleOk || emailOk;
if (anyDelivered) {
    await MarkJobDelivered(jobId, candidates);
} else if (!hasKindle && !hasEmail) {
    _logger.LogWarning("No delivery channels configured — skipping");
} else {
    await MarkJobFailed(jobId, "All configured channels failed to deliver");
}
```

Key design points:
- **Composition errors are not delivery errors**: If EPUB composition fails but HTML delivers, the recap is still successful (and vice versa).
- **Each channel's try-catch is independent**: An unhandled exception in the Kindle block does not skip the Email block. The `catch` boundaries are per-channel.
- **Retry policies are per-channel**: Fresh `AsyncRetryPolicy` for each — preventing retry state leakage between channels.
- **Sequential is correct, parallel adds no practical benefit**: SMTP latency (100ms–2s per send) dominates total time. Parallelizing two sends saves ~50% of that, but adds `AggregateException` unwrapping and makes per-channel logging order non-deterministic. Sequential keeps the code linear and debuggable.

**Alternatives considered**:

- **Parallel delivery (Task.WhenAll)**: Both channels fire simultaneously. Saves at most ~1s in wall-clock time (SMTP latency), at the cost of `AggregateException` unwrapping and non-deterministic log ordering. Not worth the complexity for a twice-daily-at-most operation.
- **Channel abstraction with `IDeliveryChannel` interface**: Two implementations (`KindleChannel`, `EmailChannel`) registered via DI. Over-engineered for 2 channels. YAGNI. The `if (hasKindle) ... if (hasEmail) ...` pattern is clear and maintainable for the MVP.
- **Queue-based delivery (outbox pattern)**: Each delivery enqueued, processed by background worker. Adds infrastructure complexity. Rejected for MVP.

---

### 4. Database Migration Strategy for SQLite

**Decision**: On server startup, after the main `CREATE TABLE IF NOT EXISTS` schema bootstrap, run an `ALTER TABLE users ADD COLUMN delivery_email TEXT NULL` statement. Guard against duplicate column errors with a `try-catch` on SQLite error code "duplicate column name" or by checking `PRAGMA table_info(users)` before adding.

**Rationale**:

- SQLite supports `ALTER TABLE ADD COLUMN` but does NOT support `ADD COLUMN IF NOT EXISTS`. The `IF NOT EXISTS` syntax is invalid in SQLite.
- Two approaches to idempotent migration:
  1. **Try-catch**: Execute `ALTER TABLE ... ADD COLUMN ...` in a try block. If it throws with "duplicate column name", catch and ignore. Simple but relies on exception message parsing.
  2. **PRAGMA check**: Query `PRAGMA table_info(users)` and check if `delivery_email` column exists before executing ALTER. More robust.
- The PRAGMA approach is preferred: it's deterministic, doesn't rely on exception messages, and works across SQLite versions.
- For fresh installs, the `CREATE TABLE IF NOT EXISTS users` statement already includes the `delivery_email TEXT NULL` column definition — no migration needed.
- The migration runs in `SchemaBootstrap.ApplyAsync()` after the main schema SQL, keeping all DB initialization in one place.

**Migration SQL**:

```sql
-- Check if column exists (in C#):
-- var columns = await connection.QueryAsync<(int cid, string name, string type, int notnull, string dflt_value, int pk)>("PRAGMA table_info(users)");
-- if (!columns.Any(c => c.name == "delivery_email")) {
--     await connection.ExecuteAsync("ALTER TABLE users ADD COLUMN delivery_email TEXT NULL");
-- }
```

**Alternatives considered**:

- **Dedicated migration framework (FluentMigrator, DbUp)**: Adds dependencies. Overkill for a single-column migration. Rejected per YAGNI.
- **Drop and recreate**: Data loss unacceptable for existing users.
- **Separate migrations table**: Tracks which migrations have been applied. Good practice for larger projects but overkill for MVP. The PRAGMA check is sufficient for a single migration.

---

### 5. Existing MailKit/MimeKit Usage in the Project

**Decision**: Extend the existing `IMailDeliveryService` interface with a `SendHtmlRecapAsync(MimeMessage message, ...)` method. `HtmlEmailComposer.Compose()` returns a `MimeMessage` that is passed directly to this method.

**Rationale**:

- `MailDeliveryService` already handles SMTP connection, authentication, and sending via a private `SendEmailAsync(MimeMessage, CancellationToken)` method. The new method simply delegates to this existing infrastructure.
- The existing `SendRecapAsync(string toAddress, byte[] epubContent, string fileName, ...)` constructs the MIME message internally for the Kindle/EPUB channel. It remains unchanged.
- `SendHtmlRecapAsync` accepts a pre-composed `MimeMessage` rather than raw parameters. This separates concerns: `HtmlEmailComposer` owns the message structure; `MailDeliveryService` owns the SMTP transport.
- `DevMailDeliveryService` mirrors the same pattern — `SendHtmlRecapAsync` delegates to its existing `SendEmailAsync` (which uses `SecureSocketOptions.None` and no auth).
- The existing `SendTestEmailAsync(string toAddress, ...)` already sends a plain-text `MimeMessage`. For the `delivery` channel test email, we can either use `SendTestEmailAsync` directly (it sends plain text, which is correct for test emails per FR-009-16) or send via `SendHtmlRecapAsync`. Using `SendTestEmailAsync` with the delivery address is the simpler, more correct approach.

**Alternatives considered**:

- **New `IHtmlMailDeliveryService` interface**: Unnecessary separation. Both EPUB and HTML delivery use the same SMTP transport.
- **Generic `SendAsync(MimeMessage)` method only**: Would require callers to compose messages. The typed methods (`SendRecapAsync` for EPUB, `SendHtmlRecapAsync` for HTML, `SendTestEmailAsync` for tests) provide clearer intent and validation.

---

### 6. Test Email Channel Parameter Design

**Decision**: Extend `POST /settings/test-email` with an optional JSON body `{ "channel": "kindle" | "delivery" | "both" }`. Default behavior (no body or no channel field) auto-detects based on configured emails: send to both if both configured, send to the single configured channel if only one, return 422 if neither.

**Rationale**:

- The existing endpoint is `POST /settings/test-email` with no request body. Adding an optional `channel` field in the body means callers that omit the field get the default auto-detect behavior (test all configured channels).
- Channel enum values (`kindle`, `delivery`, `both`) are explicit and self-documenting.
- Default auto-detection provides the most intuitive behavior for users who just want to "send a test email" without thinking about channels.
- Per FR-009-16, test email for the delivery channel sends a plain-text email (not HTML, not a recap). The existing `SendTestEmailAsync` already sends plain text, so it works for the delivery channel without modification.
- Response format: `{ "results": { "kindle": { "success": true }, "delivery": { "success": true } } }` — per-channel results for `both`; single `{ "message": "..." }` for single-channel.

**Alternatives considered**:

- **Separate endpoint `POST /settings/test-email/delivery`**: Proliferation of endpoints. Single endpoint with parameter is cleaner.
- **Query parameter `?channel=delivery`**: Less RESTful for a POST that triggers a side effect. Request body is more appropriate.

---

### 7. HTML Email Design & Branding

**Decision**: Reuse `Relego.Core.Branding.BrandColors` for the HTML email color scheme. Use a text-based logotype ("Relego") in the header — no image logo (avoids image-blocking issues).

**Rationale**:

- `BrandColors.Light.Accent` (already used by `EpubComposer` cover) provides brand consistency.
- A text-based header (styled with brand color, large font-size) renders reliably across all email clients, even when images are blocked. An image logo would require a `cid:` embedded image attachment, adding complexity.
- The footer includes a plain-text "Sent by Relego" line and a link to the project URL (`https://github.com/Krusty93/relego` per repo metadata).
- Font sizes and spacing chosen for readability on both desktop (~600px wide) and mobile (~320px wide). The table-based layout with `width: 100%` and `max-width: 600px` on the container naturally adapts.
- Highlight text is quoted with a left-border visual treatment (using `<td>` with `border-left: 3px solid {accentColor}`) to visually distinguish highlights from metadata. This works in Outlook (border on `<td>` is supported).

**Alternatives considered**:

- **Embedded image logo (cid:)**: Requires `MimeKit.BodyBuilder.LinkedResources`, adds an attachment to the email, and shows a broken image icon when clients block images. Text-based logotype is more reliable.
- **External image URL**: Blocked by default in most clients. Requires user action to "load images." Rejected.
