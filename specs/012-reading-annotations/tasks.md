---
description: "Task list for 012-reading-annotations"
---

# Tasks: Reading Annotations, Notes, and Chapters

**Input**: Design documents from `/specs/012-reading-annotations/`
**Prerequisites**: [plan.md](plan.md) · [spec.md](spec.md) · [research.md](research.md) · [data-model.md](data-model.md) · [contracts/](contracts/) · [quickstart.md](quickstart.md) · [ADR-009](../../docs/adr/009-reading-annotations-vocabulary-and-derived-context.md)

**Tests**: included — the repository constitution (IV. Tests Ship with the Code) and `AGENTS.md`
("use TDD where applicable, especially for API endpoints, parsers") require them. Every test task
starts from a failing test.

**Organization**: grouped by user story so each story is an independently testable increment.
Delivered one task per PR, stacked in the order below (`AGENTS.md` → PR workflow). Setup and
Foundational tasks ride in the first PR; Polish tasks ride in the last.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: owning user story (US1…US8); absent in Setup, Foundational and Polish
- Absolute repo-relative paths are given in every description

## Path Conventions

Single solution: `src/Relego.Core` (shared parsing + contracts), `src/Relego.Server` (API, storage,
recaps), `src/Relego.Cli`, `src/relego.web`, tests in `src/Relego.Tests` mirroring source folders.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The vocabulary types and DTO fields every story writes into. No behaviour yet.

- [ ] T001 [P] Add the `AnnotationMarkers` result record in `src/Relego.Core/Annotations/AnnotationMarkers.cs` with `Note` (`string?`), `HeadingLevel` (`int?`), `StitchLevel` (`int?`), `Tags` (`IReadOnlyList<string>`, never null), `HasMarkers`; XML docs on every public member per `src/Directory.Build.props`
- [ ] T002 [P] Add the shared source-order helper in `src/Relego.Core/Annotations/SourceOrder.cs`: order by `position_start, id` **only** when every positioned annotation in the book reports the same `position_kind` and every annotation has a `position_start`; otherwise `added_on asc (NULLs last), id asc` (data-model.md → "Source order")
- [ ] T003 [P] Extend `SyncHighlightRequest` in `src/Relego.Core/Contracts/SyncRequest.cs` with optional `Note` and `Position` (`string?`) plus XML docs — additive only, an existing payload keeps working unchanged
- [ ] T004 [P] Extend `ParsedHighlight` in `src/Relego.Core/Parsing/ParsedHighlight.cs` with `Note` and `Position` (`string?`), and delete the doc comment claiming notes are prefixed with `[my note] `
- [ ] T005 [P] Extend `HighlightItemDto` in `src/Relego.Core/Contracts/HighlightsResponse.cs` with nullable `Note`, `Tags` (array, never null — `[]` when the annotation has none), `HeadingLevel`, `ChapterId`, `ChapterPath`, `StitchGroupId`, `StitchIndex`, `StitchedText`, `Position`, `AddedOn`; change `AuthorName` to `string?`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The grammar and the schema. Four stories share the parser, so it cannot belong to one.

**⚠️ CRITICAL**: no user story work starts until this phase is complete.

