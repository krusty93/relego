# Implementation Plan: Email Delivery

**Branch**: `009-email-delivery` | **Date**: 2026-06-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/009-email-delivery/spec.md`

## Summary

Add an optional regular email delivery channel so users can receive recaps without owning a Kindle device. The Kindle delivery channel remains but becomes optional. Recaps are delivered as inline HTML email (multipart/alternative MIME) to the `delivery_email` address. When both channels are configured, each runs independently with isolated error handling.

Technical approach: Add `delivery_email` column to `users` table via auto-migration on server startup. Introduce `HtmlEmailComposer` service for composing branded, responsive HTML emails using MimeKit. Refactor `RecapService.ExecuteAsync()` to iterate over active channels (Kindle → EPUB, Email → HTML) with independent SMTP connections and error isolation. Extend `PATCH /settings` / `GET /settings` for `delivery_email`, extend `POST /settings/test-email` with channel parameter, and add `delivery-email` CLI command + TUI field.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0` TFM)
**Primary Dependencies**: MimeKit 4.x + MailKit 4.x (already in project via `Relego.Server`), Dapper (already used for data access), Spectre.Console.Cli (CLI), Terminal.Gui v2 (TUI)
**Storage**: SQLite at `/data/relego.db` — auto-migration adds `delivery_email TEXT NULL` to `users` table on server startup
**Testing**: xUnit (existing `Relego.Tests` project) — unit tests for `HtmlEmailComposer`, `RecapService` dual-channel logic, endpoint validation, CLI command validation
**Target Platform**: Cross-platform server (Docker Linux container for `relego-server`), cross-platform CLI (Windows, macOS, Linux for `relego`)
**Project Type**: Server-side service + contract extensions + CLI/TUI surface updates
**Performance Goals**: HTML email composition < 500ms for up to 15 highlights. Dual-channel delivery adds no more than one additional SMTP connection per recap (FR-009-09). Existing single-channel (Kindle-only) performance unchanged.
**Constraints**: No new NuGet packages required — MimeKit already handles multipart/alternative MIME and HTML body composition. No new .NET projects. No auth changes. Backward-compatible: existing Kindle-only users experience zero behavior change (FR-009-07). Zero-config migration on server startup (FR-009-02).
**Scale/Scope**: Single-user MVP. One new column, one new service (`HtmlEmailComposer`), modifications to 3 existing services, 1 endpoint extended, 1 contract extended, 2 new CLI commands, TUI field additions.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Client/Server Separation | **PASS** | Server handles composition + delivery; CLI/TUI sends settings. No boundary crossing |
| II. CLI-First, No GUI | **PASS** | `delivery-email` exposed via CLI `config` command; TUI is terminal-based |
| III. Zero-Config Onboarding | **PASS** | `delivery_email` defaults to NULL; existing users unaffected; new users need only set one email |
| IV. Local Processing Only | **PASS** | HTML composition is local; SMTP is user-configured. No third-party email SaaS |
| V. Tests Ship with Code | **PASS** | Unit tests for HtmlEmailComposer, RecapService dual-channel, endpoint validation included |
| VI. Simplicity / YAGNI | **PASS** | No new projects, no new NuGet packages, no template engines (inline HTML string builder), no channel abstraction beyond what's needed for two concrete channels |
| Tech: C# / .NET 10 only | **PASS** | All new code is C# |
| Tech: MailKit + SMTP | **PASS** | MimeKit already used; `BodyBuilder` for multipart/alternative composition |
| Tech: REST HTTP + JSON | **PASS** | Contract changes are JSON-serializable record properties |
| Tech: Docker distribution | **PASS** | Server runs in Docker; migration auto-applies on startup |
| Exclusion: No web UI | **PASS** | No web UI changes |
| Exclusion: No auth for MVP | **PASS** | No authentication added |

**Post-design re-check**: (to be filled after Phase 1 design)

## Project Structure

### Documentation (this feature)

```text
specs/009-email-delivery/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technology decisions and rationale
├── data-model.md        # Entity definitions and DB schema changes
├── quickstart.md        # Developer quick-start guide
├── contracts/
│   └── api.md           # API contract changes (Settings endpoints, test-email)
└── tasks.md             # Phase-by-phase task list (created by /speckit.tasks)
```

