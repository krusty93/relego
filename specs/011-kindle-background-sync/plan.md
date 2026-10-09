# Implementation Plan: Kindle Background Synchronization

**Branch**: `spec/011-kindle-background-sync` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/011-kindle-background-sync/spec.md` (base scope approved; revision 2 added regions + sync frequency; **revision 3** moves routine sync timing to the extension)

**Status**: Approved 2026-10-09 — tasks generated in [tasks.md](tasks.md).

**Tracking issue**: #468 (`enhancement`, project #2). **PRD**: FR-01 in `docs/prds/prd-v1.md`.

## Summary

Deliver Kindle cloud highlight sync the way the documented established product (Readwise) does:
a **Relego browser extension** (Chrome + Firefox, Manifest V3) running in the user's own browser
reads the Kindle Notebook of the user's selected, allowlisted region (one of eight validated regions:
US `read.amazon.com`, Canada `read.amazon.ca`, UK `read.amazon.co.uk`, Germany `lesen.amazon.de`,
France `lire.amazon.fr`, Italy `leggi.amazon.it`, Spain `leer.amazon.es`, Brazil `ler.amazon.com.br`) while the user is signed in **on Amazon's own page**, and pushes
parsed highlights to the user's Relego server. **The extension owns routine sync timing** with a
browser alarm whose period is the user-chosen frequency (15 min … 24 h, default 6 h); a separate
fixed 60-second **heartbeat** alarm only reports liveness and fetches user-requested commands and the
stored frequency. The server persists/returns the frequency and accepts extension-initiated syncs but
creates **no** routine sync schedule. The server adds a provider-neutral
**`ICloudSyncProvider`** contract (the server-side, push-based counterpart of Core's
`IHighlightSource`), sync connection/job/batch/
provenance/reminder tables, a REST sync API, a Quartz.NET inactivity-reminder job (45 days; the only
Quartz use in this feature), and
reuses the existing `SyncRepository.ImportAsync` dedup so retries and full resyncs are idempotent
and non-destructive. The **web UI** (`/app/sync`) is the primary surface for connect, progress,
completion, expired-auth, retry/full-resync, disclosures, and reminders; a one-line CLI status is
optional. Local Kindle/Kobo import is unchanged and remains the documented fallback.

Rejected by constraint (see [research.md](research.md) R1): headless login, stored Amazon
session/passwords, CAPTCHA/MFA bypass, Login-with-Amazon scopes, undocumented Amazon APIs,
email ingestion.

## Technical Context

**Language/Version**: C# / .NET 10 (server, Core); TypeScript (web UI — existing; browser extension — new, browser-mandated)

**Primary Dependencies**: ASP.NET Core minimal APIs, Dapper, `Microsoft.Data.Sqlite`, Quartz.NET (existing scheduler), MailKit via `IMailDeliveryService` (existing), Serilog; React/Vite/TanStack Query (existing); extension: WebExtension MV3 APIs, Vite build, a Vitest-class unit runner (confirm with existing web toolchain at implementation)

**Storage**: SQLite `/data/relego.db`; five new tables added via `CREATE TABLE IF NOT EXISTS` in `SchemaBootstrap`; no existing table altered ([data-model.md](data-model.md))

**Testing**: xUnit + `RelegoTestApplicationFactory` (API, provider, reminder); committed HTML fixtures for parser; Playwright for web; extension unit tests with mocked browser APIs

**Target Platform**: Docker server (unchanged); Chrome/Firefox desktop for the extension

**Project Type**: Web service + web UI + new browser-extension project (`src/relego.extension`); CLI touch optional

**Performance Goals**: first sync of ~500 books / ~10k highlights completes with visible progress; batch ≤ 1000 highlights / 2 MB; UI progress latency ≤ 2 s polling

**Constraints**: no Amazon credentials/cookies/page HTML leave the browser; non-destructive inserts only; unauthenticated management API per ADR-004 (extension channel uses a Relego pairing token only); automatic work only while server + browser run (FR-012)

**Scale/Scope**: single implicit user today, `user_id` on all new tables; one provider (`kindle-cloud`), eight validated hosts (US, CA, UK, DE, FR, IT, ES, BR); Japan/China excluded as incompatible and Denmark/Ireland/Poland not offered (no validated host); no arbitrary host input

## Constitution Check

*GATE: pass before Phase 0; re-checked after Phase 1.* Constitution v1.1.1.

| Principle / constraint | Status | Notes |
|------------------------|--------|-------|
| I. Client/Server Separation | **PASS** | Server owns state, dedup, REST, UI and the reminder job. The extension is a separate client component that owns browser-session acquisition **and routine sync timing** (alarms). CLI untouched except optional status line. |
| II. Zero-Config Onboarding | **PASS (with note)** | Feature is optional; library works with no sync. In-app reminders need no email; wizard is ≤ 6 steps. Extension install is an inherent prerequisite, disclosed (FR-017). |
| III. Local Processing Only | **PASS (with note)** | Data flows Amazon → user's browser → user's server; no Relego-operated or third-party service, no telemetry. The browser contacting Amazon is the user's own authenticated session and is disclosed. Reminder email uses only the user-configured SMTP. |
| IV. Tests Ship with the Code | **PASS** | Test plan in research R15 and per-contract tests; tests accompany each task PR. |
| V. Simplicity / YAGNI | **PASS (with note)** | No multi-user auth added; pairing token is scoped only to the sync channel. Polling, not SSE/websockets. One provider; contract exists because FR-016/FR-08 require open registration. |
| Tech: C# / .NET 10 only | **JUSTIFIED DEVIATION** | Browser extensions cannot be written in .NET; the extension is a TypeScript project like the existing `relego.web`. Server and Core remain C#. See Complexity Tracking; ADR candidate A1 (needs user approval). |
| Tech: SQLite only | **PASS** | |
| Tech: Serilog, no raw Console | **PASS** | Token/cookie/text redaction required. |
| Tech: Quartz.NET scheduling | **PASS** | Quartz.NET is used for the server-side 45-day reminder job only. Routine sync is not server-scheduled: it is extension-owned (browser alarms, outside the server), so no custom server scheduler is introduced. |
| Tech: Spectre.Console CLI | **PASS** | Optional status line only. |
| Tech: REST + JSON | **PASS** | |
| Tech: Docker-only server | **PASS** | Extension is a client artifact, not a server package. |

**Post-design re-check (after Phase 1)**: unchanged. New surface area is limited to one contract,
five tables, one endpoint group, one Quartz job, one UI route, one extension project. No PRD
requirement was dropped or added; all FR-001–FR-019 trace below.

### Requirement traceability

| Spec | Plan element |
|------|--------------|
| FR-001, FR-011, FR-012 | Extension `kindle-sync` alarm + fixed 60 s heartbeat; `browser_not_reporting`; on-demand `sync` command; independent of recap delivery |
| FR-002, FR-003, FR-004 | Amazon sign-in on Amazon page only; no credential/cookie/HTML upload; no LWA; token ≠ Amazon credential |
| FR-005 a–e | Job progress + completion; `auth_expired` state/dot; `full_resync` command; Quartz reminder @ 45 days |
| FR-006, FR-007 | `INSERT OR IGNORE` dedup; job/batch idempotency; contract-break failure codes |
| FR-008, FR-017 | Versioned `CoverageDisclosure` + prerequisites shown before connect and always |
| FR-009 | `IHighlightSource`, `POST /imports`, CLI import untouched; UI fallback link |
| FR-010 | `/app/sync` primary; CLI optional |
| FR-013, FR-014 | No telemetry; `user_id` + token ownership; revocable |
| FR-015 | Insert-only; disconnect keeps data; unknown = NULL |
| FR-016 | `ICloudSyncProvider` (in `Relego.Server/Sync`) + one DI registration; no enum; not placed in Core since the CLI does not need it |
| FR-018 | Not implemented |
| FR-019 | Provider constant 45 days |
| FR-020–FR-024, FR-030, FR-031 | Region allowlist in `ParseProfile` (eight validated regions: US/CA/UK/DE/FR/IT/ES/BR), IDs only, server-derived host, extension-only selection, optional per-host permission, `PUT region`, read-only web display, "only validated hosts supported" disclosure; DK/IE/PL and JP/CN not offered |
| FR-025, FR-028 | `sync_interval_minutes` (ten values, default 360) stored/returned by server; web selector; `PUT schedule` |
| FR-026, FR-027, FR-029 | Extension-owned `kindle-sync` alarm (= frequency, recreated on start/change); fixed 60 s `kindle-heartbeat`; single-flight + idempotent `POST /jobs`; no server routine schedule, Quartz reminder only |

## Open decisions for review-plan (defaults used; nothing silently decided)

| # | Question | Default in this plan |
|---|----------|----------------------|
| D1 | Pairing token for the extension channel vs. none (trusted-LAN only) | Token (hashed, revocable) |
| D2 | Reminder channel | In-app always; email also when SMTP + delivery email configured |
| D3 | Launch regions | **US, CA, UK, DE, FR, IT, ES, BR** allowlist (eight hosts validated per R9 on 2026-10-09); Japan/China excluded (documented incompatible); Denmark/Ireland/Poland not offered (no dedicated notebook host validated); others unsupported until validated |
| D4 | Extension distribution | Build artifacts for Chrome/Firefox attached to releases + unpacked/dev install docs; store listing is a release-process follow-up |
| D5 | Pairing UX | Manual paste of server URL + code (no web→extension handoff) |
| D6 | Navigation | New top-level **Sync** route |
| D7 | CLI scope | Optional read-only status line, last priority |
| A1 | **ADR?** Provider contract + push-based browser-extension acquisition + TypeScript extension project (+ candidate A2: extension-owned cadence with fixed heartbeat; see research R17) | **Ask user**; no ADR created without approval, none created |

## Project Structure

### Documentation (this feature)

```text
specs/011-kindle-background-sync/
├── spec.md                 # revision 3, awaiting plan review
├── plan.md                 # this file
├── research.md             # Phase 0
├── data-model.md           # Phase 1
├── quickstart.md           # Phase 1
├── checklists/requirements.md
├── contracts/
│   ├── cloud-sync-provider.md
│   ├── sync-api.md
│   ├── extension-notebook-profile.md
│   └── web-ui.md
└── tasks.md                 # 70 tasks, generated 2026-10-09 (after plan approval)
```

### Source Code (repository root)

```text
src/
├── Relego.Core/
│   └── Contracts/          # + Sync* request/response DTOs (shared; no cloud provider types)
├── Relego.Server/
│   ├── Sync/               # + ICloudSyncProvider, CoverageDisclosure, CloudSyncBatch, ParseProfile
│   │                       #   KindleCloudProvider, SyncService, token service, status derivation
│   ├── Data/               # + SyncConnectionRepository, SyncJobRepository, ProvenanceRepository
│   ├── Endpoints/          # + CloudSyncEndpoints (management + extension channel)
│   ├── Jobs/               # + SyncReminderJob (Quartz; the only sync-related Quartz job — no routine sync job)
│   └── Infrastructure/Database/SchemaBootstrap.cs   # + new tables
├── Relego.Cli/Commands/StatusCommand.cs             # optional one-line status
├── relego.web/src/routes/SyncPage.tsx               # + components, api.ts, types.ts, AppShell indicator
├── relego.web/tests/sync.spec.ts
├── relego.extension/                                # NEW (add to CI; not in Relego.slnx)
│   ├── manifest.json (MV3) · src/{background (sync + heartbeat alarms),notebook-parser,client,profile}.ts
│   └── tests/{fixtures,*.test.ts}
└── Relego.Tests/{Api,Sources,Services,Infrastructure}/  # mirrored sync tests
docs/ARCHITECTURE.md        # living-doc update in the implementing PR
```

**Structure Decision**: extend the existing server/Core/web layout; add exactly one new project
(`src/relego.extension`) because the browser session cannot be reached any other way without
violating the PRD's credential constraints. The extension is outside `Relego.slnx` (JS project),
like `relego.web`. The `ICloudSyncProvider` contract and its supporting types live in
`Relego.Server/Sync/` (not Core) because only the server consumes cloud sync; Core keeps only the
shared `Sync*` DTOs.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| TypeScript browser extension (Constitution: C#/.NET only) | Only mechanism that satisfies FR-002/003 (user authenticates on Amazon; no server-side session) and the PRD's "copy the established approach" | Server-side/headless login forbidden; bookmarklet violates FR-001; .NET cannot run in browser extensions. Precedent: `relego.web` is already TS. |
| New `ICloudSyncProvider` (server-side) alongside Core's `IHighlightSource` | Existing contract is file-pull (`Locate`/`ReadAsync`); forcing push data through it needs fake paths | Extending `IHighlightSource` would break Kobo/Kindle sources and ADR-008 §5 |
| Pairing token (not general auth) | FR-014 ownership + revocation for the one channel that writes data from a browser | No token leaves the sync channel open to any LAN client/page; full auth is out of scope (Constitution V) |

## Delivery notes

- Spec package merges to `main` before implementation (AGENTS.md); implementation is one task per
  stacked PR via `gh-stack`, each marking its task `[X]`; no conventional commits.
- Suggested task ordering for `/speckit-tasks` (after plan approval): schema + Core contract →
  provider + normalization → connection/token → jobs/batches/dedup → heartbeat/commands +
  stored frequency → status derivation + disclosures → reminder job → extension parser →
  extension alarms (sync + heartbeat)/client →
  web Sync UI → AppShell indicator + Import pointer → optional CLI line → docs/ARCHITECTURE +
  release packaging.
- Known residual risks (disclosed, not hidden): Amazon page changes (mitigated by FR-007
  failure reporting), cross-source duplicate when truncation/title differences defeat text dedup,
  regional hosts for markets without a dedicated notebook (only the eight allowlisted hosts are supported and disclosed as such; DK/IE/PL and JP/CN are not offered), best-effort cadence
  (alarm delay, closed/sleeping browser, frequency applied after next heartbeat), store-review lead time for the extension.