- [ ] T006 [P] Add `src/Relego.Tests/Parsing/AnnotationMarkerParserTests.cs` — table-driven over the research.md R1 grammar table: `.h1`/`.H2`/`.h3` are headings 1–3, `.h1\nA summary.` and `.h1 A summary.` keep `A summary.` as the note, `.h0`/`.h4`/`.hx` are not headings, `.c1`/`.C12` are stitch levels, `.prob` and `.prob .review` are tags, `.prob a real comment` splits into tag + note, a bare `.` is commentary, whitespace-only yields no note, `[my note] …` is never recognised, and a repeated tag collapses case-insensitively keeping the first spelling
- [ ] T007 Implement `AnnotationMarkerParser` in `src/Relego.Core/Parsing/AnnotationMarkerParser.cs` (pure, no I/O) returning `AnnotationMarkers`; case-insensitive matching, reader spelling preserved for display; the first non-token text ends token scanning and the remaining text keeps its internal line breaks
- [ ] T008 Extend `SchemaSql`/`ApplyAsync` in `src/Relego.Server/Infrastructure/Database/SchemaBootstrap.cs`: add `schema_migrations (id TEXT PRIMARY KEY, applied_at TEXT NOT NULL)`; add the eight `highlights` columns from data-model.md with `CHECK (heading_level IS NULL OR heading_level BETWEEN 1 AND 3)`, `CHECK (stitch_level IS NULL OR stitch_level >= 1)`, `CHECK (position_kind IS NULL OR position_kind IN ('location', 'page'))`, `CHECK (length(text) > 0 OR note IS NOT NULL)`; make `authors.name` nullable in the fresh-install DDL and add `uq_authors_unknown ON authors(name) WHERE name IS NULL`; replace `uq_highlights_user_book_text` with `uq_highlights_user_book_passage`, `uq_highlights_user_book_note` and `idx_highlights_user_book_order` for fresh databases
- [ ] T009 Extend `src/Relego.Tests/Infrastructure/SchemaBootstrapTests.cs`: a fresh database has every new column, each `CHECK` rejects its violating value (`heading_level = 4`, `stitch_level = 0`, `position_kind = 'chapter'`, empty `text` with `NULL` note), `schema_migrations` starts empty, `authors.name` accepts `NULL`, and `ApplyAsync` is still idempotent

**Checkpoint**: grammar and schema fixed — all eight stories can now proceed.

---

## Phase 3: User Story 1 — My notes stay with the passage they belong to (Priority: P1) 🎯 MVP

**Goal**: a Kindle note clipping becomes the note of its highlighted passage instead of a second,
`[my note] `-prefixed library entry; unmatched notes survive as their own annotation; a re-import
updates in place rather than duplicating.

**Independent Test**: import a clippings file where one highlight has a note and another note has no
matching highlight, then confirm the first passage carries the note, the second survives as its own
item, and importing the same file twice changes no counts.

*Covers FR-010…FR-015, plus FR-034…FR-039 (annotation identity and one ingestion shape), which no
single story owns but which Story 1 acceptance scenario 4 depends on.*

### Tests for User Story 1 ⚠️ (write first, confirm they fail)

- [ ] T010 [P] [US1] Add `src/Relego.Tests/Parsing/HighlightAggregatorTests.cs`: a note at `Location 1234` attaches to the highlight at `Location 1234-1236` (matched on the position *start*, never on note text); the note with the greatest `AddedOn` wins; superseded note clippings count as duplicates; an unmatched note becomes `text: ""` with its note; a multi-line note keeps its line breaks; no `[my note] ` prefix is ever recognised
- [ ] T011 [P] [US1] Update `src/Relego.Tests/Parsing/ClippingsParserTests.cs`: a note clipping contributes an attached note and never a `[my note] `-prefixed passage
- [ ] T012 [P] [US1] Add `src/Relego.Tests/Data/SyncRepositoryAnnotationTests.cs`: re-importing identical data inserts nothing; a changed note updates the same row and preserves `id`, `weight`, `excluded`, `delivery_count`, `last_seen`
- [ ] T013 [P] [US1] Extend `src/Relego.Tests/Api/HighlightEndpointTests.cs`: `note` round-trips through `GET /highlights`; `text` carries neither a marker nor the legacy prefix; a note-only item is returned with `"text": ""`
- [ ] T014 [P] [US1] Extend `src/Relego.Tests/Recap/EpubComposerTests.cs` and `src/Relego.Tests/Services/HtmlEmailComposerTests.cs`: a selected passage's note renders beneath the passage in both composers

### Implementation for User Story 1