### Source Code Changes

```text
src/Relego.Core/Contracts/
├── SettingsResponse.cs          ← MODIFIED: add DeliveryEmail property
├── UpdateSettingsRequest.cs     ← MODIFIED: add DeliveryEmail property
└── StatusResponse.cs            ← MODIFIED: add DeliveryEmailConfigured property

src/Relego.Server/
├── Models/
│   └── User.cs                  ← MODIFIED: add DeliveryEmail property
├── Data/
│   └── UserRepository.cs        ← MODIFIED: add DeliveryEmail to queries, add UpdateDeliveryEmailAsync
├── Infrastructure/Database/
│   └── SchemaBootstrap.cs       ← MODIFIED: add ALTER TABLE migration for delivery_email
├── Services/
│   ├── HtmlEmailComposer.cs     ← NEW: composes multipart/alternative HTML+plain-text MIME email
│   ├── IMailDeliveryService.cs  ← MODIFIED: add SendHtmlRecapAsync method
│   ├── MailDeliveryService.cs   ← MODIFIED: implement SendHtmlRecapAsync
│   ├── DevMailDeliveryService.cs← MODIFIED: implement SendHtmlRecapAsync
│   └── RecapService.cs          ← MODIFIED: dual-channel delivery logic with error isolation
├── Endpoints/
│   ├── SettingsEndpoints.cs     ← MODIFIED: handle delivery_email in PATCH/GET, extend test-email
│   └── StatusEndpoints.cs       ← MODIFIED: expose DeliveryEmailConfigured

src/Relego.Cli/
├── Commands/
│   ├── Config/
│   │   ├── ConfigDeliveryEmailCommand.cs ← NEW: CLI `config delivery-email` command
│   │   └── ConfigShowCommand.cs          ← MODIFIED: display delivery-email row
│   └── StatusCommand.cs                  ← MODIFIED: display delivery email status
├── Tui/
│   ├── SettingsScreen.cs        ← MODIFIED: add "Delivery email" field
│   └── StatusChrome.cs          ← MODIFIED: update warning logic (check both emails)
└── Program.cs                   ← MODIFIED: register ConfigDeliveryEmailCommand

src/Relego.Tests/
├── Services/
│   ├── HtmlEmailComposerTests.cs     ← NEW: HTML structure, multipart MIME, plain-text fallback
│   └── RecapServiceDualChannelTests.cs ← NEW: dual-channel delivery, error isolation, skip logic
├── Api/
│   └── SettingsDeliveryEmailTests.cs ← NEW: PATCH/GET delivery_email, test-email channel param
└── Cli/
    └── ConfigDeliveryEmailTests.cs   ← NEW: CLI validation and server integration
```

**Structure Decision**: No new .NET projects. `HtmlEmailComposer` lives in `Relego.Server/Services/` alongside `EpubComposer` (both are stateless composers). Contract changes are additive properties on existing records. CLI gets a new `ConfigDeliveryEmailCommand` in the existing `Commands/Config/` folder, registered alongside `ConfigKindleEmailCommand`. TUI changes are minimal field additions to existing screens.

## Complexity Tracking

No constitution violations. No complexity justification needed.

---

## Phase 1: Data Layer (DB Migration, Models, Contracts)

**Purpose**: Add `delivery_email` column to the database, update the `User` model and repository, and extend the shared contracts (`SettingsResponse`, `UpdateSettingsRequest`, `StatusResponse`). After this phase, the database supports `delivery_email` storage, the server reads/writes it, and the API contracts are ready for consumption.

### Database Migration

- [ ] T001 Modify `src/Relego.Server/Infrastructure/Database/SchemaBootstrap.cs`: add `ALTER TABLE users ADD COLUMN delivery_email TEXT NULL` to the migration SQL. Use a separate migration step that runs after the main schema creation, guarded by a column-existence check (try-catch or `PRAGMA table_info`). The `CREATE TABLE IF NOT EXISTS users` statement should also include `delivery_email TEXT NULL` for fresh installs.

### Model Updates

- [ ] T002 Modify `src/Relego.Server/Models/User.cs`: add `public string? DeliveryEmail { get; set; }` property (nullable, defaults to null).

### Repository Updates

