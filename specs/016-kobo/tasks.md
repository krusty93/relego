# Tasks: Kobo Integration

**Input**: Design documents from `/specs/016-kobo/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md, contracts/kobo-sqlite-read.md
**ADR**: [ADR-008 — Kobo Integration: `KoboReader.sqlite` Source, Import-Only Delivery](../../docs/adr/008-kobo-reader-sqlite-source.md)

**Tests**: TDD approach — write tests before or alongside implementation, mirroring feature 003. Kobo reader + resolver tests go in `src/Relego.Tests/Sources/`.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. Each phase is **self-contained**: it ends with a build + test gate and leaves the solution green, so every phase maps to exactly one PR that can be merged on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- All source paths are relative to repository root

---

## Phase 1: Setup — Dependency, Structure & Source Abstraction

**Purpose**: Add the `Microsoft.Data.Sqlite` dependency (with the mandatory security-audit suppression), create the source directory structure, introduce the `IHighlightSource` abstraction, and extract the Kindle parser's deduplication/grouping tail into a shared `HighlightAggregator` so both readers emit identical `ParseResult` values with zero downstream changes. **Functional Requirements**: FR-010, FR-011, FR-016.

**⚠️ CRITICAL**: No Kobo user-story work can begin until the abstraction exists and the existing Kindle parser tests still pass (the aggregator extraction is behavior-preserving).

- [X] T001 [P] Add `<MicrosoftDataSqliteVersion>10.0.5</MicrosoftDataSqliteVersion>` to the `<PropertyGroup>` in `src/PackageVersions.props` (mirrors the server's version; follows the existing `PollyVersion` convention). See `specs/016-kobo/research.md` §6.
- [X] T002 In `src/Relego.Cli/Relego.Cli.csproj` add `<PackageReference Include="Microsoft.Data.Sqlite" Version="$(MicrosoftDataSqliteVersion)" />` **and** an `<ItemGroup>` with `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />` mirroring `Relego.Server.csproj`. Without the suppression the build fails (`TreatWarningsAsErrors=true`, GHSA-2m69-gcr7-jv3q / CVE-2025-6965). See `specs/016-kobo/research.md` §6 and `specs/016-kobo/quickstart.md`.
- [X] T003 [P] Create directories `src/Relego.Cli/Sources/` and `src/Relego.Tests/Sources/`, and confirm the committed fixture `docs/examples/kobo-highlights.sqlite` is present (do not regenerate it — FR-016).
- [X] T004 Verify `dotnet build src/Relego.slnx` succeeds with `Microsoft.Data.Sqlite` referenced and the audit suppressed. Fix any restore/build errors before proceeding.
- [X] T005 [P] Create the `IHighlightSource` interface and `SourceDescriptor` record in `src/Relego.Cli/Sources/IHighlightSource.cs` and `src/Relego.Cli/Sources/SourceDescriptor.cs` per the contract in `specs/016-kobo/contracts/kobo-sqlite-read.md` §2. Interface (this phase): `SourceDescriptor Descriptor { get; }` + `Task<ParseResult> ReadAsync(string path, ILogger? logger = null, CancellationToken cancellationToken = default)`. `SourceDescriptor(string Id, string DisplayName)` is the source's self-owned identity, used only as a label (no central `HighlightSourceKind` enum — keeps the registry open, ADR-008 §5). The `Locate` detection member is added in US2 (Phase 3). Namespace `Relego.Cli.Sources`.
- [X] T006 Create internal `HighlightAggregator` in `src/Relego.Cli/Parsing/HighlightAggregator.cs` by **extracting verbatim** the dedup + grouping tail currently inside `ClippingsParser.ParseAsync` (the text-less filter, `HashSet<(string, string?, string)>` dedup keyed on `(Title, Author, finalText)` keeping first occurrence, `(Title, Author)` first-seen grouping, empty-book exclusion, and `TotalEntriesProcessed`/`DuplicatesRemoved` counting). Signature: `ParseResult Aggregate(IReadOnlyList<RawClipping> clippings, int totalEntriesProcessed)`. Pure, no I/O. See `specs/016-kobo/data-model.md` (HighlightAggregator) and `src/Relego.Cli/Parsing/ClippingsParser.cs`.
- [X] T007 Refactor `src/Relego.Cli/Parsing/ClippingsParser.cs` to delegate its dedup/grouping to `HighlightAggregator.Aggregate(...)` instead of doing it inline. The `[my note]` prefix application must remain consistent so the aggregator dedups on the final prefixed text exactly as today. No change to `ClippingsParser`'s public signatures.
- [X] T008 [P] Create `KindleClippingsSource` in `src/Relego.Cli/Sources/KindleClippingsSource.cs`: an `IHighlightSource` with `Descriptor => new SourceDescriptor("kindle", "Kindle")` whose `ReadAsync` delegates to `ClippingsParser.ParseAsync(path, logger)`. No new parsing logic (FR-011). Its `Locate` (Kindle detection) is added in US2 (Phase 3).
- [X] T009 Run `dotnet test src/Relego.Tests/Relego.Tests.csproj --filter "FullyQualifiedName~Parsing"` — **all existing Kindle parser tests must still pass**, proving the aggregator extraction is behavior-preserving. Then `dotnet build src/Relego.slnx` clean.

**Checkpoint**: SQLite dependency restores, the solution builds clean, the shared abstraction and aggregator exist, and the Kindle path is unchanged in behavior. **PR-ready**: this phase ships on its own with all existing tests green. Kobo reader work can begin.

---

## Phase 2: User Story 1 — Import Highlights and Notes from a Kobo Device (Priority: P1) 🎯 MVP

**Goal**: Read highlights and notes from `KoboReader.sqlite` (copied to a temp file, opened read-only), joining `Bookmark ⋈ content`, classify rows (skip hidden / dogear / text-less / orphaned), prefix `Type = note` rows with `[my note]` using the **same** prefix the Kindle parser uses, normalize to `RawClipping`, and aggregate into the same `ParseResult` the Kindle parser produces. Includes UTF-8, performance, and downstream-parity hardening so the phase ships fully green.

**Independent Test**: Point `KoboReaderSource.ReadAsync` at `docs/examples/kobo-highlights.sqlite` and verify all valid highlights are extracted with correct title/author/text/date, note rows emit `[my note]`-prefixed text indistinguishable from Kindle notes, skipped rows excluded, and the device file left byte-identical.

**Functional Requirements**: FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-012, FR-013.

### Tests for User Story 1

> **Write these tests FIRST — they must FAIL (or not compile) before implementation begins.**
> Reader/notes/edge tests go in `src/Relego.Tests/Sources/KoboReaderSourceTests.cs`; the downstream-parity test goes in `src/Relego.Tests/Sources/SyncRequestParityTests.cs`. All use xUnit `[Fact]` / `[Theory]`.
> Load the fixture via `docs/examples/kobo-highlights.sqlite` (the reader copies internally — point it at the fixture directly). See `specs/016-kobo/quickstart.md` "Test data".

- [X] T010 [P] [US1] Write test in `src/Relego.Tests/Sources/KoboReaderSourceTests.cs`: given `docs/examples/kobo-highlights.sqlite`, `KoboReaderSource.ReadAsync(fixture)` returns a `ParseResult` whose books contain the expected highlights with correct title (`content.Title`), author (`content.Attribution`), and text (`Bookmark.Text`). Asserts the `Bookmark ⋈ content` join on `VolumeID = ContentID` (FR-003).
- [X] T011 [P] [US1] Write test: a `Bookmark` row with `Hidden` truthy (`'true'` / `1`) is excluded from the result (FR-006); and a `Type = 'dogear'` row and a text-less row (both `Text` and `Annotation` null/empty) are excluded (FR-005). See `specs/016-kobo/research.md` §2 classification table.
- [X] T012 [P] [US1] Write test: an orphaned `Bookmark` row whose `VolumeID` has no matching `content.ContentID` is dropped (INNER JOIN), the remaining valid rows are still imported, and a warning is logged via a mock `ILogger` (FR-013). See `specs/016-kobo/contracts/kobo-sqlite-read.md` §1.3.
- [X] T013 [P] [US1] Write test: copy-then-read safety — capture the fixture's bytes (or last-write time) before and after `ReadAsync`, assert the on-disk fixture is **byte-identical** afterward and that no `-wal` / `-journal` sidecar is left next to it (FR-007, SC-007). See `specs/016-kobo/research.md` §3.
- [X] T014 [P] [US1] Write test: pre-read validation — a non-SQLite file (wrong first 16 bytes) and a missing/locked file each surface an actionable failure (not a raw SQLite exception), and the temp copy is cleaned up. See `specs/016-kobo/contracts/kobo-sqlite-read.md` §1.5.
- [X] T015 [P] [US1] Write test: a Kobo database containing no importable rows returns an **empty** `ParseResult` (`Books` empty) without throwing (FR-012).
- [X] T016 [P] [US1] Write test: a `Bookmark` row with `Type = 'note'` and annotation text emits a highlight whose `Text` equals `"[my note] " + Annotation`; a note row with empty `Annotation` falls back to `Text`; a note row with both empty is skipped (FR-004). See `specs/016-kobo/research.md` §2 edge cases.
- [X] T017 [P] [US1] Write test: highlight rows and note rows for the same book appear under the **same** `ParsedBook` group, distinguished only by the `[my note] ` prefix (no separate type field), matching the Kindle parser's note handling.
- [X] T018 [P] [US1] Write test: the `[my note] ` prefix string is byte-identical to the Kindle parser's (`ClippingsParser` note prefix) — assert both sources produce the same prefixed text for an equivalent note, guaranteeing cross-source consistency (SC-003).
- [X] T019 [P] [US1] Write edge-case tests: (a) UTF-8 content with CJK, diacritics, and RTL scripts in title/author/text/note round-trips verbatim (research §8); (b) `content.Attribution` null/empty → `Author = null`; (c) a malformed/orphaned row is skipped-and-logged while surrounding valid rows import (FR-013).
- [X] T020 [P] [US1] Write performance test: build (or generate) a `KoboReader.sqlite` with 10,000 `Bookmark` rows, read + normalize + dedup + group via `KoboReaderSource`, and assert completion within 5 seconds (SC-008). Use a `Stopwatch` or `[Fact(Timeout = 5000)]`.
- [X] T021 [P] [US1] Write downstream-parity test in `src/Relego.Tests/Sources/SyncRequestParityTests.cs`: a Kobo `ParseResult` passed through the existing `ClippingsImportWorkflow.CreateSyncRequest` mapping produces a `SyncRequest` whose `Books`/`Highlights` shape is identical to the Kindle path — proving downstream is source-agnostic and no new delivery code is exercised (FR-014, FR-015, SC-006).

### Implementation for User Story 1

- [X] T022 [US1] Create internal `KoboBookmarkRow` projection record in `src/Relego.Cli/Sources/KoboBookmarkRow.cs` with `Title`, `Author?`, `Text?`, `Annotation?`, `Type?`, `DateCreated?`, `Hidden?` (all per `specs/016-kobo/data-model.md` KoboBookmarkRow). Internal — never exposed.
- [X] T023 [US1] Create `KoboReaderSource` in `src/Relego.Cli/Sources/KoboReaderSource.cs` (`Descriptor => new SourceDescriptor("kobo", "Kobo")`). Implement copy-then-read: copy the DB to `Path.Combine(Path.GetTempPath(), "relego-kobo-" + Guid + ".sqlite")`, validate the first 16 bytes equal `"SQLite format 3\0"`, open `Data Source=<temp>;Mode=ReadOnly;Pooling=false`, and delete the temp copy in a `finally` block (FR-007). Its `Locate` (Kobo detection) is added in US2 (Phase 3). See `specs/016-kobo/research.md` §3.
- [X] T024 [US1] Implement the read query in `KoboReaderSource`: run `SELECT c.Title, c.Attribution, b.Text, b.Annotation, b.Type, b.DateCreated, b.Hidden FROM Bookmark b JOIN content c ON b.VolumeID = c.ContentID ORDER BY c.Title, b.DateCreated;`, map each row to `KoboBookmarkRow`, and honor the `CancellationToken`. Validate `Bookmark` and `content` tables exist, else fail with an actionable error naming the missing table (FR-003). See `specs/016-kobo/contracts/kobo-sqlite-read.md` §1.3, §1.5.
- [X] T025 [US1] Implement row classification → `RawClipping` in `KoboReaderSource`: skip when `Hidden` truthy (FR-006); skip `Type = 'dogear'` and rows with both `Text`/`Annotation` empty (FR-005); for `Type = 'highlight'`/text-bearing emit `Text` verbatim; map `DateCreated` best-effort via `DateTimeOffset.TryParse(..., CultureInfo.InvariantCulture, ...)` (null on failure, `Location = null`). Log dropped/orphaned counts as warnings. (Note prefixing for `Type = note` is added in the next task, T026.) See `specs/016-kobo/data-model.md` classification table and `research.md` §2, §8.
- [X] T026 [US1] Add the note branch to `KoboReaderSource` row classification: for `Type = 'note'` set `RawClipping.IsNote = true` and emit `"[my note] " + (Annotation ?? Text)` as the text, reusing the **same** prefix constant/semantics as `ClippingsParser` (extract a shared constant if needed so it cannot drift). A note row with both `Annotation` and `Text` empty is skipped (FR-004). See `specs/016-kobo/data-model.md` classification table and `research.md` §2.
- [X] T027 [US1] Feed the produced `RawClipping` list through `HighlightAggregator.Aggregate(...)` to return a `ParseResult`, so Kobo output is identical in shape to the Kindle path (FR-010). Empty input → empty `ParseResult` (FR-012).
- [X] T028 [US1] Run `dotnet test src/Relego.Tests/Relego.Tests.csproj --filter "FullyQualifiedName~Sources"` — **all US1 tests (extraction, skipping, notes, UTF-8, performance, parity) must pass**. `dotnet build src/Relego.slnx` clean. Confirm no downstream component required Kobo-specific handling (FR-010, SC-005).

**Checkpoint**: `KoboReaderSource` reads the fixture into a correct `ParseResult` — highlights and `[my note]`-prefixed notes — without touching the device file, within the performance budget, and source-agnostic downstream. This is the Kobo MVP. **PR-ready**: a usable reader independent of detection, fully tested and green.

---

## Phase 3: User Story 2 — Auto-Detect Source and Import From All Connected Devices (Priority: P1)

**Goal**: Make every source self-describing and detection-owning, add a `HighlightSourceResolver` built from the **injected** set of `IHighlightSource` that returns **all** detected sources (no source-type flag, no per-source branching), and wire it into the import workflow so that one device routes correctly and two simultaneously-connected devices both import in one run with per-source failure isolation. Each source's `Locate` owns its filename/directory/device rules. Register the sources (Kindle first) + resolver in DI.

**Independent Test**: Point the resolver at a Kindle path, a Kobo path, and an invalid path; verify correct routing via each source's `Locate`, and an actionable error with probed locations when neither is found. Then point `sync` at a location where both a `My Clippings.txt` and a `.kobo/KoboReader.sqlite` are present and verify highlights from both are imported in one run, that making one source fail still imports the other and reports the failure, and that the combined result is order-independent.

**Functional Requirements**: FR-001, FR-008, FR-009, FR-011, FR-017, FR-018.

### Tests for User Story 2

> Routing/not-found tests go in `src/Relego.Tests/Sources/HighlightSourceResolverTests.cs`; the multi-source import test goes in `src/Relego.Tests/Sources/MultiSourceImportTests.cs`.
> Build temp directories/files (and use the fixture for the Kobo case) — see `specs/016-kobo/research.md` §5 and `contracts/kobo-sqlite-read.md` §3.

- [ ] T029 [P] [US2] Write test: with both sources registered, the resolver routes a file named `My Clippings.txt` (and a `documents/My Clippings.txt` directory) to the source whose `Descriptor.Id == "kindle"`. Exercises `KindleClippingsSource.Locate`. See `contracts/kobo-sqlite-read.md` §3.
- [ ] T030 [P] [US2] Write test: the resolver routes a file named `KoboReader.sqlite`, a file with a SQLite header but a different name (header sniff), and a directory containing `.kobo/KoboReader.sqlite` to the source whose `Descriptor.Id == "kobo"`. Exercises `KoboReaderSource.Locate`. See `contracts/kobo-sqlite-read.md` §3.
- [ ] T031 [P] [US2] Write test: a path containing neither source returns `Found == false`, an empty `Sources`, and `ProbedLocations` listing **both** the Kindle path/pattern and the Kobo `.kobo/KoboReader.sqlite` path that were checked, and does not throw (FR-009, spec AS-2.3).
- [ ] T032 [P] [US2] Write test: a directory containing **both** a `My Clippings.txt` and a `.kobo/KoboReader.sqlite` resolves to a `SourceResolution` whose `Sources` contains **both** the Kindle and Kobo sources (FR-017). No precedence/exclusion.
- [ ] T033 [P] [US2] Write test: the import workflow given two resolved sources where the first throws/fails still imports the second, surfaces the failed source with an actionable error, and returns a combined outcome (does not abort the run) — per-source failure isolation (FR-018).
- [ ] T034 [P] [US2] Write test: order independence — importing `[Kindle, Kobo]` vs `[Kobo, Kindle]` yields the same combined set of imported highlights (FR-017).

### Implementation for User Story 2

- [ ] T035 [US2] Add the detection member to the abstraction: `SourceProbe Locate(string? userPath)` on `IHighlightSource` (`src/Relego.Cli/Sources/IHighlightSource.cs`) and the `SourceProbe(string? FoundPath, IReadOnlyList<string> ProbedLocations)` record in `src/Relego.Cli/Sources/SourceProbe.cs`. `userPath` null → probe connected devices; else resolve the path. Per `contracts/kobo-sqlite-read.md` §2 and `data-model.md`.
- [ ] T036 [US2] Create `KoboDetector` in `src/Relego.Cli/Infrastructure/KoboDetector.cs` mirroring `KindleDetector`: `DetectDatabasePath()` probes the same mount roots (`/Volumes`, `/media`, `/run/media`, Windows drives `D`–`G`) for `.kobo/KoboReader.sqlite`, plus a `GetSuggestedDatabasePath()` helper. See `specs/016-kobo/research.md` §5 and `src/Relego.Cli/Infrastructure/KindleDetector.cs`.
- [ ] T037 [US2] Implement `Locate` on both sources, each owning its own detection (so the resolver needs no per-source branching, ADR-008 §5): `KindleClippingsSource.Locate` matches `My Clippings.txt` / `<dir>/documents/My Clippings.txt` / `<dir>/My Clippings.txt` and probes devices via `KindleDetector`; `KoboReaderSource.Locate` matches `KoboReader.sqlite` / `<dir>/.kobo/KoboReader.sqlite`, sniffs the SQLite header for an oddly-named file, and probes devices via `KoboDetector`. Each returns a `SourceProbe` with `FoundPath` (or null) and the `ProbedLocations` it checked. See `specs/016-kobo/research.md` §5.
- [ ] T038 [P] [US2] Create the `ResolvedSource(IHighlightSource Source, string ResolvedPath, SourceDescriptor Descriptor)` record in `src/Relego.Cli/Sources/ResolvedSource.cs` and the `SourceResolution(bool Found, IReadOnlyList<ResolvedSource> Sources, IReadOnlyList<string> ProbedLocations)` record in `src/Relego.Cli/Sources/SourceResolution.cs` (per `specs/016-kobo/data-model.md`; invariants: ≥1 entry when `Found`, both paths listed when not found).
- [ ] T039 [US2] Create `HighlightSourceResolver(IEnumerable<IHighlightSource> sources)` in `src/Relego.Cli/Sources/HighlightSourceResolver.cs` implementing `SourceResolution Resolve(string? userPath)`: iterate the injected sources (registration order), call each `s.Locate(userPath)`, collect every `FoundPath` into `Sources` and union all `ProbedLocations`. **No** per-source `switch`/`if` and **no** precedence. See `specs/016-kobo/research.md` §5.
- [ ] T040 [US2] Register the sources and resolver in `src/Relego.Cli/Program.cs` DI: `AddSingleton<IHighlightSource, KindleClippingsSource>()` **then** `AddSingleton<IHighlightSource, KoboReaderSource>()` (registration order = processing/precedence order, Kindle first), and `AddSingleton<HighlightSourceResolver>()`. This one-line-per-source registration is the only edit a future source needs (FR-011, SC-010).
- [ ] T041 [US2] Wire the resolver into `src/Relego.Cli/Import/ClippingsImportWorkflow.cs`: inject `HighlightSourceResolver`, replace the direct `KindleDetector` + `ClippingsParser.ParseAsync` path with `Resolve(...)` then **loop over `resolution.Sources`** reading each via `resolved.Source.ReadAsync(resolved.ResolvedPath, ...)`. `CreateSyncRequest` and everything downstream remain **unchanged** (FR-010, FR-015). On not-found, return the existing not-found/parse-failed outcome carrying the probed locations.
- [ ] T042 [US2] Implement failure-isolated multi-import in `src/Relego.Cli/Import/ClippingsImportWorkflow.cs`: read + import each resolved source **independently inside a `try`/`catch`**, accumulate per-source successes and failures, continue on failure, and return a combined outcome carrying each source's result (FR-017, FR-018). `CreateSyncRequest` and downstream remain unchanged (FR-015).
- [ ] T043 [US2] Update `src/Relego.Cli/Commands/ImportCommand.cs` to report each detected source's `Descriptor.DisplayName` (e.g. "Detected Kobo source at …") in the existing Spectre.Console summary. **No new sub-command or `--source` flag** (FR-001, spec out-of-scope). See plan.md naming note.
- [ ] T044 [US2] Extend `src/Relego.Cli/Commands/ImportCommand.cs` to render a **per-source summary** in the Spectre.Console output (each source's imported counts; any source failure shown as an actionable error) for the both-connected case, while the single-source output stays as in T043.
- [ ] T045 [US2] Run `dotnet test src/Relego.Tests/Relego.Tests.csproj --filter "FullyQualifiedName~Sources"` — all US1 and US2 tests pass (routing, not-found, both-connected, failure isolation, order independence). `dotnet build src/Relego.slnx` clean.

**Checkpoint**: `relego sync` auto-detects and routes a single Kindle or Kobo source with no source-type flag, imports two simultaneously-connected devices in one run with per-source failure isolation, and a new source is "implement `IHighlightSource` + register one line". **PR-ready**: a Kobo user runs the same command as a Kindle user, fully tested and green.

---

## Phase 4: Living Documentation

**Purpose**: Make the open source-registry extensibility a first-class, documented promise of the project, connect the docs so a contributor can add a new source confidently, validate the quickstart against the shipped surface, and run the full suite to prove the whole feature is green. **Each task here is part of the feature's living docs (Constitution / repo conventions).**

- [ ] T046 [P] Update `docs/ARCHITECTURE.md`: document the `Relego.Cli/Sources/` extensibility model — the **open source registry** (`IHighlightSource` + `SourceDescriptor`, no central enum; each source owns `Locate`; a DI-injected `HighlightSourceResolver` iterates registered sources with no per-source branching), **how future integrations must be served** ("implement `IHighlightSource` and register one line in `Program.cs` — no resolver/workflow/command/enum edits"), the **multi-source import + per-source failure isolation** behavior, the copy-then-read SQLite safety model, and the **import-only** note (Kobo recaps reach users via the existing `delivery_email` channel — feature 009 / ADR-007 — with server/schema/sync API/scheduler/recap composition unchanged). Reference ADR-008 (§3, §5).
- [ ] T047 [P] Update `CONTRIBUTING.md`: add an **"Adding a new highlight source"** guide — implement `IHighlightSource` (`Descriptor` with a stable id + display name, `Locate` owning the source's detection, `ReadAsync` returning the shared `ParseResult`), normalize device rows to `RawClipping` and run them through `HighlightAggregator`, register one `AddSingleton<IHighlightSource, …>()` line in `Program.cs` (registration order = processing order), and add tests + a fixture under `docs/examples/`. Emphasize that **no** edits to the resolver, import workflow, command, or any enum are needed (Open/Closed). Cross-reference ARCHITECTURE.md and ADR-008.
- [ ] T048 [P] Update `README.md`: weave the documents together — note that Relego imports highlights from **multiple sources** (Kindle and Kobo today), call out the **extensibility** of adding new sources, and link to CONTRIBUTING.md ("Adding a new highlight source"), ARCHITECTURE.md (the registry design), and ADR-008 — making contributing a new source approachable and interesting for the reader.
- [ ] T049 [P] Validate that the `specs/016-kobo/quickstart.md` code examples (API names, namespaces, fixture path) match the implemented surface; fix any drift in quickstart.md.
- [ ] T050 Run full `dotnet test src/Relego.Tests/Relego.Tests.csproj` — **all** tests (Kindle parser, Kobo sources, multi-import, parity, and existing suites) pass. Run `dotnet build src/Relego.slnx` clean with `TreatWarningsAsErrors=true`. Confirm zero server/schema/API changes (FR-015) via `git status`.

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup + Foundational ─────────────────────────────────────────────┐
  (deps + dirs + IHighlightSource + SourceDescriptor + Aggregator refactor) │
                                                                            ▼
                                                       ┌────────────────────────────┐
                                                       │  Phase 2: US1 (P1)         │ 🎯 Kobo MVP
                                                       │  Kobo reader + notes        │
                                                       │  + UTF-8/perf/parity        │
                                                       └──────────────┬─────────────┘
                                                                      ▼
                                                       ┌────────────────────────────┐
                                                       │  Phase 3: US2 (P1)         │
                                                       │  Auto-detect + import all   │
                                                       │  connected (multi+isolation)│
                                                       └──────────────┬─────────────┘
                                                                      ▼
                                                       ┌────────────────────────────┐
                                                       │  Phase 4: Living Docs       │
                                                       └────────────────────────────┘
```