- [ ] T015 [US1] Rewrite attachment in `src/Relego.Core/Parsing/HighlightAggregator.cs`: group by `(title, author, position kind, position start)`, attach the latest note to a group that has a highlight, emit note-only annotations for groups without one, count consumed note clippings as processed and superseded ones as duplicates, and delete the `NotePrefix` constant
- [ ] T016 [US1] Update `src/Relego.Core/Parsing/ClippingsParser.cs` to populate `ParsedHighlight.Note` (raw note text, markers included) and keep `Text` passage-only — never trimmed of internal line breaks
- [ ] T017 [US1] Replace `INSERT OR IGNORE` in `SyncRepository.ImportAsync` (`src/Relego.Server/Data/SyncRepository.cs`) with select-then-update-or-insert over the annotation identity, persisting `note`, `heading_level`, `stitch_level` and `tags` produced by `AnnotationMarkerParser`; an update counts as an existing annotation, and `weight`, `excluded`, `delivery_count` and `last_seen` are never reset
- [ ] T018 [US1] Select `note` in the item projection of `src/Relego.Server/Data/HighlightRepository.cs` (both the paged query and the count query keep using the same predicate)
- [ ] T019 [US1] Map `Note` onto `HighlightItemDto` in `src/Relego.Server/Endpoints/HighlightEndpoints.cs`; a `NULL` note stays `null` and is never rendered as `""`
- [ ] T020 [US1] Send and receive notes on every ingestion path: `src/Relego.Core/Sources/KoboReaderSource.cs` (a Kobo `note` row becomes `Note`, not a prefixed passage), `src/Relego.Core/Sources/KindleClippingsSource.cs`, `src/Relego.Server/Sync/KindleCloudProvider.cs` (`CloudSyncHighlight.Note` is currently discarded) and `src/Relego.Server/Services/UploadImportService.cs`
- [ ] T021 [US1] Carry the note through recaps: add it to `SelectionCandidate` (`src/Relego.Server/Services/SelectionCandidate.cs`), populate it in `src/Relego.Server/Data/RecapRepository.cs`, and render it in `src/Relego.Server/Services/EpubComposer.cs` and `src/Relego.Server/Services/HtmlEmailComposer.cs`
- [ ] T022 [US1] Render the note beneath its passage on the highlights page — `src/relego.web/src/lib/types.ts` and `src/relego.web/src/routes/HighlightsPage.tsx`; invoke the `impeccable` skill before changing UI, and keep the existing list layout conventions

**Checkpoint**: notes are attached, stored, exposed and delivered — MVP demonstrable on its own.

---

## Phase 4: User Story 2 — Chapters from heading markers (Priority: P1)

**Goal**: `.h1`/`.h2`/`.h3`-marked passages become chapters, hierarchy follows level and reading order,
and each annotation reports the chapter it belongs to instead of a bare location.

**Independent Test**: import a book with `.h1` and `.h2` marked passages interleaved with ordinary
highlights and confirm each highlight reports the right chapter and nesting and each heading shows
its own summary note.

*Covers FR-016…FR-025.*

### Tests for User Story 2 ⚠️

- [ ] T023 [P] [US2] Add `src/Relego.Tests/Annotations/ChapterTreeBuilderTests.cs`: a `.h2` nests under the preceding `.h1`; a `.h2` before any `.h1` becomes a top-level chapter (Story 2 AS-4); changing a marker's level re-parents descendants; a heading annotation with no passage text creates no chapter (FR-020); the section summary becomes the chapter note; a book with no markers reports every annotation as unknown chapter (Story 2 AS-3); ordering follows reading order, not arrival order (FR-023)
- [ ] T024 [P] [US2] Extend `src/Relego.Tests/Api/HighlightEndpointTests.cs`: each item reports `headingLevel` and `chapterId`/`chapterPath`, with `null`/`[]` when there is no chapter

### Implementation for User Story 2