- [ ] T003 Modify `src/Relego.Server/Data/UserRepository.cs`:
  - Update `UserRow` to include `DeliveryEmail` column.
  - Update `GetByIdAsync` SQL to select `delivery_email AS DeliveryEmail`.
  - Update `EnsureUserAsync` INSERT to include `delivery_email` column with NULL default.
  - Add `UpdateDeliveryEmailAsync(int userId, string? deliveryEmail)` method — sets `delivery_email` (NULL for empty string).
  - Update `UpdateKindleEmailAsync` to use a consistent pattern with the new method.

### Contract Updates

- [ ] T004 Modify `src/Relego.Core/Contracts/SettingsResponse.cs`: add `public string? DeliveryEmail { get; set; }` property (nullable, null when not configured).
- [ ] T005 Modify `src/Relego.Core/Contracts/UpdateSettingsRequest.cs`: add `public string? DeliveryEmail { get; set; }` property (nullable; null means "don't change", empty string means "clear").
- [ ] T006 Modify `src/Relego.Core/Contracts/StatusResponse.cs`: add `public bool DeliveryEmailConfigured { get; set; }` property.

### Tests

- [ ] T007 Write `src/Relego.Tests/Api/SettingsDeliveryEmailTests.cs`: test that `GET /settings` returns `deliveryEmail` field (null when not set, value when set). Test that `PATCH /settings` with `deliveryEmail` persists correctly. Test that empty string clears the field.

**Checkpoint**: `GET /settings` returns `deliveryEmail`. `PATCH /settings` accepts and persists `deliveryEmail`. Database migration runs on startup without errors for fresh and existing databases. Tests pass.

---

## Phase 2: Service Layer (HtmlEmailComposer, RecapService Refactor, MailDeliveryService)

**Purpose**: Build the HTML email composer, refactor `RecapService.ExecuteAsync()` for dual-channel delivery with error isolation, and extend `IMailDeliveryService`/`MailDeliveryService`/`DevMailDeliveryService` to support HTML recap delivery.

### HtmlEmailComposer

- [ ] T008 Create `src/Relego.Server/Services/HtmlEmailComposer.cs`: static class with method `static MimeMessage Compose(IReadOnlyList<SelectionCandidate> highlights, DateTimeOffset recapDate, string cadence, string toAddress, string fromAddress)`. Produces a `MimeMessage` with:
  - `multipart/alternative` body containing `text/html` and `text/plain` parts.
  - HTML part: inline CSS, responsive meta viewport, branded header (Relego name/logotype), recap date, highlights grouped by book with title/author/text, footer with "Sent by Relego" and project link.
  - Plain-text part: text-only formatting with book/author/highlight separation using whitespace and dashes.
  - Uses MimeKit's `BodyBuilder` class for multipart construction.
  - Email-safe CSS: tables for layout (no CSS Grid/Flexbox as primary), inline styles, `max-width: 600px`, system fonts.
- [ ] T009 Write `src/Relego.Tests/Services/HtmlEmailComposerTests.cs`: test HTML structure contains required elements (brand header, recap date, book grouping, footer), plain-text part is non-empty, MIME structure is multipart/alternative, empty highlight list produces graceful message, Unicode characters are preserved.

### MailDeliveryService Extensions

- [ ] T010 Modify `src/Relego.Server/Services/IMailDeliveryService.cs`: add `Task SendHtmlRecapAsync(MimeMessage message, CancellationToken cancellationToken = default)` method. The method accepts a pre-composed `MimeMessage` (from `HtmlEmailComposer.Compose()`) and sends it via SMTP.
- [ ] T011 Modify `src/Relego.Server/Services/MailDeliveryService.cs`: implement `SendHtmlRecapAsync` — sends the provided `MimeMessage` via SMTP using the existing `SendEmailAsync` private method.
- [ ] T012 Modify `src/Relego.Server/Services/DevMailDeliveryService.cs`: implement `SendHtmlRecapAsync` — sends the provided `MimeMessage` via SMTP using the existing `SendEmailAsync` private method (no TLS, no auth for dev).

### RecapService Dual-Channel Refactor

