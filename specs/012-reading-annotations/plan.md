# Implementation Plan: Reading Annotations, Notes, and Chapters

**Branch**: `fr02-reading-annotations` | **Date**: 2026-11-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/012-reading-annotations/spec.md` (PRD FR-02 in `docs/prds/prd-v1.md`)

**Status**: Draft — awaiting plan review

**Tracking issue**: pending (created after plan approval, label `enhancement`, added to project #2)

## Summary

Annotations stop being "a quote plus where it came from" and become the full record a reader already
maintains in the source: the passage, the reader's own note, the chapter the passage belongs to,
inline tags, and stitched multi-fragment passages — with provenance (position and reported date)
visible instead of guessed.

Technically the feature is three deltas on the existing pipeline, not a new subsystem:

1. **Parsing** — a new `AnnotationMarkerParser` in `Relego.Core` turns a reader's note into
   `(cleaned note, heading level, stitching level, tags)` using the Readwise annotation vocabulary
   (`.h1`–`.h3`, `.c1`–`.cN`, leading `.tag` words, and the reserved legacy `[my note] ` prefix).
   `HighlightAggregator` learns to attach a Kindle note clipping to the highlight it belongs to
   (matched on position, not on note text) instead of storing it as a separate pseudo-highlight.
2. **Storage** — `highlights` gains `note`, `heading_level`, `stitch_level`, `tags`,
   `source_position`, `position_start`, `position_kind`, `added_on`; `created_at` keeps meaning *when
   Relego learned about the record* (FR-007). The unique index moves from
   `(user_id, book_id, text)` to an identity that survives note and marker edits (FR-034), and the
   note prefix is split out of `text` by a guarded one-time migration (FR-053–FR-056).
3. **Reading it back** — chapters, chapter paths and stitched passages are **derived, never
   denormalised**: two pure builders in `Relego.Core` compute them on read from a book's ordered
   annotations, so FR-037 ("recomputed after any change") is true by construction. One query layer
   feeds the REST API, the web UI, the CLI and both recap composers, so all four render one
   representation (SC-011, SC-013, SC-015).

The behaviour contract is deliberately identical to Readwise's, down to the marker grammar, because
the point is that a reader's existing notes, chapters, tags and stitches keep meaning what they meant
before they moved to Relego. The single Relego-specific behaviour is the legacy `[my note] ` prefix,
which is retired on upgrade.

## Technical Context

**Language/Version**: C# / .NET 10 (solution `src/Relego.slnx`: Core · Server · Cli · Tests); TypeScript for `src/relego.web` (React + Vite) and `src/landing` (Astro Starlight)

**Primary Dependencies**: ASP.NET Core Minimal APIs (`Relego.Server`), Dapper + `Microsoft.Data.Sqlite`, Serilog, Quartz.NET, MailKit, Spectre.Console (`Relego.Cli`), TanStack Query + Playwright (`relego.web`), xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `RichardSzalay.MockHttp` (`Relego.Tests`)

**Storage**: SQLite at `/data/relego.db` only (ADR-004, ADR-008); schema created and migrated in code by `SchemaBootstrap` (no EF, no migration tool). This feature adds columns, replaces one unique index, and introduces the first guarded data migration (a `schema_migrations` marker table).

**Testing**: `dotnet test src/Relego.Tests/Relego.Tests.csproj` (Core parser/aggregator/builder unit tests, `RelegoTestApplicationFactory` API tests, MockHttp CLI command tests); `pnpm exec playwright test` in `src/relego.web`; `pnpm run lint` and `pnpm run format:check` at the repository root.

**Target Platform**: Linux server container (`relego-server`) on the reader's own LAN; Windows/macOS/Linux CLI binary; evergreen browsers for the web UI and the Astro docs site.

**Project Type**: client + server + web UI + docs site (existing layout, no new project).

**Performance Goals**: annotation listing stays interactive at page size ≤ 100; a book's chapter tree and stitched passages are derived per request from one bounded query — a 2 000-annotation book must list in < 150 ms locally, and a 5 000-highlight recap must still generate within the existing recap budget.

**Constraints**: single-user, unauthenticated, trusted-LAN (Constitution V); SQLite is the only store; no background reindex and no denormalised column may be introduced, because FR-037 requires derivation after *any* change (edit, exclusion, deletion, re-import); import stays a single transaction and must remain idempotent (FR-034–FR-036); missing data stays unknown — no `"Unknown Author"` sentinel, no fabricated dates (FR-008, FR-009, FR-007); all three ingestion paths (file upload, CLI `import`, Kindle cloud sync) must produce identical records (FR-038, FR-039).

**Scale/Scope**: 1 user, ≤ 20 000 annotations, ≤ 500 books; 8 user stories across 4 surfaces (Core, Server, Cli, web) plus a docs task; 56 functional requirements and 16 success criteria.

## Constitution Check

| Principle / constraint | Status | Notes |
|------------------------|--------|-------|
| I. Client/Server Separation | PASS | Only REST HTTP + JSON crosses the boundary. The CLI and web UI gain one new read endpoint each (`GET /books/{id}/chapters`, `GET /tags`) and two new filters on `GET /highlights`; no shared assembly, no direct database access from a client. |
| II. Zero-Config Onboarding | PASS | No new configuration. Marker recognition is on by default, the chapter table of contents appears only when the reader's own headings exist (FR-026), and unknown chapter/author render as unknown rather than as a setup prompt. |
| III. Local Processing Only | PASS | Parsing, chapter tree building and stitch joining run in-process. No external call, no telemetry, no new egress path. |
| IV. Tests Ship with the Code | PASS | Every task carries tests: table-driven marker grammar tests in Core, aggregation/migration/endpoint tests in Server, command tests in Cli, one Playwright scenario covering heading → note → tag → chapter navigation. |
| V. Simplicity Over Premature Generalization | PASS (with note) | No new project, no new NuGet package, no new table for chapters, tags or stitches — chapters and stitches are derived views (Decisions D1/D2 in [research.md](research.md)), tags reuse the annotation row. See Complexity Tracking for the deviations actually accepted. |
| Tech: .NET 10 only | PASS | No new runtime or language version. |
| Tech: SQLite at `/data/relego.db` only | PASS (with note) | Columns + one replacement unique index + a `schema_migrations` marker table. The old `uq_highlights_user_book_text` index is dropped and recreated narrower; that is the feature's only schema-breaking step and it runs inside the guarded migration (FR-053). |
| Tech: Serilog, no `Console.WriteLine` | PASS | New diagnostics (migration applied, unpaired note clipping, marker rejected) go through `ILogger`; CLI user-facing output stays Spectre.Console. |
| Tech: MailKit / Quartz.NET unchanged | PASS | No new job. Recaps render notes, headings and stitched passages inside the existing EPUB/HTML composers. |
| Tech: REST HTTP + JSON only | PASS | Additive nullable fields on `HighlightItemDto` plus two read endpoints. `POST /imports` gains optional additive request fields; existing payloads keep working. |
| Tech: Server Docker-only | PASS | `Dockerfile` unchanged; the `/data` volume already exists. |
| FR-007 vs. current import (`created_at` falls back to import time) | PASS (behaviour change, must be sequenced) | Today `SyncRepository` writes `created_at = highlight.AddedOn ?? UtcNow`, so it *fakes* a reading date. FR-007 splits the two facts: `created_at` is when Relego learned the record, `added_on` is what the source reported (or NULL). Existing rows keep `created_at` untouched and get `added_on = NULL`, so they report "date unknown" instead of a date Relego invented. Decision D5. |
| FR-008 vs. current `"Unknown Author"` sentinel | PASS (behaviour change, must be sequenced) | `SyncRepository.ResolveAuthorName` currently writes the literal author `"Unknown Author"`, indistinguishable from a real author of that name, which FR-008 forbids. It becomes NULL plus `authorName: null` in the DTO; web and CLI render a local "unknown author". Nothing is deleted and SC-008 is unaffected. |
| Assessment | PASS | No unjustified violation, so no ADR is *required* by the constitution. The annotation vocabulary and derived-context model were nonetheless judged architecturally significant and recorded, with user approval, as [ADR-009](../../docs/adr/009-reading-annotations-vocabulary-and-derived-context.md). |

### Post-design re-evaluation (after Phase 1)

Re-checked against the artefacts Phase 1 produced, because three decisions changed shape while the
contracts were written. The verdict above is unchanged: **PASS**, no new violation.

| What design surfaced | Effect on the gates |
|----------------------|---------------------|
| `chapterId` without `bookId` — the API ignores it (nothing to resolve the tree against), the CLI rejects it with an actionable error | Asymmetry is intentional and documented in [contracts/annotations-api.md](contracts/annotations-api.md) and [contracts/cli.md](contracts/cli.md). Keeps Principle I (a permissive transport) and Principle II (a client that can explain itself) both satisfied without inventing an error code on the wire. |
| `PRAGMA foreign_keys = OFF` must be issued *outside* the migration transaction, since SQLite ignores the pragma inside one and `books.author_id` / `excluded_authors.author_id` reference the rebuilt `authors` table | A SQLite constraint, not a design choice. It is confined to `SchemaBootstrap`, restored in a `finally`, and asserted by the upgrade test (a failing mid-migration run must leave the pragma back on). Still inside the Tech: SQLite gate — no new store, no new database. |
| `authorName` becomes nullable on the wire, and `note`/`tags`/`position`/`addedOn` appear on `HighlightItemDto` | Principle V holds: the change is additive apart from one intentional nullability, it is delivered in the same task as the migration so no release ships mixed semantics, and both consumers already tolerate a missing value after that task. |

The three derived reads (chapters, stitches, tag aggregates) each resolve against one book rather
than against the page, which was the concern flagged in Complexity Tracking. Bounded by the book's
annotation count, computed once per request in `AnnotationContextService`, and shared by all four
consumers — so the simpler alternative (persisting a cache) remains correctly rejected.

## Requirement traceability

| Spec | Plan element |
|------|--------------|
| FR-001–FR-004 | Marker recognition is on by default; `SyncResponse` keeps its new/duplicate counters and per-file failure list; consumed note clippings count as processed and superseded ones as duplicates |
| FR-005, FR-006 | Provenance columns `source_position`, `position_kind`, `added_on` on the row; `HighlightItemDto.Position` / `AddedOn` nullable and never synthesised |
| FR-007 | `created_at` = learning date, `added_on` = source date or NULL (Decision D5) |
| FR-008, FR-009 | `authors.name` nullable, DTO null, nothing inferred for absent fields (`hasTableOfContents` false, `chapterId` null, `tags` empty) |
| FR-010–FR-015 | `AnnotationMarkerParser` + `note` column; note rendered under its passage in web, CLI and both composers; note-only annotations keep `text = ''` |
| FR-016–FR-025 | Heading grammar in `AnnotationMarkerParser`; `heading_level` column; `ChapterTreeBuilder` derives chapters, parents and re-levelling; reserved-token rules (bare `.`, `.probability`, paragraph split, empty note) |
| FR-026–FR-029 | `GET /books/{id}/chapters`, table of contents gated on at least one level-1 heading, chapter path on each item, `EpubComposer` heading sections |
| FR-030–FR-033 | `GET /highlights` filters `bookId` / `chapterId` / `tag` / `q`; CLI `highlights list`; web table of contents and filter chips — one query service behind all of them |
| FR-034–FR-037 | Identity indexes `(user_id, book_id, text, coalesce(source_position,''))` and `(user_id, book_id, note)`; import updates instead of inserting; context derived per read |
| FR-038, FR-039 | One write path (`SyncRepository.ImportAsync`) and one derive path (`AnnotationContextService`) shared by upload, CLI and cloud sync |
| FR-040–FR-045 | `stitch_level` column + `StitchedPassageBuilder`; ellipsis join, exclusion/weight/note/delivery stay on the fragments, orphan `.cN` surfaces its marker as a tag |
| FR-046–FR-052 | Tags parsed from leading tokens, stored space-separated in reader spelling, de-duplicated case-insensitively, filterable per book and across the library, never dropped from a note-only path |
| FR-053–FR-056 | `schema_migrations` marker + one guarded migration splitting `[my note] ` out of `text`, then running the marker parser on the remainder; weights, exclusions and delivery counts untouched; new imports never apply the prefix |

## Project Structure

### Documentation (this feature)

```text
specs/012-reading-annotations/
├── spec.md                 # approved; 8 stories, 56 FRs, 16 SCs
├── plan.md                 # this file
├── research.md             # Phase 0 — decisions R1–R9
├── data-model.md           # Phase 1 — columns, indexes, derived views
├── quickstart.md           # Phase 1 — runnable validation scenarios
├── checklists/requirements.md
├── contracts/
│   ├── annotations-api.md  # GET /highlights (+filters), GET /books/{id}/chapters, GET /tags
│   ├── import-contract.md  # POST /imports request/response deltas
│   └── cli.md              # relego highlights list | chapters
└── tasks.md                # generated after plan approval
```

### Source Code (repository root)

```text
src/
├── Relego.Core/
│   ├── Parsing/
│   │   ├── ParsedHighlight.cs          # + Note, position kind/start — no marker fields
│   │   ├── HighlightAggregator.cs      # note attachment by position; [my note] prefix retired
│   │   └── AnnotationMarkerParser.cs   # NEW — .hN / .cN / .tag / bare dot / legacy prefix
│   ├── Annotations/
│   │   ├── ChapterTreeBuilder.cs       # NEW — ordered annotations -> chapters, paths, TOC gate
│   │   └── StitchedPassageBuilder.cs   # NEW — fragment groups -> joined passages
│   └── Contracts/                      # + annotation DTO fields, Chapters/Tags responses
├── Relego.Server/
│   ├── Data/
│   │   ├── SyncRepository.cs           # upsert by identity, provenance columns
│   │   ├── HighlightRepository.cs      # query object (book/chapter/tag/q) + tag counts
│   │   └── RecapRepository.cs          # candidates carry note/tags/levels/position
│   ├── Services/
│   │   ├── AnnotationContextService.cs # NEW — one derive path for API, CLI, web, recaps
│   │   ├── SelectionCandidate.cs       # + annotation fields
│   │   ├── EpubComposer.cs             # heading sections, notes, stitched text
│   │   └── HtmlEmailComposer.cs        # same content model
│   ├── Endpoints/
│   │   ├── HighlightEndpoints.cs       # + bookId / chapterId / tag filters
│   │   ├── BookEndpoints.cs            # + GET /books/{id}/chapters
│   │   └── TagEndpoints.cs             # NEW — GET /tags
│   └── Infrastructure/Database/SchemaBootstrap.cs   # columns, indexes, migration marker
├── Relego.Cli/
│   ├── Program.cs                      # + highlights (list | chapters) branch
│   ├── Commands/Highlights/            # NEW — HighlightsListCommand, HighlightsChaptersCommand
│   └── Infrastructure/{RelegoHttpClient,RelegoJsonContext}.cs  # + new DTOs registered
├── relego.web/src/
│   ├── lib/{types,api}.ts              # + annotation, chapter and tag types and calls
│   └── routes/HighlightsPage.tsx       # headings, notes, tag chips, TOC, filters
├── relego.web/tests/annotations.spec.ts    # NEW
└── Relego.Tests/{Parsing,Annotations,Api,Data,Services,Cli}/   # mirrored tests
src/landing/content/docs/docs/import.md   # docs Polish task: what Relego reads now
docs/ARCHITECTURE.md                      # living-doc update in the implementing PRs
```

**Structure Decision**: keep the existing Core / Server / Cli / web layout and add no project. The
marker grammar, chapter tree and stitch joining live in `Relego.Core` because several consumers need
them, the CLI included (unlike spec 011's `ICloudSyncProvider`): the parser is used by the import path,
and the two builders must be unit-testable without a database. Derivation is reached through a single
server service (`AnnotationContextService`) so FR-033 and FR-037 cannot drift between the REST API,
the CLI, the web UI and the recap composers. No chapters, tags or stitches table is added —
`highlights` stays the only annotation table.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| Replacement of the existing unique index `uq_highlights_user_book_text` (the convention is that schema changes are additive) | The current index keys on `text`, which holds the `[my note] ` prefix today and would hold `.hN`/`.cN` markers after this feature; FR-034, FR-036 and FR-053 all need the identity to survive note and marker edits, and note-only rows would all collide on `text = ''` | Working around the index is impossible: a second marker on an existing passage can only update in place if the index does not key on the marker. The change is guarded by `schema_migrations` so it runs exactly once. |
| First data migration in the codebase (`schema_migrations` marker + `[my note] ` split) | FR-053–FR-056: existing libraries hold notes inside `text`, and the prefix must stop being recognised after upgrade | Leaving the prefix in place fails SC-008 (old notes stay readable as notes) and FR-055 (import must never treat the prefix as a marker). Adding columns alone cannot reclassify existing rows. |
| Two extra read endpoints instead of embedding everything in `GET /highlights` | FR-026 (TOC gate) and FR-049 (tags across the whole library) need aggregates that paginate badly | Deriving the TOC in the browser would duplicate the chapter algorithm in TypeScript and break SC-011/SC-013 (one representation across web, CLI and email); shipping every annotation to compute a TOC breaks the page-size contract. |

## Open decisions for review-plan

| # | Question | Default in this plan |
|---|----------|----------------------|
| D1 | Chapters derived on read vs. persisted | **Derived** — FR-037 makes any cache ownerless |
| D2 | Stitched passages derived vs. destructive merge | **Derived** — fragments keep position, note, tags, weight, exclusion and delivery history (FR-043, FR-044) |
| D3 | Tags: own table vs. column on the annotation | Column `tags` (space-separated, reader spelling); trade-off in research R4 |
| D4 | Annotation identity once markers are stripped | `(user_id, book_id, text, coalesce(source_position,''))` for passages, `(user_id, book_id, note)` for note-only rows |
| D5 | Learning date vs. reported reading date | Split: `created_at` = learned, `added_on` = reported or NULL; existing rows get `added_on = NULL` |
| D6 | How a Kindle note clipping finds its highlight | Match on `(book, position kind, position start)`, never on note text; unmatched notes become note-only annotations |
| D7 | CLI surface | New `relego highlights` group: `list --book --chapter --tag --search`, `chapters --book` |
| D8 | Web surface | Highlights page gains headings, notes, tag chips, chapter table of contents and filters; no new route |
| A1 | **ADR?** Annotation vocabulary and provenance model (`.hN` / `.cN` / `.tag` grammar, derived chapters and stitches, split of `created_at` and `added_on`) | **Resolved: approved** — recorded as [ADR-009](../../docs/adr/009-reading-annotations-vocabulary-and-derived-context.md), which also cross-links the revised `[my note] ` handling in ADR-005 and ADR-008 |

## Delivery notes

- The spec package merges to `main` before implementation (AGENTS.md); then one task per stacked PR
  via `gh stack`, each marking its task `[X]` on its own branch.
- Suggested ordering for `/speckit-tasks`: marker parser (Core, pure) → chapter and stitch builders
  (Core, pure) → aggregator note attachment → schema, migration and identity indexes → import upsert
  with provenance → `AnnotationContextService` and query object → REST endpoints (filters, chapters,
  tags) → recap composers → web UI → CLI commands → docs and `ARCHITECTURE.md`.
- Residual risks disclosed, not hidden: a note clipping whose position cannot be matched becomes a
  note-only annotation (the reader sees an orphan note rather than a lost one); two passages with
  identical text and no reported position collapse into one (indistinguishable, and the same rule the
  current code already applies); a source that changes its reported position for the same highlight
  produces a second annotation; the legacy split is one-way (rolling back means restoring the
  pre-upgrade `/data` volume that ADR-008 already asks readers to back up).