- **Setup + Foundational (Phase 1)**: No dependencies — start immediately. BLOCKS all user stories. T001→T002→T004 sequential (same csproj/build); T003/T005/T008 parallel; T006→T007 sequential (extract then delegate); T009 gates on behavior-preservation.
- **US1 (Phase 2)**: Depends on Phase 1 (needs `IHighlightSource` + `HighlightAggregator`) — BLOCKS US2. Tests T010–T021 in parallel; implementation T022–T027 sequential; T028 is the test gate.
- **US2 (Phase 3)**: Depends on US1 (needs `KoboReaderSource` for the resolver to return and for the multi-import loop). Tests T029–T034 in parallel; implementation T035–T044 sequential; T045 is the test gate.
- **Living Documentation (Phase 4)**: Depends on US1 + US2 (documents the shipped extensibility model + behavior). T046–T049 in parallel; T050 is the final full-suite gate.

### Within Each Phase

1. Tests MUST be written and FAIL before implementation begins.
2. Implementation tasks are sequential (each builds on the previous).
3. The final task in each phase is a build + test-run gate that leaves the solution green — so the phase ships as a standalone PR.

### Parallel Opportunities

- **Phase 1**: T001/T003/T005/T008 in parallel (separate files); T002/T004 sequential on the csproj/build; T006→T007 sequential (extract then delegate).
- **Phase 2**: All US1 test tasks (T010–T021) in parallel — different test methods/files; implementation T022–T027 sequential.
- **Phase 3**: US2 test tasks (T029–T034) in parallel; T038 parallel with T035–T037; resolver/DI/wire-in T039–T044 sequential.
- **Phase 4**: T046, T047, T048, T049 in parallel (different docs); T050 last.