- [ ] T013 Modify `src/Relego.Server/Services/RecapService.cs`: refactor `ExecuteAsync` for dual-channel delivery:
  1. Fetch user. Check both `KindleEmail` and `DeliveryEmail`. If NEITHER is set, log warning "No delivery channel configured" and mark job failed — return early (no SMTP attempted).
  2. Compose EPUB once if `KindleEmail` is set (existing `EpubComposer.Compose()`).
  3. Compose HTML MimeMessage once if `DeliveryEmail` is set (new `HtmlEmailComposer.Compose()`).
  4. Launch independent delivery tasks:
     - If Kindle email configured: send EPUB via `_mailDeliveryService.SendRecapAsync()` with retry policy. Catch and log failures independently.
     - If delivery email configured: send HTML via `_mailDeliveryService.SendHtmlRecapAsync()` with a separate retry (new `AsyncRetryPolicy` instance or reusing existing policy with independent execution). Catch and log failures independently.
  5. Only mark highlights as seen and job as delivered if AT LEAST ONE channel succeeded. If both failed, mark job failed.
  6. Log per-channel outcomes: `"Kindle delivery: success|failed"`, `"Email delivery: success|failed"`.
- [ ] T014 Write `src/Relego.Tests/Services/RecapServiceDualChannelTests.cs`: test dual-channel scenarios: both emails set → both channels attempted, only Kindle set → only EPUB sent, only Delivery set → only HTML sent, neither set → skip with warning, Kindle fails + Email succeeds → job marked delivered, Email fails + Kindle succeeds → job marked delivered, both fail → job marked failed. Use mocked `IMailDeliveryService`.

**Checkpoint**: Recap execution with both channels configured sends EPUB to Kindle address and HTML to delivery address. Failure in one channel does not block the other. Single-channel (Kindle-only or Email-only) works. No emails configured → skip with warning. Tests pass.

---

## Phase 3: API Layer (SettingsEndpoints, StatusEndpoints, Test-Email)

**Purpose**: Update `PATCH /settings` and `GET /settings` to handle `delivery_email`, extend `POST /settings/test-email` with channel parameter, and update `GET /status` to expose delivery email configuration status.

### Settings Endpoints

- [ ] T015 Modify `src/Relego.Server/Endpoints/SettingsEndpoints.cs`:
  - `GET /settings`: include `DeliveryEmail = user.DeliveryEmail` in `ToSettingsResponse()`.
  - `PATCH /settings`: validate `request.DeliveryEmail` — if not null, validate email format (empty string = clear, valid email = set, invalid = 422). Apply via `user.DeliveryEmail = normalizedDeliveryEmail ?? user.DeliveryEmail`. Call `await userRepo.UpdateDeliveryEmailAsync(user.Id, user.DeliveryEmail)` after `UpdateKindleEmailAsync`.
  - Update `ApplySettingsUpdate` signature to accept `normalizedDeliveryEmail`.
- [ ] T016 Modify `src/Relego.Server/Endpoints/SettingsEndpoints.cs` — `POST /settings/test-email`:
  - Accept an optional JSON body with `channel` field: `"kindle"`, `"delivery"`, or `"both"` (default: auto-detect — if both configured, send to both; if only one, send to that one).
  - When `channel` is `"kindle"`: send test email to `kindle_email` via existing `SendTestEmailAsync`.
  - When `channel` is `"delivery"`: compose a plain-text test `MimeMessage` and send via `SendHtmlRecapAsync` (which handles plain MimeMessage too) — or add a dedicated `SendTestEmailAsync` overload.
  - When `channel` is `"both"`: send independently to both addresses; return success if at least one succeeds.
  - Validation: if specified channel's email is not configured, return 422 with actionable error.
  - Return JSON response with per-channel results.

### Status Endpoints

- [ ] T017 Modify `src/Relego.Server/Endpoints/StatusEndpoints.cs`: set `status.DeliveryEmailConfigured = !string.IsNullOrWhiteSpace(user.DeliveryEmail)`.

### Tests

- [ ] T018 Write tests in `src/Relego.Tests/Api/SettingsDeliveryEmailTests.cs` (extend existing file):
  - `PATCH /settings` with invalid `deliveryEmail` → 422.
  - `PATCH /settings` with empty string `deliveryEmail` → clears value.
  - `POST /settings/test-email` with `channel=delivery` → sends to delivery email.
  - `POST /settings/test-email` with `channel=both` → sends to both.
  - `POST /settings/test-email` with unconfigured channel → 422.

