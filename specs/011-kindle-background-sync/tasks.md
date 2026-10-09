---

description: "Task list for feature 011 Kindle Background Synchronization"
---

# Tasks: Kindle Background Synchronization

**Input**: Design documents from `/specs/011-kindle-background-sync/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md) (revision 4), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution IV ("Tests Ship with the Code"), the plan's Testing section, research R15 and AGENTS.md (tests for API endpoints, parsers and other behavior-heavy changes) all require tests in the same PR. Test tasks are therefore included per user story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4, US5)
- Include exact file paths in descriptions

## Path Conventions

- **Server / Core / CLI / Tests**: `src/Relego.Server/`, `src/Relego.Core/`, `src/Relego.Cli/`, `src/Relego.Tests/`
- **Web UI**: `src/relego.web/`
- **Browser extension**: `src/relego.extension/` (new TypeScript project, outside `Relego.slnx`, like `src/relego.web`)
- **Living docs**: `docs/`
- `ICloudSyncProvider` and its supporting types live in `src/Relego.Server/Sync/` (not Core); `src/Relego.Core/Contracts/` holds only the shared `Sync*` DTOs.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new browser-extension project and wire it into build/CI.

- [X] T001 Create `src/relego.extension` TypeScript project: `src/relego.extension/manifest.json` (Manifest V3, Chrome + Firefox; permissions `storage` and `alarms`; `host_permissions` empty at install; no `cookies`/`webRequest`/`<all_urls>`), `package.json`, `tsconfig.json`, and Vite + Vitest config per plan
- [X] T002 [P] Scaffold extension modules `src/relego.extension/src/{background,notebook-parser,client,profile}.ts` and `src/relego.extension/tests/` with the test runner wired up
- [X] T003 [P] Add the extension to CI in `.github/workflows/ci.yaml`: `npm ci` + typecheck + unit tests (mirrors the existing `relego.web` job)

**Checkpoint**: Extension project builds and runs an empty test suite in CI.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, provider contract, core services and shells that MUST exist before ANY user story can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Add the five new tables to `src/Relego.Server/Infrastructure/Database/SchemaBootstrap.cs` (idempotent `CREATE TABLE IF NOT EXISTS`; **no existing table altered**; every table carries `user_id`): `sync_connections`, `sync_jobs`, `sync_job_batches`, `highlight_provenance`, `sync_reminders`, with the exact columns and constraints in `data-model.md` — including `sync_connections` `UNIQUE(user_id, provider_id) WHERE state <> 'disconnected'`, `sync_jobs` `UNIQUE(user_id, idempotency_key)`, `sync_job_batches` `PRIMARY KEY(job_id, batch_index)`, `highlight_provenance` `UNIQUE(source_id, region_id, external_id) WHERE external_id IS NOT NULL`, and `sync_reminders` `UNIQUE(connection_id, quiet_since)`
- [X] T005 [P] Implement `SyncConnectionRepository` in `src/Relego.Server/Data/SyncConnectionRepository.cs` (create/get/update state, region, frequency, `pending_command` (`sync` never overwrites `full_resync`), heartbeat/`last_new_content_at`, disconnect)
- [X] T006 [P] Implement `SyncJobRepository` in `src/Relego.Server/Data/SyncJobRepository.cs` (upsert by `(user_id, idempotency_key)`, single running job per connection, batch replay records, job state machine from `data-model.md`)
- [X] T007 [P] Implement `ProvenanceRepository` in `src/Relego.Server/Data/ProvenanceRepository.cs` (insert-only provenance; match re-observed highlights by `(source_id, region_id, external_id)` before text dedup)
- [X] T008 [P] Add the shared `Sync*` request/response DTOs to `src/Relego.Core/Contracts/` used across the REST contract and by the CLI (`SyncProviderSummary`, `SyncScheduleDto`, `CloudSyncStatusDto` and the additive `kindleCloud` summary on `StatusResponse`); no cloud-provider logic in Core
- [X] T009 Define `ICloudSyncProvider` and its supporting types in `src/Relego.Server/Sync/ICloudSyncProvider.cs` (`CloudSyncCapabilities`, `CoverageDisclosure`, `DisclosureItem`, `ParseProfile`, `KindleRegion`, `CloudSyncBatch`/`CloudSyncBook`/`CloudSyncHighlight`, `ProviderNormalizationResult`, `SyncFrequencyPolicy`) per `contracts/cloud-sync-provider.md`
- [X] T010 [P] Add the region allowlist data to `src/Relego.Server/Sync/ParseProfile.cs`: the eight validated regions with hosts and sign-in patterns — `us`/United States/`read.amazon.com`/`www.amazon.com`, `ca`/Canada/`read.amazon.ca`/`www.amazon.ca`, `uk`/United Kingdom/`read.amazon.co.uk`/`www.amazon.co.uk`, `de`/Germany/`lesen.amazon.de`/`www.amazon.de`, `fr`/France/`lire.amazon.fr`/`www.amazon.fr`, `it`/Italy/`leggi.amazon.it`/`www.amazon.it`, `es`/Spain/`leer.amazon.es`/`www.amazon.es`, `br`/Brazil/`ler.amazon.com.br`/`www.amazon.com.br`; no entry for `jp`/`cn`/`dk`/`ie`/`pl`
- [X] T011 Implement `KindleCloudProvider` in `src/Relego.Server/Sync/KindleCloudProvider.cs` (`SourceDescriptor` id `kindle-cloud`, capabilities `PushIngest`/`FullResync`/`ProgressReporting`, `InactivityReminderDays = 45`, disclosure version `1`, `ParseProfile`, `SyncFrequencyPolicy` with `AllowedMinutes = {15,30,45,60,120,240,360,720,1080,1440}` and `Default = 360`)
- [X] T012 [P] Implement `KindleCloudProvider.Normalize` in `src/Relego.Server/Sync/KindleCloudProvider.cs` returning validation errors (never throws, never performs I/O) with the constraints from `data-model.md`: highlight `text` trimmed 1–20,000 chars, book `title` 1–1,000, author ≤ 500, batch ≤ 1,000 highlights and request ≤ 2 MB, `batch_index` ≥ 0 and unique per job; unknown/optional fields persisted as NULL
- [X] T013 Register the provider and expose it: `services.AddSingleton<ICloudSyncProvider, KindleCloudProvider>()` in `src/Relego.Server/Program.cs` and the `GET /sync/providers` endpoint in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`, resolved via `IEnumerable<ICloudSyncProvider>` with `{providerId}` routing and **no** switch on `Id` (no enum); unknown ids return 404 listing valid ids
- [X] T014 Implement `SyncService` in `src/Relego.Server/Sync/SyncService.cs`: start job (single-flight — a second concurrent start returns the running job), idempotent batches per `batchIndex`, `progress`/`complete`/`fail`, and reuse of `SyncRepository.ImportAsync` so dedup is shared and inserts are never destructive
- [X] T015 Implement `SyncTokenService` in `src/Relego.Server/Sync/SyncTokenService.cs`: generate a one-time pairing token, store only its SHA-256 hash, verify `Authorization: Bearer` for the extension channel, revoke on disconnect; never log the token
- [X] T016 Implement status derivation in `src/Relego.Server/Sync/SyncStatusDerivation.cs`: `not_connected` → `awaiting_pairing` → `connected` → `syncing` with attention states `completed`/`auth_expired`/`failed`/`browser_not_reporting`/`reminder_due`; precedence `auth_expired` > `failed` > `browser_not_reporting` > `reminder_due` > `syncing` > `completed` > `connected`; `browser_not_reporting` when `last_heartbeat_at` is older than the fixed 5-minute window (5 missed 60 s heartbeats, independent of sync frequency)
- [X] T017 [P] Scaffold the extension service worker and server client in `src/relego.extension/src/background.ts` and `src/relego.extension/src/client.ts` (server URL + Bearer token from `chrome.storage`, `POST` helpers, no Amazon data in storage)
- [X] T018 [P] Scaffold the web Sync surface: `/app/sync` route and `Sync` nav item in `src/relego.web/src/AppShell.tsx` + `src/relego.web/src/routes/SyncPage.tsx`, plus `syncApi` calls and types in `src/relego.web/src/lib/api.ts` and `src/relego.web/src/types.ts`
- [X] T019 Add foundational tests: provider registration/normalization/frequency-policy tests in `src/Relego.Tests/Services/KindleCloudProviderTests.cs`; token hashing + idempotency/dedup tests in `src/Relego.Tests/Api/SyncFoundationApiTests.cs`

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Connect and Automatically Sync Kindle Cloud Highlights (Priority: P1) 🎯 MVP