- [ ] T025 [US2] Implement `ChapterTreeBuilder` in `src/Relego.Core/Annotations/ChapterTreeBuilder.cs` using `SourceOrder`: chapters keyed by the titling annotation's `id` (never by title), `ParentId` = nearest preceding chapter with a strictly smaller level, `AnnotationCount` including descendants, `HasTableOfContents` true at ≥ 1 level-1 heading
- [ ] T026 [US2] Add `AnnotationContextService` in `src/Relego.Server/Services/AnnotationContextService.cs` — the single derive path for a book, returning the `AnnotationContext` per annotation and the `TableOfContents`; register it in `src/Relego.Server/Program.cs`. This service is the only place chapters are computed; no endpoint or client re-derives them
- [ ] T027 [US2] Attach `HeadingLevel`, `ChapterId` and `ChapterPath` in `src/Relego.Server/Data/HighlightRepository.cs` (derivation runs per book, never per page) and map them in `src/Relego.Server/Endpoints/HighlightEndpoints.cs`
- [ ] T028 [US2] Render heading annotations structurally distinct from quoted passages, with the section note beneath, in `src/relego.web/src/routes/HighlightsPage.tsx` and `src/relego.web/src/lib/format.ts`; `impeccable` skill required before UI changes

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3 — Browse a book by chapter, including the unknown chapter (Priority: P2)

**Goal**: a book exposes a table of contents built from the reader's markers, and the annotation list
can be narrowed to one chapter — including the annotations that sit before the first heading.

**Independent Test**: open a book with at least two chapters, jump to one from the contents, filter
to it, then filter to the unknown chapter and confirm only the unassigned annotations appear.

*Covers FR-026…FR-031; contract in [contracts/annotations-api.md](contracts/annotations-api.md).*

### Tests for User Story 3 ⚠️

- [ ] T029 [P] [US3] Add `src/Relego.Tests/Api/ChapterEndpointTests.cs`: `GET /books/{id}/chapters` returns chapters in reading order with correct nesting and descendant-inclusive counts; `hasTableOfContents` is `false` and `chapters` empty for a book with no headings **and** for a book whose only markers are `.h2`/`.h3`; `unknownChapter` is always present with `id: null`; a missing book returns `404`
- [ ] T030 [P] [US3] Extend `src/Relego.Tests/Api/HighlightEndpointTests.cs`: `bookId` narrows to one book; `chapterId=<id>` and `chapterId=unknown` select the right halves and their totals sum to the book total; `chapterId` without `bookId` is ignored rather than rejected; a filter matching nothing returns `200` with `items: []` and `total: 0`

### Implementation for User Story 3

- [ ] T031 [US3] Add `TableOfContentsResponse` / `ChapterDto` in `src/Relego.Core/Contracts/TableOfContentsResponse.cs` with the fields and naming of [contracts/annotations-api.md](contracts/annotations-api.md)
- [ ] T032 [US3] Map `GET /books/{id}/chapters` in `src/Relego.Server/Endpoints/BookEndpoints.cs` on top of `AnnotationContextService`
- [ ] T033 [US3] Add `bookId` and `chapterId` (int or the literal `unknown`) to the query in `src/Relego.Server/Data/HighlightRepository.cs` and to the parameter binding/validation in `src/Relego.Server/Endpoints/HighlightEndpoints.cs`
- [ ] T034 [US3] Add the table of contents and the chapter filter to `src/relego.web/src/routes/HighlightsPage.tsx`, with the calls in `src/relego.web/src/lib/api.ts` and the types in `src/relego.web/src/lib/types.ts`; no navigation is offered when `hasTableOfContents` is `false`; `impeccable` skill required

**Checkpoint**: a reader can navigate a book's structure from the web portal.

---

## Phase 6: User Story 4 — Existing library upgrades without loss (Priority: P2)

**Goal**: an existing `/data/relego.db` upgrades in place, once, with old `[my note] ` rows becoming
annotations that carry a note, and nothing else changing.

**Independent Test**: build a database in the pre-feature shape, run `SchemaBootstrap.ApplyAsync`,
and confirm annotation count, weights, exclusions, delivery counts and provenance rows are unchanged
while the legacy notes are now notes.

*Covers FR-053…FR-056; this is the codebase's first data migration (plan.md → Complexity Tracking).*

### Tests for User Story 4 ⚠️

