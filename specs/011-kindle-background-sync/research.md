# Research: Kindle Background Synchronization

**Feature**: 011-kindle-background-sync | **Spec**: [spec.md](spec.md) | **PRD**: FR-01 in `docs/prds/prd-v1.md`

All Technical Context unknowns are resolved below. Items marked **Open decision** need user
confirmation at review-plan; the plan proceeds on the stated default (plan approved 2026-10-09, defaults stand). Revision 2 (2026-10-09)
adds regions (R9) and the sync frequency (R16). **Revision 3 (2026-10-09)** corrects scheduler
ownership: the browser extension owns routine sync timing with extension alarms (Readwise approach);
the server stores the frequency and accepts extension-initiated syncs; Quartz is used only for the
45-day reminder (R8). Earlier statements that conflict (including revision 2's Quartz sync
schedule) are superseded. **Revision 4 (2026-10-09)** expands the region allowlist (R9) from three to
eight validated regions (US, CA, UK, DE, FR, IT, ES, BR) and records Denmark/Ireland/Poland as not
offered (no dedicated notebook host validated); Japan/China stay excluded. Also (2026-10-09)
`ICloudSyncProvider` moved from `Relego.Core/Sources` to `Relego.Server/Sync/` per user decision
(the CLI never needs it); `Relego.Core` keeps only the shared `Sync*` DTOs.

## R1. How do established products sync Kindle cloud highlights?

**Verified source**: Readwise public docs, "Import from Amazon Kindle"
(`https://docs.readwise.io/readwise/docs/importing-highlights/kindle`), fetched 2026-10-08.

Documented behavior:

- A **browser extension** (Chrome/Firefox) installed in the user's own browser. The user logs in to
  Amazon on Amazon's own sign-in page; the extension then navigates to
  `https://read.amazon.com/notebook` and reads the highlights "available on the screen".
- Readwise states Amazon provides **no API** for highlights, the extension has access only to the
  `read.amazon.com` subdomain, and Readwise never sees the Amazon password (only the email address
  via Amazon authentication).
- Visible **progress indicator**, then a **confirmation page** on completion (spec FR-005 a, b).
- Automatic background re-sync "so long as" the browser is Chrome/Firefox on a computer **and** the
  extension stays installed (spec FR-001, FR-012).
- Browser logged out of the notebook page → dashboard shows an **orange dot** prompting re-login
  (spec FR-005 c).
- **Full Resync** link (`...sync?refresh=true`) (spec FR-005 d, FR-006).
- **Email reminder** when no new Kindle highlights synced in **45 days** (spec FR-005 e, FR-019).
- Duplicates are detected and skipped; users cannot pick books; sideloaded/personal documents are
  **not** in the cloud notebook; publisher "copyright export limit" (typically ~10–20% of a book)
  truncates highlights, with a warning shown on the notebook page (spec FR-008).
- Readwise's own Chrome extension is deliberately unlisted so that account linking precedes Amazon
  authorization.

**Decision**: Copy the approach — a **Relego browser extension** (Chrome and Firefox, Manifest V3)
that runs in the user's own, already-authenticated browser session, reads the Kindle Notebook page,
and **pushes** normalized highlights to the user's Relego server. This is the only approach
consistent with the PRD constraints (no Amazon password form, no headless login, no server-side
Amazon session, no CAPTCHA/MFA bypass, "copy the approach used by established products").

**Alternatives rejected**:

| Alternative | Rejected because |
|-------------|------------------|
| Server-side headless login / stored cookies | Violates FR-003, NFR-07, bypasses CAPTCHA/MFA. |
| Relego-hosted Amazon credential form | Violates FR-002/FR-003. |
| Login with Amazon (OAuth) | Profile scopes do not include Kindle highlights (FR-004); verified by PRD, no highlight scope exists. |
| Undocumented Amazon "Kindle API" / mobile endpoints | Would invent an unsupported Amazon API; forbidden. |
| Bookmarklet only (Bookcision-style) | Not background; per-book button press violates FR-001. Retained only as a *non-goal* reference. |
| Email-forwarding ingestion | Explicitly out of scope (FR-018). |