**Goal**: A user connects from the web UI, authenticates on Amazon's own surface, and gets automatic background imports without per-book actions.

**Independent Test**: Complete the connection once, take new eligible highlights, and verify they appear automatically with an explicit completion state and no false success.

### Tests for User Story 1

- [ ] T020 [P] [US1] API tests in `src/Relego.Tests/Api/SyncConnectionApiTests.cs`: `POST /connection` returns the pairing token exactly once and stores only its hash; `POST /pair` activates and returns `intervalMinutes` (default 360); job start single-flight; batch replay returns the stored counts; a job is never `completed_*` without matching `complete` counts
- [ ] T021 [P] [US1] Extension tests in `src/relego.extension/tests/parser.test.ts`: notebook parser on US fixtures (normal, empty, changed layout, sign-in redirect) with the five parse-profile invariants, and the single-flight lock

### Implementation for User Story 1

- [ ] T022 [US1] Implement `POST /sync/{providerId}/connection` (returns one-time pairing token) and `POST /sync/{providerId}/pair` (activates connection, stores `profile_version`/`region_id`, returns stored `intervalMinutes`) in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`
- [ ] T023 [US1] Implement the job endpoints in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`: `POST /jobs` (returns `jobId` + `knownBooks` fingerprint), `POST /jobs/{jobId}/batches` (idempotent per `batchIndex`, returns stored `SyncResponse` + `replayed`), `POST /jobs/{jobId}/progress`, `POST /jobs/{jobId}/complete` (`booksTotal == booksDone`)
- [ ] T024 [US1] Implement `GET /sync/{providerId}` status in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs` (`connection`, `schedule`, `currentJob` progress, `lastCompletedJob`, `failure`, `reminder`; `region` is `null` until the extension pairs)
- [ ] T025 [US1] Implement `DELETE /sync/{providerId}/connection` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs` (revokes the token, keeps all highlights) and complete `SyncConnectionRepository` disconnect handling
- [ ] T026 [P] [US1] Implement the notebook parser in `src/relego.extension/src/notebook-parser.ts` (invariants 1–5; unrecognized required structure → `provider_contract_changed` with `profileVersion`/`check`/counts only, no page free text)
- [ ] T027 [US1] Implement the sync runner in `src/relego.extension/src/background.ts`: start job → read notebook in the user's session → skip unchanged books via `knownBooks` (routine only) → chunked batch upload (default 200, ≤ 1,000, ≤ 2 MB) → explicit `complete`; any partial run ends `failed`/`interrupted`, never silent success
- [ ] T028 [US1] Implement extension alarms in `src/relego.extension/src/background.ts`: create `kindle-sync` at the stored frequency (default 360 min, recreated on every service-worker/browser start) and the fixed 60 s `kindle-heartbeat` that reports `profileVersion`/`appliedIntervalMinutes`/`nextSyncDueAt` to `POST /sync/{providerId}/heartbeat`
- [ ] T029 [P] [US1] Add US HTML fixtures in `src/relego.extension/tests/fixtures/us/` (`library.html`, `book.html`, `truncation-warning.html`, `sign-in.html`, `changed-layout.html`, `empty.html`)
- [ ] T030 [US1] Implement the web connect flow in `src/relego.web/src/routes/SyncPage.tsx`: prerequisites/disclosures before connect, one-time code + extension install steps, live progress (poll 2 s while running, 30 s otherwise), explicit completion ("Imported N new highlights" / "Nothing new")
- [ ] T031 [P] [US1] Add Playwright specs for the wizard, progress and completion in `src/relego.web/tests/sync.spec.ts` (mocked API)