- [ ] T035 [P] [US4] Add `src/Relego.Tests/Infrastructure/SchemaBootstrapMigrationTests.cs`, constructing the pre-feature schema and rows in code (no binary fixture): a `[my note] ` row becomes `text = ''` with the remainder as its marker-parsed note; an `"Unknown Author"` author becomes `NULL` with exactly one unknown-author row; `id`, `weight`, `excluded`, `delivery_count`, `last_seen`, book and author are preserved; the visible record count does not drop; a `highlight_provenance` row still points at the same `highlight_id`; a second `ApplyAsync` changes nothing and leaves exactly one `schema_migrations` row; and `PRAGMA foreign_keys` is back on after both a success and a mid-migration failure

### Implementation for User Story 4

- [ ] T036 [US4] Add the guarded migration to `src/Relego.Server/Infrastructure/Database/SchemaBootstrap.cs`: set `PRAGMA foreign_keys = OFF` **outside** the transaction and restore it in a `finally`; inside one transaction, if `schema_migrations` lacks `012-note-split-and-identity`, rebuild `authors` with nullable `name` (sentinel rows → `NULL`, both indexes recreated), split `[my note] ` rows through `AnnotationMarkerParser`, drop `uq_highlights_user_book_text`, create the three new indexes, and insert the marker row; log through `ILogger`, never `Console.WriteLine`

**Checkpoint**: an existing library upgrades safely; no record is lost.

---

## Phase 7: User Story 5 — Accurate provenance for every annotation (Priority: P3)

**Goal**: every annotation reports the position and date the source gave it, and says "unknown"
instead of inventing either — including for a missing author.

**Independent Test**: import annotations with and without positions, dates and authors, and confirm
the known values are shown while the missing ones are reported as unknown.

*Covers FR-006…FR-009.*

### Tests for User Story 5 ⚠️

- [ ] T037 [P] [US5] Extend `src/Relego.Tests/Api/HighlightEndpointTests.cs`: `position` and `addedOn` round-trip; a source without them yields `null` (never the import time); `authorName` is `null` when the source reports no author and never the literal `"Unknown Author"`; two books by an unreported author share one author row
- [ ] T038 [P] [US5] Extend `src/Relego.Tests/Data/SyncRepositoryAnnotationTests.cs`: `created_at` is the import time for a new row and is preserved on update; `added_on` holds only what the source reported

### Implementation for User Story 5

- [ ] T039 [US5] Write `source_position`, `position_start`, `position_kind` and `added_on` in `src/Relego.Server/Data/SyncRepository.cs`; `created_at` becomes import time only, and existing rows keep their `created_at`
- [ ] T040 [US5] Stop inventing an author: replace the `"Unknown Author"` sentinel in `SyncRepository.ResolveAuthorName` (`src/Relego.Server/Data/SyncRepository.cs`) with the single `NULL`-named author row, and return `null` from `src/Relego.Server/Data/BookRepository.cs` so the API and web render their own "unknown author"
- [ ] T041 [US5] Populate `Position` on every ingestion path: `src/Relego.Core/Parsing/ClippingsParser.cs` (from the clipping metadata), `src/Relego.Server/Sync/KindleCloudProvider.cs` (from `CloudSyncHighlight.Location`), and `src/Relego.Core/Sources/KoboReaderSource.cs` (stays `null` — Kobo reports none, and none is invented)
- [ ] T042 [US5] Map `Position`/`AddedOn` in `src/Relego.Server/Endpoints/HighlightEndpoints.cs` and render unknown values as unknown, not blank, in `src/relego.web/src/routes/HighlightsPage.tsx` and `src/relego.web/src/lib/format.ts`; `impeccable` skill required

**Checkpoint**: provenance is honest across every surface.

---

## Phase 8: User Story 6 — Stitch related passages into one (Priority: P2)

**Goal**: `.c1` starts a stitched passage, later `.cN` fragments append to it in reading order joined
by an ellipsis, and every fragment keeps its own record.

**Independent Test**: import a book containing a `.c1`, a `.c2` and an unrelated highlight, and
confirm the two marked passages appear as one stitched passage while the third is untouched.

*Covers FR-040…FR-045, FR-037.*