**Viability caveats (disclosed, not hidden)**: no official Amazon API exists; the page structure is
not a contract and may change (handled by FR-007 contract-break reporting, R6). Regional Notebook
hosts exist; see R9.

## R2. What does "required components" mean, and where does background sync run?

**Decision**: Three components must be running: the **Relego server** (state, dedup, REST, the
Quartz.NET 45-day reminder job), and a **browser with the Relego extension installed and a live
Amazon session**. The server **cannot fetch Kindle data itself** (it holds no Amazon session), so the
split follows the Readwise model — the extension in the user's authenticated browser performs *and
schedules* the sync:

| Concern | Owner |
|---------|-------|
| *When* a routine sync starts (**sync frequency**) | **Extension**, browser alarm `kindle-sync` with `periodInMinutes` = chosen frequency (R16) |
| Choosing/persisting the frequency, region, state, dedup | Server stores and returns; web Sync page edits frequency; extension popup edits region |
| 45-day inactivity reminder | Server, Quartz.NET daily job (R8) — the only Quartz use in this feature |
| *Liveness and command pickup* (**heartbeat**, fixed 60 s) | Extension alarm `kindle-heartbeat`, never derived from sync frequency, never starts a routine sync |
| *Executing* a sync (read notebook, parse, push) | Extension, in the user's authenticated browser |

Two distinct extension alarms exist: `kindle-sync` (variable, = chosen frequency) and
`kindle-heartbeat` (fixed 60 s). The heartbeat reports liveness and the extension's applied
frequency/next due time, and returns any user-requested command (`none` | `sync` | `full_resync`)
plus the stored frequency, which is how a frequency change made on the web reaches the extension.
Honest availability (FR-012, FR-027): the server tracks `last_heartbeat_at`; if the extension has
not reported within a fixed staleness window (5 minutes = 5 missed heartbeats) the UI shows
**"Browser not reporting"** and never claims a sync happened. Missed routine runs while the browser
is away collapse into at most one sync when it returns (R16).

**Alternatives**: Server-owned cadence via Quartz (revision 2, **rejected by the user in revision
3**: a server scheduler cannot execute the sync and diverges from the Readwise approach);
server-driven push into the browser (impossible without a server-held session or an open channel);
native messaging helper (extra install, platform-specific) — rejected under Constitution V.

## R3. Authentication and Amazon-credential boundary

**Decision**: The extension never forwards Amazon cookies, tokens, or page HTML to Relego.
Amazon authentication happens on Amazon's own sign-in page in the user's browser. The extension's
host permissions are limited to **the single notebook host of the user's selected, allowlisted region**
(R9) and the user's Relego server origin (both requested at runtime as optional host permissions;
no region host is granted at install). Only **parsed highlight fields**
(title, author, text, optional note, optional location, optional highlight color) leave the browser,
and only to the user's own server.

Relego↔extension linking uses a **Relego-issued pairing token** (not an Amazon credential), shown
once in the web UI, stored hashed (SHA-256) server-side, stored in extension local storage, sent as
`Authorization: Bearer`. Rationale: gives FR-014 an explicit ownership boundary (token ↔ `user_id`
↔ connection), allows revocation/disconnect without touching library data, and prevents arbitrary
web pages or LAN hosts from impersonating the sync channel, without introducing general
authentication (ADR-004 / Constitution V remain intact: all other endpoints stay unauthenticated).
**Open decision D1**: confirm the pairing token (vs. no token, relying on trusted-LAN assumption).

Server state never holds: Amazon password, cookies, session tokens. Logs redact the bearer token.

## R4. Source registration and import/dedup contracts

Existing contracts reviewed:

- `IHighlightSource` (`Relego.Core/Sources`): **file-based**, `Locate(path)` + `ReadAsync(path)`;
  DI-injected collection resolved by `HighlightSourceResolver`; identity via `SourceDescriptor(Id,
  DisplayName)`; no source enum (ADR-008 §5).