**Checkpoint**: User Story 1 is fully functional and independently testable — this is the MVP.

---

## Phase 4: User Story 2 - Recover from Expired Authentication and Force Full Resync (Priority: P1)

**Goal**: Visible auth-expired state, safe re-authentication, and a non-duplicating full resync.

**Independent Test**: Expire the upstream session, verify the repair state, re-authenticate, run full resync against imported content and confirm zero duplicates.

### Tests for User Story 2

- [ ] T032 [P] [US2] API tests in `src/Relego.Tests/Api/SyncRecoveryApiTests.cs`: `auth_expired` surfaced within one sync cycle; `full_resync` over imported content yields `newHighlights = 0`/`completed_no_changes`; failure reports carry `recoverySteps`; `GET /jobs?limit=20` returns history
- [ ] T033 [P] [US2] Extension tests in `src/relego.extension/tests/recovery.test.ts`: sign-in redirect → `fail(code=auth_expired)`; heartbeat command pickup executes `sync`/`full_resync`; a closed browser leaves an in-flight job to be marked `interrupted`

### Implementation for User Story 2

- [ ] T034 [US2] Handle `auth_expired` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`: set connection state `auth_expired` + `last_auth_expired_at` on `POST /jobs/{jobId}/fail` with `code = auth_expired`, clear it on the next successful pairing/sync
- [ ] T035 [US2] Implement `POST /sync/{providerId}/commands` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs` (queue `sync` or `full_resync`, at most one pending; `sync` never overwrites `full_resync`) and consume the single command in the heartbeat response
- [ ] T036 [US2] Implement `POST /jobs/{jobId}/fail` diagnostics validation (accept only `profileVersion`/`check`/counts; reject page free text) and `GET /sync/{providerId}/jobs?limit=20` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`
- [ ] T037 [US2] Implement stale-job handling in `src/Relego.Server/Sync/SyncService.cs`: mark a job `interrupted` after the 15-minute window without progress; ensure `interrupted`/`failed` never set `last_sync_completed_at`
- [ ] T038 [US2] Implement auth detection and command execution in `src/relego.extension/src/background.ts` (open notebook tab and wait when sign-in is needed; execute the heartbeat-returned command; retry is idempotent via the job idempotency key)
- [ ] T039 [US2] Implement the recovery UI in `src/relego.web/src/routes/SyncPage.tsx` and `src/relego.web/src/AppShell.tsx`: `auth_expired` banner + AppShell indicator, **Retry**, **Full resync**, failure report (code, plain-language detail, recovery steps), disconnect confirmation stating highlights are kept
- [ ] T040 [P] [US2] Add Playwright specs for `auth_expired`, `failed` and resync states in `src/relego.web/tests/sync.spec.ts`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 4 - Choose My Amazon Region in the Browser Extension (Priority: P1)

**Goal**: The user picks one of eight validated Amazon regions in the extension; sync reads only that region's notebook host.

**Independent Test**: Select each supported region and confirm only that region's official host is touched, the region shows in Relego status, and any unsupported region/host is impossible or rejected.

### Tests for User Story 4

- [ ] T041 [P] [US4] Region tests in `src/Relego.Tests/Services/KindleRegionTests.cs`: allowlist parity between the server `ParseProfile` and the extension profile; the eight ids resolve to their hosts; `jp`/`cn`/`dk`/`ie`/`pl`, host-shaped strings (`://`, `.`, `/`) and empty are rejected (422) listing the supported ids
- [ ] T042 [P] [US4] Extension tests in `src/relego.extension/tests/region.test.ts`: the picker lists exactly the eight regions; the optional host permission is requested only for the selected host; the previous region's permission is released on change; a denied permission leaves state unchanged

