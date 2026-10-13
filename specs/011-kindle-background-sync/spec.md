# Feature Specification: Kindle Background Synchronization

**Feature Branch**: `spec/011-kindle-background-sync`
**Created**: 2026-10-07
**Status**: Revision 4 — **plan approved 2026-10-09**; tasks generated in [tasks.md](tasks.md) (FR-01 base scope, revision 2 additions and revision 3 scheduler ownership approved by the user; revision 4 expands the region allowlist from three to eight validated regions and records Denmark/Ireland/Poland as not offered)
**Input**: User request: "Create FR-01 Kindle background synchronization from PRD v1 with competitor-verified behavior, Amazon-surface authentication only, local fallback preserved, and non-duplicating retry/resync."
**Revision 2 input** (2026-10-09): support multiple Amazon Kindle notebook regions selected by the user in the browser extension (one active region per connection at a time); let users choose the sync frequency from exactly 15 minutes, 30 minutes, 45 minutes, 1 hour, 2 hours, 4 hours, 6 hours, 12 hours, 18 hours, or 24 hours (default 6 hours). Only these additions were authorized; all other approved FR-01 constraints are unchanged.
**Revision 3 input** (2026-10-09, supersedes any conflicting earlier statement, including revision 2's "use Quartz.NET for the sync schedule"): use the same approach as Readwise — a browser extension operating in the user's own authenticated Amazon browser session performs and schedules the sync. Routine sync timing is owned by the browser extension using browser-supported extension alarms at the user-selected frequency. The server persists and returns the selected frequency and accepts extension-initiated syncs, but never creates routine sync schedules. Quartz.NET remains only for the existing server-side 45-day inactivity reminder. The web Sync page stays primary for frequency selection and status; region selection stays in the extension and is shown read-only on the web.
**Revision 4 input** (2026-10-09): the region allowlist is expanded from three to **eight validated regions** — United States, Canada, United Kingdom, Germany, France, Italy, Spain, Brazil. Each was validated on 2026-10-09 by a direct unauthenticated check showing its notebook host redirects to that region's own Amazon sign-in. **Denmark, Ireland and Poland are NOT offered** in this release because no dedicated official Kindle notebook host could be validated for them; Japan and China remain excluded as documented incompatible. No other requirement changed.

**Terminology** (used throughout the package):

- **Sync frequency** (a.k.a. chosen sync cadence): the user-selected interval, one of ten values, that decides how often a *routine sync* starts. Implemented as one extension alarm whose period equals the chosen value.
- **Extension heartbeat**: a fixed 60-second extension wake-up used only for liveness reporting and picking up user-requested commands (Sync now, Full resync, region change). It is **never** derived from, and never changes with, the sync frequency, and it never starts a routine sync by itself.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connect and Automatically Sync Kindle Cloud Highlights (Priority: P1)

A user connects Kindle cloud sync from the web interface, authenticates directly on Amazon's own surface, and gets automatic background imports of eligible Kindle highlights without pressing a button per book.

**Why this priority**: This is the core user value of FR-01: continuous background Kindle ingestion while required components are running.

**Independent Test**: Can be fully tested by completing the connection once, then taking new eligible highlights and verifying they appear automatically without per-book manual sync actions.

**Acceptance Scenarios**:

1. **Given** Kindle cloud sync is not connected, **When** the user starts setup from the web interface, **Then** authentication occurs only on Amazon-controlled surfaces and Relego never displays an Amazon password form.
2. **Given** connection succeeds, **When** required components remain running, **Then** background sync imports newly available Kindle cloud highlights automatically without per-book button presses.
3. **Given** a routine background cycle runs, **When** no new content is available, **Then** the cycle completes with a truthful "no new items" outcome rather than a false success.

---

### User Story 2 - Recover from Expired Authentication and Force Full Resync (Priority: P1)

A user can clearly see when Kindle cloud authentication has expired, re-authenticate, and run a full resync that safely reconciles content without creating duplicates.

**Why this priority**: Trust depends on visible failure states and safe recovery when upstream auth/session state changes.

**Independent Test**: Can be tested by expiring the upstream session, verifying visible repair state, re-authenticating, and running full resync against already imported content.

**Acceptance Scenarios**:

1. **Given** Kindle cloud sync was previously connected, **When** the upstream authenticated session is no longer valid, **Then** Relego shows a visible authentication-expired state with actionable recovery steps.
2. **Given** authentication has expired, **When** the user re-authenticates, **Then** normal automatic background syncing resumes.
3. **Given** previously imported highlights exist, **When** the user triggers full resync or retry, **Then** no duplicate imports are created and any upstream contract break produces an actionable failure state.

---

### User Story 3 - Understand Coverage Limits and Use Local Fallback (Priority: P2)

A user can understand what Kindle cloud sync does and does not cover, sees truncation and coverage disclosures up front, and can still rely on existing local Kindle/device import for unsupported or incomplete cloud cases.

**Why this priority**: Honest disclosure and reliable fallback prevent silent data loss and set correct expectations.

**Independent Test**: Can be tested by syncing a mix of cloud-eligible and sideloaded/personal documents, plus books with export-limit truncation, and validating disclosures and local fallback outcomes.

**Acceptance Scenarios**:

1. **Given** the user reviews Kindle sync setup or status, **When** coverage details are shown, **Then** Relego discloses cloud-only coverage boundaries, sideloaded/personal-document gaps, and publisher export limits that may truncate highlights.
2. **Given** a book's cloud export is truncated or unavailable, **When** the user imports through existing local Kindle/device path, **Then** local import remains available as the reliable fallback path.
3. **Given** no newly synchronized cloud content is detected for an extended period, **When** the inactivity threshold is reached, **Then** Relego sends a reminder to review/reconnect sync status.

### User Story 4 - Choose My Amazon Region in the Browser Extension (Priority: P1)

A user whose Kindle library lives on a regional Amazon site (for example the UK or Germany) picks that region inside the Relego browser extension from a fixed list of supported regions, and sync reads highlights from that region's official Kindle notebook site. The selected region is visible in Relego's sync status.

**Why this priority**: Kindle libraries are bound to the Amazon marketplace the user bought from; a US-only sync would silently fail or show an empty library for many users.

**Independent Test**: Can be tested by selecting each supported region in the extension, confirming the extension only touches that region's official notebook site, and confirming Relego status shows the chosen region; then attempting to supply an unsupported region or a custom address and confirming it is impossible or rejected.

**Acceptance Scenarios**:

1. **Given** the extension is being set up, **When** the user opens the region picker, **Then** only the eight supported regions (United States, Canada, United Kingdom, Germany, France, Italy, Spain, Brazil) are offered, and no free-text host/address entry exists.
2. **Given** the user selects a supported region, **When** sync runs, **Then** highlights are read only from that region's official Kindle notebook site, authentication occurs on that region's Amazon sign-in surface, and the extension asks the browser for access to that site only.
3. **Given** a region is selected, **When** the user views Relego sync status, **Then** the selected region is shown, and the web interface directs region changes to the extension rather than offering a region editor.
4. **Given** a connection already uses one region, **When** the user selects a different supported region in the extension, **Then** the change applies to the same connection without re-pairing, no previously imported highlight is deleted or duplicated, the next sync is a full read of the new region, and the user is told they may need to sign in on the new region's Amazon site.
5. **Given** a region that is known to be incompatible (Japan, China) or lacks a validated dedicated host (Denmark, Ireland, Poland), **When** the user looks for it or a client submits it, **Then** it is not selectable and any submission naming it is rejected with a message listing the supported regions.

---

### User Story 5 - Choose How Often Kindle Sync Runs (Priority: P1)

A user chooses how often routine Kindle sync runs from ten fixed choices in the web interface, sees when the next run is due, and the choice is stored by Relego and applied by the browser extension, surviving server restarts and browser restarts.

**Why this priority**: Different users want near-real-time recaps or low background activity; an adjustable but bounded frequency makes the "automatic" promise controllable and testable.

**Independent Test**: Can be tested by selecting each of the ten frequencies, confirming the stored value is saved and returned, confirming the extension's sync alarm period matches after its next heartbeat, confirming the displayed next run reflects the extension-reported time, restarting the server and the browser and confirming the choice is intact; and by attempting an eleventh value and confirming rejection.

**Acceptance Scenarios**:

1. **Given** the Sync page is open for a connected user, **When** the user opens the frequency selector, **Then** exactly these choices are offered: 15 minutes, 30 minutes, 45 minutes, 1 hour, 2 hours, 4 hours, 6 hours, 12 hours, 18 hours, 24 hours.
2. **Given** the user selects any one of the ten frequencies, **When** the change is saved, **Then** the server stores and returns it immediately, the extension replaces its sync alarm with that period on its next heartbeat, and the next run shown to the user reflects the extension-reported time once applied (until then the page says the extension has not yet applied it).
3. **Given** a frequency has been chosen, **When** the server or the browser restarts, **Then** the chosen frequency is retained without user action and the extension re-creates its sync alarm at that frequency.
4. **Given** the browser was closed or asleep across one or more due runs, **When** the browser becomes available, **Then** the extension runs at most one sync (no backlog), Relego never claims a sync occurred while the browser was away, and the status shows "Browser not reporting".
5. **Given** a value other than the ten choices (for example 5 minutes or 3 hours) is submitted by any client, **When** the server validates it, **Then** it is rejected with a message listing the ten allowed values and the existing frequency is unchanged.
6. **Given** a connection is newly activated, **When** no choice has been made, **Then** a default frequency (6 hours) applies and is shown as the current selection.
7. **Given** a routine sync is due, **When** the extension starts it, **Then** the server accepts it as an extension-initiated sync (idempotent per request) and does not itself create or queue any routine sync schedule.

### Edge Cases

- What happens when the user changes region while a sync is running, or changes the frequency while a sync is running (the running sync is unaffected; the new alarm period applies to the next run)?
- What happens when the extension has not yet applied a newly saved frequency (browser closed)? It applies on the next heartbeat after the browser opens; the UI shows the pending state.
- What happens when the browser restarts and the browser does not persist alarms? The extension re-creates the alarm at the stored frequency on startup.
- What happens when the same book appears in two different regions' libraries (different regional catalogs)?
- What happens when the browser grants no permission for the newly selected region's site?
- What happens when the chosen region's Amazon sign-in surface differs from the one the user is currently signed in to?

- What happens when a user disconnects required components (server, browser session, extension/session bridge) for an extended period and then reconnects?
- What happens when Kindle cloud provides partial data for a title (missing notes, missing locations, truncated snippets)?
- How does the system behave when upstream page/layout/contract changes prevent parsing expected highlight fields?
- What happens when the same highlight is observed in multiple sync cycles with changed metadata ordering but unchanged semantic content?
- How are outcomes reported when one selected source path succeeds (local import) while Kindle cloud sync fails in the same operational window?
- What happens when legacy records lack metadata that newer cloud imports include?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support Kindle highlight import that runs automatically in the background while required components are running, without requiring a button press per book for routine synchronization.
- **FR-002**: The system MUST require users to authenticate directly on Amazon-controlled surfaces for Kindle cloud synchronization.
- **FR-003**: The system MUST NOT present an Amazon password form, store Amazon passwords, store Amazon session cookies/tokens on the server, or perform headless backend login.
- **FR-004**: The system MUST NOT treat Login with Amazon OAuth profile scopes as authorization for Kindle highlights.
- **FR-005**: The Kindle sync user experience MUST match documented established-product behavior by including: (a) visible initial sync progress, (b) explicit completion state, (c) visible authentication-expired state, (d) full resync action, and (e) inactivity reminder after a long period without newly synchronized content.
- **FR-006**: The system MUST provide a full resync operation that reconciles all available Kindle cloud highlights without creating duplicates from previously imported records.
- **FR-007**: The system MUST ensure retries are idempotent for already imported content and MUST surface upstream/provider changes as actionable failures, never silent success.
- **FR-008**: The system MUST disclose provider limits and boundaries, including: cloud coverage vs. locally sideloaded documents, and publisher export limits that can truncate highlights.
- **FR-009**: The system MUST keep existing local Kindle/device import available as a reliable fallback path for content not covered or incompletely represented by Kindle cloud sync.
- **FR-010**: The web UI MUST be the primary management surface for connection status, sync status, expired-auth handling, full resync, and disclosures; CLI interaction for this feature remains optional.
- **FR-011**: The feature MUST operate as one product mode that supports both on-demand sync and continuous background sync; neither mode may require enabling recap delivery.
- **FR-012**: Automatic synchronization MUST run only while required components are available and MUST not claim availability while those components are stopped.
- **FR-013**: The feature MUST preserve local-first handling: no mandatory account, no mandatory telemetry, and no hidden third-party transfer beyond user-configured integrations.
- **FR-014**: Credentials and integration control MUST remain under user control, with explicit ownership boundaries and no assumption of exactly one global user identity in the architecture.
- **FR-015**: Synchronization and reconciliation MUST be non-destructive: no automatic destructive deletion in either direction, unknown metadata remains unknown, and legacy records continue to function.
- **FR-016**: Provider-specific transport/session mechanics MUST stay outside the core reading domain contracts; source behavior MUST integrate through registered-source extension contracts rather than a closed hard-coded enumeration.
- **FR-017**: Privacy and availability disclosures MUST be explicit and honest, including prerequisites, session/browser requirements, provider limits/fees, and downtime expectations.
- **FR-018**: Email-based Kindle ingestion via a dedicated forwarding address is out of scope and MUST NOT be included in this feature.
- **FR-019**: The default inactivity reminder threshold for Kindle cloud sync MUST be 45 days without newly synchronized Kindle cloud content to match documented competitor behavior.
- **FR-020**: The system MUST support a finite, explicitly allowlisted set of Amazon Kindle notebook regions, each mapping to exactly one official Kindle notebook host. The initial allowlist is: United States (`read.amazon.com`), Canada (`read.amazon.ca`), United Kingdom (`read.amazon.co.uk`), Germany (`lesen.amazon.de`), France (`lire.amazon.fr`), Italy (`leggi.amazon.it`), Spain (`leer.amazon.es`), Brazil (`ler.amazon.com.br`). Additional regions MUST NOT be offered until validated.
- **FR-021**: The user MUST select the region inside the browser extension from the allowlist only. Neither the extension nor any API MAY accept an arbitrary host, URL, or free-text region; clients identify a region by its allowlisted ID and the server validates it and derives the host itself. Unknown or excluded region IDs MUST be rejected with an actionable message listing supported regions.
- **FR-022**: Japan (`amazon.co.jp`) and China (`amazon.cn`) MUST be excluded as unsupported because the documented established-product behavior states they are incompatible.
- **FR-023**: Sync MUST read only from the selected region's official notebook host, the extension MUST request browser access only for that selected host, and Amazon authentication MUST occur on that region's own Amazon surface (all FR-002/FR-003 constraints apply unchanged per region).
- **FR-024**: The selected region MUST be shown in Relego sync status. Changing region MUST apply to the existing connection without re-pairing, MUST NOT delete or duplicate previously imported highlights, MUST cancel any in-flight sync as interrupted, and MUST cause the next sync to be a full read of the new region.
- **FR-025**: The user MUST be able to choose the routine sync frequency from exactly ten values: 15 minutes, 30 minutes, 45 minutes, 1 hour, 2 hours, 4 hours, 6 hours, 12 hours, 18 hours, 24 hours. No other value MAY be accepted. The default for a new connection is 6 hours.
- **FR-026**: The browser extension MUST own routine sync timing: it MUST schedule routine syncs with browser-supported extension alarms whose period equals the user's chosen sync frequency, and MUST re-create that alarm whenever the extension starts and whenever the chosen frequency changes. The server MUST persist and return the chosen frequency per connection (default 6 hours) and MUST accept extension-initiated syncs, but MUST NOT create, own, or reconcile any routine sync schedule. Quartz.NET MUST NOT be used to schedule routine sync; it remains only for the existing 45-day inactivity reminder (FR-019).
- **FR-027**: Routine syncs MUST NOT accumulate a backlog: while a sync is running, or after the browser was unavailable across any number of due runs, the extension MUST run at most one sync, and the server MUST reject or return the existing job for a second concurrent sync start. Relego MUST NOT report a sync as having occurred unless the browser actually executed it (FR-012 applies).
- **FR-028**: The web Sync page (primary surface) MUST expose the frequency selector with exactly the ten choices and show the next run as reported by the extension, clearly marked as pending until the extension has applied a newly saved frequency; the web interface MUST show the selected region read-only.
- **FR-029**: The extension heartbeat MUST be a fixed cadence (60 seconds) used only for liveness and for picking up user-requested commands (Sync now, Full resync, region change) and the current stored frequency; it MUST NOT vary with the sync frequency and MUST NOT by itself start a routine sync. Documentation, contracts, and UI wording MUST keep "heartbeat" and "sync frequency" distinct.
- **FR-030**: Region support claims MUST be limited to hosts validated against documented sources; Relego MUST disclose that only the validated allowlisted hosts are supported and that other Amazon regions are unsupported until validated.
- **FR-031**: Denmark, Ireland and Poland MUST NOT be offered in this release: no dedicated official Kindle notebook host could be validated for them, unlike the eight allowlisted regions. They stay out of scope until a dedicated host is validated and added to the allowlist, and any submission naming them MUST be rejected like any other unknown region. (Japan and China remain excluded separately by FR-022.)

### Key Entities *(include if feature involves data)*

- **Kindle Cloud Connection**: User-approved linkage state that enables Kindle cloud synchronization while required components are running.
- **Sync Job**: A single on-demand or background synchronization attempt with start/end timestamps, progress state, and outcome.
- **Sync Status**: User-visible aggregate state (connected, syncing, completed, auth expired, failed, idle, reminder due).
- **Import Record**: Canonical persisted highlight/note entry used for deduplication and idempotent retries/resync.
- **Sync Reminder**: Notification event triggered when no new Kindle cloud content has synchronized for the configured inactivity period.
- **Coverage Disclosure**: Persisted/user-visible informational statement defining cloud eligibility, truncation constraints, and fallback guidance.
- **Failure Report**: Actionable outcome describing upstream contract/auth/data failures with concrete recovery steps.
- **Kindle Region**: A validated, allowlisted entry (ID, display name, official notebook host) the user selects in the extension; excluded and unvalidated regions are not entries.
- **Sync Frequency**: One of the ten allowed intervals stored per connection on the server and applied by the extension as the period of its sync alarm; defines routine cadence.
- **Extension Sync Schedule**: The extension-owned alarm (period = Sync Frequency) and its extension-reported next due time; display-only on the server.
- **Extension Heartbeat**: Fixed-cadence liveness/command pickup report, independent of Sync Frequency.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 95% of users who start Kindle cloud setup complete first sync without per-book actions and see an explicit completion state.
- **SC-002**: 100% of routine sync cycles produce a truthful terminal status (completed with changes, completed with no changes, or actionable failure) with no silent-success outcomes.
- **SC-003**: 100% of full resync and retry operations avoid duplicate imports for previously imported records.
- **SC-004**: 100% of authentication-expired conditions are surfaced as a visible repair state within one sync cycle.
- **SC-005**: At least 90% of users encountering inactivity receive a reminder after 45 days of no newly synchronized Kindle cloud content.
- **SC-006**: 100% of Kindle sync setup/status surfaces include clear disclosure of cloud-vs-sideloaded coverage and publisher truncation limits.
- **SC-007**: Existing local Kindle/device import remains fully usable for unsupported cloud cases in 100% of tested fallback scenarios.
- **SC-008**: 100% of upstream incompatibility events (auth/session invalidation or provider contract changes) produce actionable failure messaging with at least one recovery path.
- **SC-009**: In 100% of tested runs, each allowlisted region's sync contacts only that region's official notebook host, and 100% of attempts to select or submit a host/region outside the allowlist (including Japan, China, Denmark, Ireland, Poland, arbitrary URLs) are rejected or impossible.
- **SC-010**: A user can select a region in the extension and reach the sign-in or first-sync state within 2 minutes in at least 90% of moderated trials, and the selected region is displayed in status in 100% of tested states.
- **SC-011**: 100% of the ten allowed frequencies can be selected and are stored and returned within 5 seconds of saving; when the browser is reporting, the extension's sync alarm period equals the chosen value within 2 heartbeat periods and the displayed next run reflects it; 100% of values outside the ten are rejected with the existing selection unchanged.
- **SC-012**: After a server restart or a browser restart, 100% of connections retain their chosen frequency and the extension's sync alarm is re-created at that frequency without user action.
- **SC-013**: Across a simulated outage of any length, 100% of connections run at most one routine sync on recovery, and zero syncs are reported as completed during the outage.
- **SC-014**: 100% of routine syncs are initiated by the extension; zero routine sync schedules exist on the server, and Quartz.NET jobs on the server are limited to the inactivity reminder.

## Assumptions

- The competitor reference for FR-01 experience is documented Kindle synchronization behavior from established products (notably Readwise documentation).
- "Long period" for inactivity reminder is standardized to 45 days to mirror documented competitor behavior and can be revisited in later features if needed.
- Kindle cloud coverage includes eligible Amazon-synced highlights but excludes many personal/sideloaded-document cases unless imported through existing local/device methods.
- Publisher export limits can truncate cloud-visible highlights and are treated as an external constraint, not a Relego defect.
- Background synchronization availability depends on required components being active; this feature does not promise sync while all components are stopped.
- This feature does not expand into other PRD V1 functional areas beyond FR-01.
- Regions: Readwise documentation states most international Amazon domains work and that amazon.co.jp and amazon.cn are incompatible. Direct unauthenticated checks on 2026-10-09 confirmed eight notebook hosts each redirect to their own Amazon sign-in: `read.amazon.com`, `read.amazon.ca`, `read.amazon.co.uk`, `lesen.amazon.de`, `lire.amazon.fr`, `leggi.amazon.it`, `leer.amazon.es`, `ler.amazon.com.br`. Further regions are added only after validation, as an allowlist change.
- A user's Kindle library belongs to one Amazon region at a time per connection; using several regions at once is not part of this feature.
- The region is chosen in the browser extension because browser host-access permission and the Amazon sign-in are browser-side; the web interface only displays it.
- Routine cadence is a best-effort target: it requires the browser to be open and the extension installed, and browser alarms may fire late (the browser documents that alarms may be delayed, do not wake a sleeping device, and fire missed repeating alarms at most once on wake). Actual sync time can lag the due time. Manual sync/full resync remain available at any time.
- Following the documented Readwise approach, the extension in the user's own authenticated browser session performs and schedules the sync; the server is a state/persistence/dedup/reminder service and does not initiate Kindle reads.
- Region validation status: eight regions are validated and supported — United States (`read.amazon.com`), Canada (`read.amazon.ca`), United Kingdom (`read.amazon.co.uk`), Germany (`lesen.amazon.de`), France (`lire.amazon.fr`), Italy (`leggi.amazon.it`), Spain (`leer.amazon.es`), Brazil (`ler.amazon.com.br`). Validation sources: Readwise documentation (international domains work; Japan/China do not) and direct unauthenticated checks on 2026-10-09 showing each host redirects to its own Amazon sign-in. Denmark, Ireland and Poland are excluded because no dedicated host could be validated (`read.amazon.pl`, `czytaj.amazon.pl`, `read.amazon.dk`, `read.amazon.ie` do not resolve; `amazon.dk` redirects to amazon.de; `amazon.ie` has no notebook host). Authenticated per-region parsing is verified with fixtures at implementation. Relego discloses that only these allowlisted hosts are supported.

## Out of Scope

- Email-based Kindle ingestion via dedicated forwarding address.
- Any implementation that asks for Amazon credentials directly in Relego UI, stores Amazon passwords/sessions on the server, or performs backend headless login.
- New product capabilities from FR-02 through FR-09.
- Any claim that sideloaded/personal-document highlights are fully covered by Kindle cloud sync alone.
- Arbitrary or user-typed Amazon hosts; Japan and China (documented incompatible); and Denmark, Ireland and Poland (no dedicated official notebook host validated — not offered in this release).
- Free-form sync intervals, per-book schedules, or any frequency outside the ten allowed values.
- Server-created or server-driven routine sync schedules (including Quartz.NET) and any server push into the browser.
- Syncing multiple Amazon regions simultaneously for one connection.