- `SyncRequest`/`SyncResponse` + `SyncRepository.ImportAsync` (`POST /highlights/import`):
  transactional upsert of author → book → highlight with `INSERT OR IGNORE` on unique indexes
  `uq_books_user_author_title` and `uq_highlights_user_book_text`. **This is the canonical dedup
  path** and is reused unchanged for cloud highlights, so retries/full resyncs are idempotent by
  construction (FR-006/FR-007, SC-003).
- Web import: `POST /imports` → `UploadImportService` → same `SyncRepository`.

**Decision**: `IHighlightSource` is *pull-from-file* and does not fit a *push-from-browser*
provider; forcing it would require a fake file path. Add a second, equally open **push-based**
contract **`ICloudSyncProvider`** in `Relego.Server/Sync/` (the CLI never needs cloud sync, so there
is no reason to place it in the shared `Relego.Core` library) that reuses `SourceDescriptor`, declares
`Capabilities`, a versioned **coverage disclosure**, the inactivity threshold (default 45 days),
and validates/normalizes a provider payload into the existing `SyncRequest`. The Kindle cloud
provider is registered with one DI line; no central enum, no edits to `HighlightSourceResolver`
(FR-016). Local `KindleClippingsSource`/`KoboReaderSource` and `POST /imports` are **untouched**
(FR-009, SC-007).

**Open decision / ADR candidate A1**: the new `ICloudSyncProvider` contract + push-based
(extension) acquisition is a significant architectural decision (pairs naturally with FR-08
extension contracts). Ask the user whether to record an ADR; **none is created without approval**.

**Dedup cross-source limitation (disclosed)**: dedup keys are `(user, book(title+author), text)`.
Cloud highlights may be truncated (export limit) or titled/authored slightly differently from
`My Clippings.txt`, so the same highlight imported via both paths can appear twice. Mitigation in
this feature: normalization (trim, collapse whitespace) applied only to the provider payload, a
**provenance** table recording `source_id` + provider `external_id` so cloud re-observations match
by external id *before* text matching (handles reordered/changed metadata), and disclosure text.
Cross-source semantic merge is not attempted (would risk destructive merges; FR-015).

## R5. Non-destructive, idempotent retry and full resync

**Decision**:

- Every observed highlight is `INSERT OR IGNORE`d; nothing is ever deleted or overwritten
  (FR-015). Highlights removed on Amazon remain in Relego.
- **Full resync** = the extension re-reads the entire notebook (it already always reads every
  book, as Readwise states it cannot filter) and bypasses the "unchanged since last sync"
  fast-path (R7); dedup guarantees no duplicates. A resync that finds nothing new is reported as
  `completed_no_changes`.
- Retries use a client-generated **job idempotency key**; the server upserts the job by
  `(user_id, idempotency_key)`, and batches by `(job_id, batch_index)`, so a replayed batch returns
  the stored per-batch result rather than recounting.
- Truncated highlights: stored as observed with a `truncated` flag on provenance; a later
  non-truncated cloud/local version is stored as an additional highlight, never replacing the
  earlier one.
- Unknown metadata stays `NULL` (location, note, color); legacy rows without provenance keep
  working.

## R6. Upstream change detection (actionable failure, never silent success)

**Decision**: The extension parser has a **versioned parse profile** and validates structural
invariants (notebook container present, ≥1 book row when the library list is non-empty, required
fields per highlight). Violations produce a `provider_contract_changed` failure with a stable
`failure_code`, sanitized diagnostics (parser version, failing selector id, counts — **no page
content**), and recovery steps (update the extension, use local import, report an issue). The
server stores the failure and shows it; it never converts it to a successful empty run.
Parser logic is covered by committed HTML fixtures (Constitution IV). Auth expiry is detected
separately: a redirect to Amazon's sign-in host or a sign-in form on the notebook URL →
`auth_expired` (SC-004: surfaced within one sync cycle).