### Implementation for User Story 4

- [ ] T043 [US4] Implement `GET /sync/{providerId}/regions` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs` (the eight entries with `id`/`displayName`/derived `host` plus the "only these validated hosts are supported" disclosure)
- [ ] T044 [US4] Implement `PUT /sync/{providerId}/region` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`: same connection and token (no re-pair), cancel a running job as `interrupted`, set `region_id`/`region_changed_at`, queue exactly one `full_resync`, keep interval/highlights/history unchanged
- [ ] T045 [US4] Include the selected region (id, display name, server-derived host; `null` until paired) in `GET /sync/{providerId}` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`
- [ ] T046 [US4] Implement the region picker in `src/relego.extension/src/profile.ts` + `src/relego.extension/src/background.ts`: mirror the eight-region allowlist, render the picker in the extension popup with no free-text host input, and call `PUT /region` on change
- [ ] T047 [US4] Add per-region HTML fixtures in `src/relego.extension/tests/fixtures/<regionId>/` for `ca`, `uk`, `de`, `fr`, `it`, `es`, `br` (library list, book page, truncation warning, sign-in redirect, changed layout, empty), and bump the profile version when fixtures change
- [ ] T048 [US4] Implement the read-only region display in `src/relego.web/src/routes/SyncPage.tsx` ("Region: Germany (lesen.amazon.de)", change-in-extension hint, "Region: chosen in the extension" before pairing) and the "Supported regions: United States, Canada, United Kingdom, Germany, France, Italy, Spain, Brazil" notice; no region editor or host input
- [ ] T049 [P] [US4] Add a Playwright spec asserting the region is read-only, no editable host field exists, and the supported-regions notice is shown in `src/relego.web/tests/sync.spec.ts`

**Checkpoint**: Region selection is enforced end-to-end and independently testable.

---

## Phase 6: User Story 5 - Choose How Often Kindle Sync Runs (Priority: P1)

**Goal**: The user picks one of ten frequencies in the web UI; the extension applies it to its sync alarm and the choice survives restarts.

**Independent Test**: Select each of the ten frequencies, confirm it is stored/returned, the extension alarm period matches after the next heartbeat, the next run reflects it, and an eleventh value is rejected.

### Tests for User Story 5

- [ ] T050 [P] [US5] API tests in `src/Relego.Tests/Api/SyncScheduleApiTests.cs`: all ten values are stored/returned within 5 s; any other value → 422 listing the ten with the stored value unchanged; the value survives a server restart; the heartbeat carries it; **no Quartz job/trigger exists for routine sync** (only the reminder job is registered)
- [ ] T051 [P] [US5] Extension tests in `src/relego.extension/tests/alarms.test.ts`: `kindle-sync` period equals each of the ten frequencies and is re-created on startup and on change; `kindle-heartbeat` stays 60 s for all ten; the heartbeat never starts a routine sync; overdue/overlapping firings yield at most one sync

### Implementation for User Story 5

- [ ] T052 [US5] Implement `PUT /sync/{providerId}/schedule` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs` (validate against the ten `AllowedMinutes`, persist `sync_interval_minutes`, return `{ intervalMinutes, applied, nextRunAt }`; no Quartz change) and store the extension-reported `applied_interval_minutes`/`next_sync_due_at` (display-only) in the heartbeat handler
- [ ] T053 [US5] Implement frequency application in `src/relego.extension/src/background.ts`: replace the `kindle-sync` alarm when the heartbeat's `intervalMinutes` differs from `appliedIntervalMinutes`; report `nextSyncDueAt`; on startup run at most one catch-up sync when a period has elapsed (N missed runs ⇒ ≤ 1 sync)
- [ ] T054 [US5] Implement the frequency selector in `src/relego.web/src/routes/SyncPage.tsx`: exactly ten options in order (15 min … 24 h) from `schedule.allowedIntervalMinutes`, save via `PUT /schedule`, show "Applies when your browser next reports" until `applied` is true, then the extension-reported Next run; revert on 422; helper text "Relego's server does not schedule it"
- [ ] T055 [P] [US5] Add a Playwright spec for the ten options, the pending→applied round-trip and the 422 revert in `src/relego.web/tests/sync.spec.ts`