### Tests for User Story 6 ⚠️

- [ ] T043 [P] [US6] Add `src/Relego.Tests/Annotations/StitchedPassageBuilderTests.cs`: `.c1` + `.c2` produce one group in reading order joined by `" … "` with no trailing ellipsis on a single member; a `.c2` before any `.c1` is not stitched and surfaces its marker as a tag (Story 6 AS-2); two runs of `.c1` produce exactly two groups (AS-3); removing a marker and re-importing reflects the change without losing a fragment (AS-4); an excluded fragment is omitted from the joined text while the rest still stitch (FR-044); stitching never crosses books (FR-042)
- [ ] T044 [P] [US6] Extend `src/Relego.Tests/Api/HighlightEndpointTests.cs`: `stitchGroupId`/`stitchIndex` are reported on every fragment, `stitchedText` appears on the group's first fragment only, and that holds even when `pageSize = 1`

### Implementation for User Story 6

- [ ] T045 [US6] Implement `StitchedPassageBuilder` in `src/Relego.Core/Annotations/StitchedPassageBuilder.cs` on top of `SourceOrder`, reusing `AnnotationMarkers.StitchLevel`; each fragment keeps its own `text`, `note`, `tags`, `position`, `weight`, `excluded` and `delivery_count`
- [ ] T046 [US6] Extend `AnnotationContextService` in `src/Relego.Server/Services/AnnotationContextService.cs` to produce stitch groups and map `StitchGroupId`, `StitchIndex` and `StitchedText` through `src/Relego.Server/Data/HighlightRepository.cs` and `src/Relego.Server/Endpoints/HighlightEndpoints.cs`
- [ ] T047 [US6] Make an orphan `.cN` surface as a tag rather than as commentary in `src/Relego.Core/Parsing/AnnotationMarkerParser.cs` and `src/Relego.Core/Annotations/StitchedPassageBuilder.cs` (Story 6 AS-2)
- [ ] T048 [US6] Show a stitched passage as one quoted item while keeping its fragments individually readable, each with its own position and note, in `src/relego.web/src/routes/HighlightsPage.tsx`; `impeccable` skill required

**Checkpoint**: stitched passages work and are inspectable.

---

## Phase 9: User Story 7 — File passages under their own tags (Priority: P2)

**Goal**: leading `.word` tokens on a note become tags on that annotation, visible on the item,
searchable, and filterable per book and across the whole library.

**Independent Test**: import passages noted `.prob`, `.prob .review` and `.prob followed by a real
comment`, and confirm the first two gain tags with no stray comment while the third gains a tag plus
its preserved comment.

*Covers FR-046…FR-052.*

### Tests for User Story 7 ⚠️

- [ ] T049 [P] [US7] Add `src/Relego.Tests/Api/TagEndpointTests.cs`: `GET /tags` groups case-insensitively, reports one representative spelling per tag (from the lowest annotation `id`), counts annotations, and orders by `count desc` then `lower(name)`; an empty library returns `{ "tags": [] }`
- [ ] T050 [P] [US7] Extend `src/Relego.Tests/Api/HighlightEndpointTests.cs`: `tag=PROB` and `tag=prob` return the same set while each item keeps its own spelling; `q` matches a tag name; a tag filter that matches nothing returns `200` with an empty page

### Implementation for User Story 7

- [ ] T051 [US7] Add `TagsResponse` / `TagSummaryDto` in `src/Relego.Core/Contracts/TagsResponse.cs` per [contracts/annotations-api.md](contracts/annotations-api.md)
- [ ] T052 [US7] Add `GET /tags` in `src/Relego.Server/Endpoints/TagEndpoints.cs` and register it in `src/Relego.Server/Program.cs`
- [ ] T053 [US7] Add the case-insensitive `tag` filter to `src/Relego.Server/Data/HighlightRepository.cs` and its parameter binding in `src/Relego.Server/Endpoints/HighlightEndpoints.cs`, matching on a lower-cased form of the space-separated `tags` column
- [ ] T054 [US7] Include tag names in `q` search in `src/Relego.Server/Data/HighlightRepository.cs` (FR-051) without changing how `q` matches text, title or author
- [ ] T055 [US7] Add tag chips to each annotation and a tag filter to the list in `src/relego.web/src/routes/HighlightsPage.tsx`, with the call in `src/relego.web/src/lib/api.ts`; offer no tag filter when the library has no tags (Story 7 AS-5); `impeccable` skill required