## R7. Initial sync progress, completion, and incremental behavior

**Decision**: Jobs report `phase` (`discovering`, `reading`, `uploading`, `done`), `books_total`,
`books_done`, `highlights_seen`, `highlights_new`. The web UI polls (`GET /sync/kindle-cloud`,
2 s while a job is `running`, 30 s otherwise) — no websockets/SSE (YAGNI, Constitution V).
Because the notebook page cannot be filtered, the extension compares each book's highlight count
and last-annotated date from the library list against the server-provided `known_books`
fingerprint returned at job start and skips unchanged books on routine runs (not on
`full_resync`). Fingerprints are an optimization; correctness rests on dedup. Fingerprints are
scoped to the connection's current region and discarded on region change (R9).
Uploads are chunked (default 200 highlights/batch, server max 1000, request body max 2 MB).

## R8. Inactivity reminder (FR-019, SC-005)

**Decision**: A Quartz.NET job (existing scheduler; Constitution: no custom scheduler) runs daily.
For each active connection, if `now − max(last_new_content_at, connected_at) ≥ 45 days` and no
reminder was already sent for that quiet period, create a `sync_reminders` row. Channels:

1. **In-app banner** on the web UI always (no email configuration needed — Constitution II).
2. **Email** through the existing SMTP/`IMailDeliveryService` to the configured `delivery_email`
   when both exist; absence of email is not an error.

The reminder is cleared and the quiet period resets when new Kindle cloud content syncs.
**Open decision D2**: confirm in-app + optional email (competitor uses email). The reminder only
works while the server is running (NFR-11); a stopped server catches up at next start.
Threshold is a provider-declared constant (45), not user-configurable in this feature.

## R9. Regional Amazon Kindle Notebook hosts (revised 2026-10-09)

**Evidence**: Readwise's Kindle import documentation states most international Amazon domains
work, while **amazon.co.jp and amazon.cn are incompatible**. Direct unauthenticated checks
(2026-10-09) confirmed eight notebook hosts, each redirecting to its own Amazon sign-in
(`amzn_kindle_ynhv2_<cc>`): `read.amazon.com`→amazon.com, `read.amazon.ca`→amazon.ca,
`read.amazon.co.uk`→amazon.co.uk, `lesen.amazon.de`→amazon.de, `lire.amazon.fr`→amazon.fr,
`leggi.amazon.it`→amazon.it, `leer.amazon.es`→amazon.es, `ler.amazon.com.br`→amazon.com.br. The same
checks found **no dedicated host** for Denmark (`amazon.dk` redirects to amazon.de;
`read.amazon.dk`/`laes.amazon.dk` do not resolve), Ireland (`amazon.ie` exists but
`read.amazon.ie` does not resolve) or Poland (`read.amazon.pl`/`czytaj.amazon.pl` do not resolve;
`/notebook` 404s). These confirm host existence and sign-in redirect only; per-region authenticated
parsing is validated with fixtures at implementation (task obligation), not assumed.

**Decision**: A **finite region profile allowlist** lives in the versioned parse profile (the
provider's `ParseProfile` in `Relego.Server/Sync/`, mirrored in the extension's bundled profile and
checked by a parity test):

| Region ID | Name | Notebook host | Sign-in host |
|-----------|------|---------------|--------------|
| `us` | United States | `read.amazon.com` | `www.amazon.com` |
| `ca` | Canada | `read.amazon.ca` | `www.amazon.ca` |
| `uk` | United Kingdom | `read.amazon.co.uk` | `www.amazon.co.uk` |
| `de` | Germany | `lesen.amazon.de` | `www.amazon.de` |
| `fr` | France | `lire.amazon.fr` | `www.amazon.fr` |
| `it` | Italy | `leggi.amazon.it` | `www.amazon.it` |
| `es` | Spain | `leer.amazon.es` | `www.amazon.es` |
| `br` | Brazil | `ler.amazon.com.br` | `www.amazon.com.br` |