**Checkpoint**: All P1 stories are independently functional.

---

## Phase 7: User Story 3 - Understand Coverage Limits and Use Local Fallback (Priority: P2)

**Goal**: Honest coverage/truncation disclosures, a 45-day inactivity reminder, and the local import fallback.

**Independent Test**: Sync cloud-eligible, sideloaded and truncated books, and validate disclosures, reminder behaviour and the local fallback.

### Tests for User Story 3

- [ ] T056 [P] [US3] Tests in `src/Relego.Tests/Api/SyncDisclosureApiTests.cs` and `src/Relego.Tests/Services/SyncReminderJobTests.cs`: disclosure + prerequisites are always returned (including when not connected); the reminder fires at 45 days using the clock abstraction and `UNIQUE(connection_id, quiet_since)` prevents repeats; dismiss works; existing Kindle/Kobo and `/imports` tests stay green (local fallback regression)

### Implementation for User Story 3

- [ ] T057 [US3] Return `disclosure` (versioned items) and `prerequisites` from `GET /sync/{providerId}` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`, always (including `not_connected`), and persist the shown `disclosure_version` on the connection
- [ ] T058 [US3] Implement `SyncReminderJob` in `src/Relego.Server/Jobs/SyncReminderJob.cs` (Quartz daily — the **only** sync-related Quartz job): for each active connection, when `now − max(last_new_content_at, connected_at) ≥ 45 days` and no reminder exists for that quiet period, create a `sync_reminders` row; in-app always and email via `IMailDeliveryService` only when SMTP + `delivery_email` are configured; clear/reset when new content syncs
- [ ] T059 [US3] Implement `POST /sync/{providerId}/reminders/{id}/dismiss` in `src/Relego.Server/Endpoints/CloudSyncEndpoints.cs`
- [ ] T060 [US3] Flag export-limit truncation in `src/relego.extension/src/notebook-parser.ts` (`truncated = true` per book, never an error) and report `truncatedBooks` on `complete`; store the `truncated` provenance flag
- [ ] T061 [US3] Implement the always-visible disclosure blocks, reminder banner and fallback link in `src/relego.web/src/routes/SyncPage.tsx` (Coverage & limits, What this needs, Privacy) plus the local-import pointer on `src/relego.web/src/routes/ImportPage.tsx`
- [ ] T062 [P] [US3] Add Playwright specs for disclosures-on-setup/always, the reminder banner and the fallback link in `src/relego.web/tests/sync.spec.ts`

**Checkpoint**: All user stories are independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple stories.

- [ ] T063 [P] Add the optional one-line CLI status (`relego status` prints e.g. `Kindle cloud: syncing (37/120 books)` from `GET /status.kindleCloud`) in `src/Relego.Cli/Commands/StatusCommand.cs` (additive only)
- [ ] T064 [P] Add Serilog redaction for the pairing token (and ensure no Amazon cookies/HTML are ever logged) in `src/Relego.Server/`
- [ ] T065 [P] Add extension release packaging (signed-ready Chrome/Firefox artifacts attached to releases, unpacked install docs) in `src/relego.extension/` and the release workflow
- [ ] T066 [P] Update `docs/ARCHITECTURE.md` (provider registry in `Relego.Server/Sync`, extension ownership of cadence, region allowlist, Quartz limited to the reminder job)
- [ ] T067 Run the `quickstart.md` end-to-end validation and fix any gaps
- [ ] T068 Security spot checks: server DB/logs/responses contain no Amazon cookies/passwords/session tokens and no raw pairing token; the manifest grants no host at install and only allowlisted hosts can be requested; `GET /sync/{providerId}` and `/jobs` contain no page HTML
- [ ] T069 Performance validation: batch ≤ 1,000 highlights / ≤ 2 MB, progress polling ≤ 2 s, and a first sync of ~500 books / ~10k highlights completes with visible progress
- [ ] T070 Verify all new .NET projects are in `src/Relego.slnx` and that `dotnet build src/Relego.slnx` + `dotnet test src/Relego.Tests/Relego.Tests.csproj` + web/extension test suites are green

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**
- **User Stories (Phase 3–7)**: All depend on Foundational completion; can then proceed in parallel (if staffed) or sequentially in the order below
- **Polish (Phase 8)**: Depends on the desired user stories being complete

### User Story Dependencies

- **US1 (P1, Phase 3)**: after Foundational; no dependency on other stories — MVP
- **US2 (P1, Phase 4)**: after Foundational; reuses US1 job/status components but is independently testable
- **US4 (P1, Phase 5)**: after Foundational; reuses the US1 pairing/job flow; independently testable
- **US5 (P1, Phase 6)**: after Foundational; builds on the US1 alarms/heartbeat; independently testable
- **US3 (P2, Phase 7)**: after Foundational; the reminder/disclosure/fallback paths are independently testable

### Within Each User Story

- Tests are written and FAIL before implementation
- Models/repositories before services; services before endpoints; core before integration
- Story complete before moving to the next priority

### Parallel Opportunities

- All Setup tasks marked `[P]` can run in parallel
- All Foundational tasks marked `[P]` can run in parallel (within Phase 2)
- Once Foundational completes, all user stories can start in parallel
- Test tasks marked `[P]` within a story can run in parallel
- Different stories can be worked on by different developers/worktrees

---

## Parallel Example: User Story 1

```bash
# Tests first (must fail):
Task: "API tests in src/Relego.Tests/Api/SyncConnectionApiTests.cs"
Task: "Extension parser tests in src/relego.extension/tests/parser.test.ts"

# Independent files in parallel:
Task: "Notebook parser in src/relego.extension/src/notebook-parser.ts"
Task: "US HTML fixtures in src/relego.extension/tests/fixtures/us/"
Task: "Playwright specs in src/relego.web/tests/sync.spec.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: test User Story 1 independently (quickstart step 3–6)

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. US1 → validate → MVP
3. US2 → validate (auth recovery + resync)
4. US4 → validate (regions)
5. US5 → validate (frequency)
6. US3 → validate (disclosures, reminder, fallback)
7. Polish → validate

### Delivery notes

- The spec package merges to `main` before implementation; implementation is one task per stacked PR via `gh-stack` (`task/TXXX-short-description`), each marking its task `[X]` in this file on the same branch before pushing; no conventional commits.
- Setup and Foundational tasks belong to the first PR; Polish tasks belong to the last.
- Each PR references issue #468 with `Relates to #468`.

---

## Notes

- `[P]` = different files, no dependencies; `[Story]` maps a task to its user story for traceability
- Each user story is independently completable and testable; avoid same-file conflicts and cross-story dependencies
- The plan's no-ADR state is intentional: A1/A2 remain candidates awaiting user approval, so no ADR task is included