---

## Parallel Example: Phase 2 (User Story 1 Tests)

```
# All US1 test methods can be written simultaneously:
T010: Basic Bookmark ⋈ content extraction
T011: Hidden / dogear / text-less skip
T012: Orphaned row dropped + warned
T013: Copy-then-read device-untouched
T014: Pre-read validation (non-SQLite / missing)
T015: Empty source → empty ParseResult
T016: Note row → [my note] prefix
T017: Notes + highlights same book group
T018: Prefix byte-identical to Kindle
T019: UTF-8 / null author / malformed row
T020: 10,000-row performance budget
T021: SyncRequest downstream parity
# Then implement sequentially: T022 → T023 → T024 → T025 → T026 → T027 → T028
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1: Setup + Foundational — SQLite dependency + audit suppress builds, abstraction + behavior-preserving aggregator extraction (Kindle tests still green).
2. Phase 2: US1 — `KoboReaderSource` reads the fixture into a correct `ParseResult` with `[my note]`-prefixed notes, within budget, source-agnostic downstream.
3. **STOP and VALIDATE**: The reader extracts Kobo highlights and notes without touching the device file. Usable Kobo import even before auto-detection wiring.

### Incremental Delivery

1. Phase 1 → abstraction in place, Kindle path unchanged. **(PR 1)**
2. Phase 2 (US1) → Kobo reader + notes work on the fixture (MVP!), fully tested. **(PR 2)**
3. Phase 3 (US2) → `relego sync` auto-detects and routes a source via the open registry, and imports both connected devices with per-source failure isolation. **(PR 3)**
4. Phase 4 → extensibility model + contribution guide + README glue + quickstart validation + full-suite gate → ship-ready. **(PR 4)**

### Single-Developer Flow

Sequential by phase: 1 → 2 → 3 → 4. Within each phase, write all tests first, then implement. **Each phase is its own PR** and leaves the build + tests green so it can be merged independently. Commit after each completed phase.

---

## Summary

| Metric | Count |
|--------|-------|
| **Total tasks** | 50 |
| **Phase 1 — Setup + Foundational** | 9 |
| **Phase 2 — US1 (P1)** | 19 |
| **Phase 3 — US2 (P1)** | 17 |
| **Phase 4 — Living Documentation** | 5 |
| **Parallelizable tasks** | 27 |
| **Source files created** | 11 (9 in `Sources/`, 1 `HighlightAggregator.cs`, 1 `KoboDetector.cs`) + `Program.cs` edited + 4 test files |
| **PRs** | 4 (one per phase, each independently green) |

## Notes

- All source-abstraction code uses `namespace Relego.Cli.Sources;`; the aggregator stays in `namespace Relego.Cli.Parsing;`.
- Source identity is a self-owned `SourceDescriptor` (`Id` + `DisplayName`) used **only as a label, never branched on** — there is no central source-type enum (ADR-008 §5, Open/Closed). A new source = implement `IHighlightSource` + register one `AddSingleton` line in `Program.cs`.
- The `HighlightSourceResolver` is constructed from the **injected** `IEnumerable<IHighlightSource>`; DI registration order (Kindle first) defines processing/precedence order. The resolver contains no per-source branching.
- When both devices are connected, **both** are imported in one run with per-source failure isolation (US2) — no precedence/tie-break drops a source.
- `KoboBookmarkRow` is `internal` — only `KoboReaderSource` uses it.
- The `[my note] ` prefix MUST be a single shared constant/semantics across `ClippingsParser` and `KoboReaderSource` so notes cannot drift (SC-003).
- `Microsoft.Data.Sqlite` is the only new package; the `NuGetAuditSuppress` for GHSA-2m69-gcr7-jv3q is mandatory (`TreatWarningsAsErrors=true`).
- Zero changes to `Relego.Server`, `Relego.Core`, the database schema, the sync API contract, the scheduler, or recap composition (FR-015).
- Dynamic/external plugin loading (assembly scanning, third-party DLLs) is explicitly out of scope — sources are in-process and DI-registered.
- `docs/examples/kobo-highlights.sqlite` is committed — do not regenerate it.
- Each phase ends with a build + test gate and ships as a standalone, independently-mergeable PR.