- Excluded explicitly: Japan and China (documented incompatible) and Denmark, Ireland and Poland (no dedicated notebook host validated on 2026-10-09). Not offered: any region not yet
  validated; adding one = profile entry + fixtures + parity test, not an architectural change.
- **No arbitrary host entry** anywhere. Pair/update APIs carry a region **ID** only; the server
  validates against the allowlist and derives the host (never trusts a client-supplied host).
  Unknown/excluded IDs → 422 listing supported IDs. **Disclosure (FR-030)**: the web page, extension
  popup and `GET /regions` state that only these validated hosts are supported.
- **The extension popup owns region choice** (host permission and Amazon sign-in are browser-side).
  It requests the optional host permission for the selected region's host only, and releases the
  previous region's permission on change. The web UI shows the region read-only in status.
- **Region change / re-pair**: change applies to the *same* connection and token (no re-pair):
  extension → `PUT region`; server cancels any running job (`interrupted`), stores `region_id`,
  and queues a `full_resync` (fingerprints are region-scoped). The user may need to sign in on the
  new region's Amazon site (`auth_expired` repair flow, no new mechanism). Highlights are never
  deleted. Re-pairing (revoked/lost token) re-sends the region ID with the pair call; the
  connection keeps its history and frequency.
- Cross-region duplicates: a book in two regional catalogs may have different ASIN/title; text dedup
  still applies, provenance is unique per `(source_id, region_id, external_id)`, and the same
  semantic limitation as R4 is disclosed. No semantic cross-region merge (FR-015).
- Sign-in host detection (`auth_expired`) is per-region profile data (sign-in URL patterns).

**Alternatives rejected**: free-text/custom host (SSRF-like/permission-widening, violates FR-021);
region selector in the web UI (host permission cannot be requested from the web page, and the
extension is where sign-in happens); one extension build per region (needless distribution cost).

## R10. Browser extension technology, distribution, and constitution fit

- Chrome and Firefox require JavaScript/TypeScript for extensions; Manifest V3 service worker +
  content script. No .NET option exists in the browser extension runtime. TypeScript is already in
  the repo for `src/relego.web`; the extension is a **new TS project** `src/relego.extension`
  using the same toolchain (Vite, Playwright for tests where applicable, Vitest or the existing
  runner for parser unit tests).
- Constitution "C#/.NET 10 — no mixing of languages or runtimes" is already exercised by
  `relego.web`; extension JS is a browser-mandated runtime for this one component and the server
  stays 100% C#. Recorded under Complexity Tracking; ties to ADR candidate A1.
- Distribution: Readwise ships store-listed (unlisted) extensions. **Open decision D4**: for this
  feature, deliver (a) a build producing signed-ready Chrome/Firefox packages attached to releases
  and loadable unpacked, and (b) store submission as a release-process follow-up outside the spec
  package. The web UI setup guide must state that store review and any browser developer-mode
  steps are prerequisites (FR-017/NFR-09). Fees: Chrome Web Store charges a one-time developer fee
  to *publishers* only; end users pay nothing to Relego; Amazon/Kindle prerequisites are the
  user's own account (disclosed, no invented figures).
- Required permissions are minimal: `storage`, `alarms` (two alarms: variable `kindle-sync` at the
  chosen frequency and fixed 60 s `kindle-heartbeat`; R16), and **optional** host permissions requested at runtime for the
  selected region's notebook host and the user's server origin. No host is granted at install.

## R11. Pairing and setup interaction

**Decision (default)**: Web UI **Connect Kindle cloud** wizard: (1) read disclosures and
prerequisites, (2) create connection → one-time pairing code and server URL shown, (3) install the
extension (link + steps), (4) paste server URL + code into the extension popup **and pick the
Amazon region from the allowlist there**, (5) extension opens that region's notebook page; user
signs in on Amazon's page; (6) the UI shows the selected region, live progress and the
explicit completion state. **Open decision D5**: manual paste vs. a one-click handoff from the web
page (content-script on the Relego origin). Manual paste is the default (fewer permissions).