**Checkpoint**: tags are captured, shown, searchable and filterable.

---

## Phase 10: User Story 8 — The same structure from the web portal and the CLI (Priority: P3)

**Goal**: the CLI reports exactly what the web portal reports for the same annotation, and narrows by
chapter and tag in the same way.

**Independent Test**: read a book with chapters, notes and tags through the CLI and the web portal and
confirm both report the same note, chapter context, stitched grouping and tags.

*Covers FR-033 and SC-015/SC-016; contract in [contracts/cli.md](contracts/cli.md).*

### Tests for User Story 8 ⚠️

- [ ] T056 [P] [US8] Add `src/Relego.Tests/Cli/HighlightsCommandTests.cs` using the existing mock-HTTP pattern from `src/Relego.Tests/Cli/ImportCommandTests.cs`: `list` renders note, tags, chapter path, position and date; a note-only annotation prints its block with an empty passage; unknown author prints `Unknown author`; `chapters` prints nesting and an actionable line when `hasTableOfContents` is false; `--chapter` without `--book` fails with `--chapter requires --book <id>.`; an empty result exits `0`
- [ ] T057 [P] [US8] Add `src/relego.web/tests/annotations.spec.ts` (Playwright) asserting SC-016: heading distinction, note, tag chips, stitched grouping and chapter filtering agree with the API payload for the same annotation

### Implementation for User Story 8

- [ ] T058 [US8] Add the three new request methods to `src/Relego.Cli/Infrastructure/RelegoHttpClient.cs` and `[JsonSerializable]` entries for every new DTO to `src/Relego.Cli/Infrastructure/RelegoJsonContext.cs` (the source-generated serializer will not see them otherwise)
- [ ] T059 [US8] Add `HighlightsListCommand` in `src/Relego.Cli/Commands/Highlights/HighlightsListCommand.cs` with `--book`, `--chapter`, `--tag`, `--search`, `--page`, `--page-size` per [contracts/cli.md](contracts/cli.md)
- [ ] T060 [US8] Add `HighlightsChaptersCommand` in `src/Relego.Cli/Commands/Highlights/HighlightsChaptersCommand.cs` rendering nesting, per-chapter counts and the unknown chapter
- [ ] T061 [US8] Register the `highlights` branch (`list`, `chapters`) in `src/Relego.Cli/Program.cs` following the existing `exclude` / `weight` grouping style

**Checkpoint**: all eight stories work, and web and CLI agree field by field.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: documentation, the living map and end-to-end validation.

- [ ] T062 [P] Update `docs/ARCHITECTURE.md`: the annotation vocabulary, the derived chapter/stitch model, the one derive path and the migration marker
- [ ] T063 [P] Update the docs site under `src/landing/content/docs/docs/` — `import.md` currently claims only quote, title and author are read; document `.h1`–`.h3`, `.c1`–`.cN`, `.tag`, the retired `[my note] ` prefix, and how to browse by chapter and tag (this is the documentation user story carried as a Polish task)
- [ ] T064 [P] Update `CONTRIBUTING.md`: what a new highlight source must now report (note, position) and why marker interpretation stays on the server
- [ ] T065 [P] Re-read `docs/adr/009-reading-annotations-vocabulary-and-derived-context.md` against the delivered code and correct any drift, including the `[my note] ` cross-links in ADR-005 and ADR-008
- [ ] T066 Run `dotnet build src/Relego.slnx` and `dotnet test src/Relego.Tests/Relego.Tests.csproj`; fix every failure — warnings are errors per `src/Directory.Build.props`
- [ ] T067 Run `pnpm run lint`, `pnpm run format:check` and `cd src/relego.web && pnpm test`; fix findings with `pnpm run lint:fix` and `pnpm run format` (never by hand)
- [ ] T068 Execute the six scenarios in [quickstart.md](quickstart.md) end to end against a running server and confirm each expected outcome, including the pre-upgrade database run in Scenario 6
- [ ] T069 Verify SC-014 and SC-016 by hand: every dot-prefixed word written as a tag appears as a tag and none appears as commentary, and the API, web and CLI payloads for one annotation are identical

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: needs Setup — **blocks all stories**
- **User stories (Phases 3–10)**: each needs Foundational; the chain below decides their order
- **Polish (Phase 11)**: needs every story that is being delivered