**Checkpoint**: `PATCH /settings` and `GET /settings` support `delivery_email`. `POST /settings/test-email` supports channel selection. `GET /status` returns `deliveryEmailConfigured`. Tests pass.

---

## Phase 4: CLI/TUI Layer

**Purpose**: Add `delivery-email` CLI command, update `config show` and `status` commands, add "Delivery email" field to TUI settings screen, and update TUI warning logic.

### CLI Commands

- [ ] T019 Create `src/Relego.Cli/Commands/Config/ConfigDeliveryEmailCommand.cs`: Spectre.Console.Cli command `config delivery-email <address>`. Validates email format (same regex as `ConfigKindleEmailCommand`). Sends `PATCH /settings` with `{ "deliveryEmail": address }`. Displays success/error with Spectre.Console markup. Accepts empty string to clear.
- [ ] T020 Modify `src/Relego.Cli/Program.cs`: register `ConfigDeliveryEmailCommand` in the `config` branch alongside `ConfigKindleEmailCommand`.
- [ ] T021 Modify `src/Relego.Cli/Commands/Config/ConfigShowCommand.cs`: add `table.AddRow("Delivery Email", response.DeliveryEmail ?? "[grey](not set)[/]");` row.
- [ ] T022 Modify `src/Relego.Cli/Commands/StatusCommand.cs`: add `table.AddRow("Delivery Email", FormatDeliveryEmail(response.DeliveryEmailConfigured));` row. Add helper `FormatDeliveryEmail(bool configured)`.

### TUI Updates

- [ ] T023 Modify `src/Relego.Cli/Tui/SettingsScreen.cs`: add a `SettingsField("Delivery Email", _settings.DeliveryEmail ?? "", "deliveryEmail", FieldKind.Editable, ...)` to the fields list, after the "Kindle Email" field. Wire up save logic to include `deliveryEmail` in the `UpdateSettingsRequest`.
- [ ] T024 Modify `src/Relego.Cli/Tui/StatusChrome.cs`: update `RefreshAsync` — after fetching status, set a new `AnyEmailConfigured` property (`KindleEmailConfigured || DeliveryEmailConfigured`). Update `UpdateLabels`:
  - Change warning condition from `!KindleEmailConfigured` to `!AnyEmailConfigured`.
  - Change warning text to `"⚠ No delivery email configured — recaps cannot be delivered"`.
  - Add `DeliveryEmailConfigured` property alongside `KindleEmailConfigured`.

### Tests

- [ ] T025 Write `src/Relego.Tests/Cli/ConfigDeliveryEmailTests.cs`: test email validation (valid, invalid, empty), server integration (mock `RelegoHttpClient`), success/error output messages.

**Checkpoint**: `relego config delivery-email user@example.com` sets the delivery email. `relego config show` displays both emails. `relego status` shows delivery email status. TUI settings page has "Delivery email" field. TUI warning shows only when NEITHER email is configured. Tests pass.

---

## Phase 5: Integration Testing & Polish

**Purpose**: End-to-end verification, edge case handling, and documentation polish.

- [ ] T026 Manual E2E test with smtp4dev: configure both `kindle_email` and `delivery_email`, trigger recap via `POST /recaps`, verify two emails arrive in smtp4dev UI — one with EPUB attachment, one with HTML body.
- [ ] T027 Verify edge cases: empty recap (no highlights) → no email sent to either channel; both channels unconfigured → skip with logged warning; SMTP failure in one channel → other succeeds; Unicode highlights rendered correctly in HTML and plain-text parts.
- [ ] T028 Verify HTML email rendering in Gmail, Outlook, Apple Mail (manual or via screenshot comparison). Check responsive layout at 320px width.
- [ ] T029 Verify backward compatibility: existing database (no `delivery_email` column) auto-migrates on startup; existing `kindle_email` value preserved; Kindle-only delivery unchanged.
- [ ] T030 Update `docs/ARCHITECTURE.md` with new `HtmlEmailComposer` service and dual-channel delivery flow (Mermaid diagram).

**Checkpoint**: Feature complete. All acceptance scenarios from spec.md validated. Backward compatibility confirmed.