## R12. Web management surface vs. CLI

**Decision**: Web UI is primary: new **Sync** route (`/app/sync`) containing connection wizard,
status, progress, resync/retry, disconnect, disclosures, and reminders; a status indicator in
`AppShell` (equivalent to Readwise's orange dot); the existing `ImportPage` gains a short pointer
to Kindle cloud sync and keeps the local file upload unchanged. CLI (`relego status`) gets an
**optional, read-only** one-line Kindle cloud status — scheduled as the lowest-priority task and
droppable without affecting the spec (FR-010). UI work follows the `impeccable` skill (AGENTS.md).
**Open decision D6**: confirm nav placement (new top-level "Sync" vs. section on Import).

## R13. Multi-user readiness and ownership

All new tables carry `user_id`; connection, token, jobs, provenance, and reminders are keyed by
`user_id` and resolved via the existing `UserRepository.EnsureUserAsync()` for the implicit
single user. No code assumes one global user (FR-014, NFR-08). Credentials under user control:
token revocation = disconnect.

## R14. Privacy, logging, cost, and disclosure content

- Data path: Amazon (user's browser session) → user's browser extension → user's Relego server. No
  Relego-operated service, no telemetry (FR-013).
- Serilog logs omit tokens, cookies, and highlight text at Information level.
- Disclosures (versioned, served by the provider descriptor, shown at setup and on the Sync page,
  SC-006): cloud-only coverage; sideloaded/personal documents not covered (use My Clippings /
  Kobo import); publisher export limit truncation (typically ~10–20% per Readwise docs, stated as
  "varies by book"); requires a running server and a Chrome/Firefox browser with the extension and
  an active Amazon login; sync pauses while stopped; Amazon may change its page and break sync.
- No fee is charged by Relego; no invented figures.

## R15. Testing strategy (Constitution IV)

- Parser: HTML fixtures of notebook pages (normal, truncated warning, sign-in redirect, changed
  layout, empty library) → expected normalized payloads.
- Server: `RelegoTestApplicationFactory` tests for connection lifecycle, token auth, job/batch
  idempotency, dedup with `SyncRepository`, auth-expired state, contract-break state, heartbeat
  staleness, full-resync command, reminder job (clock abstraction), disclosures endpoint.
- Provider contract (in `Relego.Server/Sync/`): registration + `Normalize` tests (no enum edits).
- Web: Playwright specs for wizard, states, resync, disclosures, a11y.
- Extension: unit tests for heartbeat polling/command execution, region allowlist + permission
  request for the selected host only, and auth detection with a mocked browser API; tests assert
  the `kindle-sync` alarm period equals each of the ten frequencies, is re-created on startup and on
  frequency change, that `kindle-heartbeat` stays fixed at 60 s for all ten, that the heartbeat never
  starts a routine sync, and that overdue/overlapping alarm firings yield at most one sync.
- Server: tests that the stored frequency validates/persists/returns (ten accepted, others 422),
  that the server registers **no** Quartz job/trigger for routine sync (only the reminder job), that
  extension-initiated `scheduled` job starts are accepted and a second concurrent start returns the
  existing job, and that `nextSyncDueAt` is display-only.
- Region: allowlist parity test (Core profile ⇄ extension profile) over the eight regions, 422 for `jp`/`cn`/`dk`/`ie`/`pl`/unknown/host-shaped input.
- Regression: existing Kindle/Kobo source tests and `/imports` tests must stay green.

## R16. Sync frequency: the extension owns routine sync timing (FR-025–FR-029)

**Findings in code**: Quartz is registered in `Program.cs` (`AddQuartz` with persistent SQLite
store + `AddQuartzHostedService`); `SchedulerService` is the established pattern for server-side
jobs. That pattern is reused **only** for the FR-019 reminder (R8). **No Quartz job or trigger is
created for routine sync.**

**Source check (2026-10-09)**: Chrome `chrome.alarms` documentation: alarms may repeat via
`periodInMinutes`; Chrome enforces a 30-second minimum and may delay alarms arbitrarily; alarms do
not wake a sleeping device and, after wake, a missed repeating alarm fires at most once and is
rescheduled from wake time; alarm persistence across browser restarts is not guaranteed across
browsers/older versions, so the extension must verify its alarms exist every time its service
worker starts. Readwise documents the same overall model (extension in the user's browser keeps
syncing automatically while the browser and extension are present). These facts drive the design
below; Firefox parity of the same behavior is verified by extension tests at implementation.

**Decision**:

- **Allowed intervals** (minutes): `15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440`; default `360`.
  One Core constant (`SyncFrequency.AllowedMinutes`) validated server-side, mirrored in the web UI
  and the extension (parity test). All values are ≥ the 30-second browser alarm minimum.
- **Persistence (server)**: `sync_connections.sync_interval_minutes` (CHECK-constrained). The server
  persists and returns it; it does not schedule anything from it.
- **Sync alarm (extension)**: alarm `kindle-sync`, `periodInMinutes` = chosen frequency. Created
  after pairing and region selection; replaced (same name) when the heartbeat reports a stored
  frequency different from the applied one; ensured to exist on every service-worker start and
  browser start (`alarms.get` → recreate if missing).
- **Heartbeat (extension)**: alarm `kindle-heartbeat`, fixed 60 s, independent of frequency. Request
  carries `profileVersion`, `appliedIntervalMinutes`, `nextSyncDueAt` (from the sync alarm's
  `scheduledTime`). Response carries any user-requested command, the region, and the stored
  `intervalMinutes`. This is how a web-side frequency change reaches the browser (≤ 60 s while the
  browser is open) without the server ever scheduling work.
- **Starting a routine sync**: on `kindle-sync` the extension (if not already running one) calls
  `POST /jobs` with `trigger: "scheduled"` and an idempotency key. The server accepts it only for an
  `active` connection with a region; if a job is already running it returns that job (no second
  job); a pending `full_resync`/`sync` command is satisfied by the job rather than duplicated.
- **Coalescing / no backlog**: the extension holds a single-flight lock; a missed repeating alarm
  fires at most once after wake/restart (browser behavior above); on startup the extension runs at
  most one catch-up sync if `lastScheduledAttemptAt + interval` has passed. N missed runs ⇒ ≤ 1 sync.
- **Next run**: display-only `next_sync_due_at`, reported by the extension in the heartbeat and
  returned as `schedule.nextRunAt`; `null` until the extension has reported. `schedule.applied` is
  `true` only when `appliedIntervalMinutes` equals the stored frequency, otherwise the UI says
  "Applies when your browser next reports". If the browser is unavailable the UI shows
  "Browser not reporting" and never "synced".
- **Frequency vs. heartbeat**: sync frequency = how often a sync *starts* (variable, user-chosen);
  heartbeat = how often the extension *reports/asks for commands* (fixed 60 s). Neither replaces the
  other and docs/UI keep them distinct.
- **Reminder**: the existing 45-day reminder remains a separate daily Quartz job (R8).
- **Trade-offs disclosed**: cadence is best-effort (alarm delay, sleeping device, closed browser);
  a frequency change applies after the extension's next heartbeat; the server cannot make a sync
  happen when the browser is away.
- **Rejected**: Quartz-owned routine schedule and server-queued ticks (revision 2, rejected in
  revision 3: the server cannot execute the sync and the Readwise approach is extension-driven);
  deriving the heartbeat from the frequency (breaks liveness/command latency at 24 h); per-frequency
  cron; free-form intervals (FR-025).

## R17. ADR candidates

- **A1** (existing): `ICloudSyncProvider` + push-based extension acquisition + TypeScript extension project.
- **A2** (candidate, consequential): *extension-owned sync cadence with a fixed heartbeat and
  server-persisted frequency, plus the allowlisted regions* — a durable split of responsibility
  between extension and server. Could be folded into A1. **Ask the user; no ADR is created
  without approval, and none has been created.**