### Story dependencies

| Story | Depends on | Why |
|-------|-----------|-----|
| US1 (P1) | Foundational | annotation identity and the note column |
| US2 (P1) | US1 | a chapter is a note that carries `.hN`, so the note must exist first |
| US3 (P2) | US2 | the table of contents is derived from chapters |
| US4 (P2) | US1, US2 | the migration parses the note remainder with the marker parser |
| US5 (P3) | US1 | provenance rides on the same upsert path |
| US6 (P2) | US1, US2 | stitching needs identity and source order |
| US7 (P2) | US1 | tags are parsed out of the note |
| US8 (P3) | US3, US6, US7 | parity is verified over the finished surfaces |

US5 and US6 can be reordered after US1 if capacity allows; US4 must land before any release that also
ships US1's index change to an existing library.

### Within each story

Tests first and failing → Core types/builders → server repository → endpoint mapping → web/CLI surface.
A story is not complete until its Independent Test passes without the later stories present.

### Parallel opportunities

- Setup: T001–T005 are all `[P]` (five different files)
- Foundational: T006 runs alongside T008/T009 preparation, but T007 depends on T006 and T009 on T008
- US1: T010–T014 in parallel; T015–T016 in parallel after them; T017 then T018–T019; T020–T022 in parallel
- US3: T029 and T030 in parallel; T031 first, then T032 and T033 in parallel, then T034
- US6: T043/T044 in parallel; T045 then T046/T047; T048 last
- US7: T049/T050 in parallel; T051 → T052/T053/T054 in parallel → T055
- US8: T056/T057 in parallel with T058; T059/T060 in parallel after T058; T061 last
- Polish: T062–T065 in parallel; T066–T069 sequential

### Parallel example — User Story 1 tests

```text
Task: HighlightAggregatorTests note attachment by position start
Task: ClippingsParserTests note clipping produces an attached note
Task: SyncRepositoryAnnotationTests upsert preserves curation state
Task: HighlightEndpointTests note round-trips
Task: composer tests render the note beneath the passage
```

---

## Implementation Strategy

### MVP first (US1 + US2 + US4)

1. Phase 1 and Phase 2
2. Phase 3 (US1) — validate: import a file with notes, no `[my note] ` survives
3. Phase 4 (US2) — validate: `.h1`/`.h2` produce nested chapters
4. Phase 6 (US4) — validate: a pre-upgrade database upgrades without loss
5. Stop and review: this is the smallest slice that is safe to put in front of an existing library

### Incremental delivery

Phases 3 → 4 → 6 → 5 → 8 → 9 → 7 → 10, one PR per task, stacked per `AGENTS.md`. Each story is
demonstrable on its own; US8 last proves parity rather than adding capability.

### PR rule

One task per PR. Each PR marks its task `[X]` in this file *on the same branch*, before pushing, and
references the feature issue with `Relates to #N`.

---

## Notes

- `[P]` means different files and no dependency on an incomplete task
- Anything that changes `src/relego.web` requires the `impeccable` skill first (`AGENTS.md`)
- Marker interpretation belongs to `AnnotationMarkerParser` alone; no endpoint, composer or client
  may re-implement it (FR-033)
- A missing value is `null`/`[]`, never invented (FR-009)
- Chapters, stitches and tag aggregates are derived on read; adding a table for any of them is a
  regression against ADR-009
- Commit after each task, without conventional-commit prefixes
