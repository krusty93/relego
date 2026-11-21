# Quickstart: validate 012-reading-annotations

**Feature**: 012-reading-annotations | **Spec**: [spec.md](spec.md) | **Model**: [data-model.md](data-model.md) | **Contracts**: [contracts/](contracts/)

This is a validation guide: it proves the feature end-to-end without reading the implementation.
Run the scenarios in order — each one depends on the state the previous one left behind.

## Prerequisites

- .NET 10 SDK, Docker (for the running server), `curl`.
- A scratch directory for fixtures: `mkdir /tmp/fr02 && cd /tmp/fr02`.
- Feature 011's server running locally: `docker compose up --build` (or `dotnet run --project
  src/Relego.Server`). `RELEGO_URL` below means `http://localhost:8080`.
- Every command is non-destructive to source files; the server writes only to `/data/relego.db`.

---

## Scenario 1 — Markers are understood (Stories 1, 2, 7; FR-005…FR-020, FR-046)

Write `fixture.txt` — one book, one heading, one tagged note, one stitched pair, one note-only
annotation. The Kindle separator is exactly ten `=` characters.

```text
Epigrams on Programming (Alan Perlis)
- Your Highlight on Location 210-214 | Added on Monday, March 2, 2026 8:41:00 PM

.h1 Foundations
==========
Epigrams on Programming (Alan Perlis)
- Your Highlight on Location 1234-1236 | Added on Monday, March 2, 2026 9:14:00 PM

Simplicity is prerequisite for reliability.
==========
Epigrams on Programming (Alan Perlis)
- Your Note on Location 1234 | Added on Monday, March 2, 2026 9:20:00 PM

.prob .review the whole design principle in one line
==========
Epigrams on Programming (Alan Perlis)
- Your Highlight on Location 1300-1302 | Added on Monday, March 2, 2026 9:31:00 PM

first fragment
==========
Epigrams on Programming (Alan Perlis)
- Your Highlight on Location 1303-1305 | Added on Monday, March 2, 2026 9:32:00 PM

second fragment
```

Import and inspect:

```sh
relego import fixture.txt          # or: curl -F "file=@fixture.txt" $RELEGO_URL/imports
curl -s "$RELEGO_URL/highlights?pageSize=100" | jq
```

**Expected**

- Four stored annotations: the `.h1` title (heading level 1, `text: "Foundations"`), the tagged
  passage, and the two fragments. The note clipping is **not** a fifth record — it attached to the
  passage at `Location 1234` (FR-010, R2).
- The passage has `note: "the whole design principle in one line"`, `tags: ["prob","review"]`, and
  its `text` is exactly `Simplicity is prerequisite for reliability.` — no `.prob` left in the text
  (FR-005, FR-046, FR-047).
- The heading annotation has `headingLevel: 1` and its `chapterId` is `null` for itself.
- `addedOn` equals the clipping's printed date, not the import time (FR-007).
- Neither `text` nor `note` contains `[my note]` (FR-053, R10).

Then, with the note clipping removed from the file, import again into an empty server: the passage
must appear with `text: ""` and the note, as a standalone annotation (Story 1 AS-3).

## Scenario 2 — Chapters and the unknown chapter (Story 3; FR-018…FR-031)

```sh
curl -s "$RELEGO_URL/books/$BOOK/chapters" | jq
curl -s "$RELEGO_URL/highlights?bookId=$BOOK&chapterId=$TITLE_ID" | jq '.total'
curl -s "$RELEGO_URL/highlights?bookId=$BOOK&chapterId=unknown" | jq '.total'
```

**Expected**

- One chapter (`Foundations`, level 1, parent `null`) with `annotationCount: 3` — it includes the
  descendants and the later fragments.
- `hasTableOfContents: true`, `unknownChapter.annotationCount: 0`.
- Filtering by the chapter returns the 3 annotations after the heading; filtering by `unknown`
  returns the heading plus anything before it. The two totals sum to the book's total (FR-031).
- Remove the `.h1` line, re-import into a fresh server, and `hasTableOfContents` becomes `false`
  while the `chapters` array is empty — the book is browsable but has no chapter navigation
  (FR-026). A book whose only markers are `.h2` behaves the same way.

## Scenario 3 — Derived stitching (Story 6; FR-043…FR-045, SC-013)

```sh
curl -s "$RELEGO_URL/highlights?bookId=$BOOK" | jq '[.items[] | {id, text, stitchGroupId, stitchIndex, stitchedText}]'
```

**Expected**

- Both fragments share `stitchGroupId` (`null` on any annotation that is not part of a group), with
  `stitchIndex` `0` and `1`.
- Only the first fragment carries `stitchedText`, and it is the two fragments joined by a space
  (FR-045). The second keeps `stitchedText: null`.
- With page size 1, the joined text still appears on the group's first fragment — grouping is
  resolved against the whole book, not the page (R9).
- Edit the fixture to place an unrelated highlight between the fragments; **both** must come out of
  a fresh import as separate annotations (`stitchGroupId: null`), and the total record count must not
  change (SC-013). Then restore the fixture and confirm the pair stitches again — stitching must
  never have to be rebuilt by hand.

## Scenario 4 — Tags (Story 7; FR-046…FR-052)

```sh
curl -s "$RELEGO_URL/tags" | jq
curl -s "$RELEGO_URL/highlights?tag=PROB" | jq '.total'   # case-insensitive
curl -s "$RELEGO_URL/highlights?q=review" | jq '.total'   # tag names are searchable
```

**Expected**

- One tag `prob` with count 1; spelling and case are the reader's.
- Filtering with `PROB` and with `prob` return the same single annotation, and the item still reports
  `tags: ["prob","review"]` (FR-050).
- Searching for a tag name finds the annotation (FR-051).
- Repeating `.prob` twice in one note yields one tag, not two (FR-046).

## Scenario 5 — Web portal and CLI agree (Story 8; SC-015, SC-016)

```sh
relego highlights list --book $BOOK
relego highlights chapters --book $BOOK
```

Open the same book in `src/relego.web` and compare, field by field, with the API item from Scenario 3.

**Expected** — the note, chapter path, stitched grouping, tags, position and date are identical
across all three surfaces, and `--tag prob` / `--chapter unknown` narrow to the same sets as the
HTTP filters. Every annotation appears exactly once in the CLI listing, and the CLI exits `0` when a
filter matches nothing (see [contracts/cli.md](contracts/cli.md)).

## Scenario 6 — Repeat import does not duplicate (Story 5; FR-034…FR-039)

```sh
relego import fixture.txt   # same file, second time
```

**Expected** — `newHighlights: 0`, `duplicateHighlights: 4`, `newBooks: 0`, `newAuthors: 0`, and
`GET /highlights` returns the same 4 annotations with their original `id`s. Now edit one note in the
fixture and re-import: the count of stored annotations is unchanged, and the annotation's note,
tags and classification are updated in place — the annotation is not duplicated and its curation
state (`weight`, `excluded`, `delivery_count`) is preserved (FR-034, FR-037).

---

## Automated coverage

```sh
dotnet test src/Relego.Tests/Relego.Tests.csproj
```

The upgrade path is exercised by `src/Relego.Tests/Infrastructure/SchemaBootstrapTests.cs`, which
builds a database in the pre-feature shape (a `[my note] ` row, an `"Unknown Author"` author), runs
`SchemaBootstrap.ApplyAsync` twice, and asserts FR-053…FR-056: the note is extracted, the sentinel
becomes `NULL`, `schema_migrations` holds exactly one row for `012-note-split-and-identity`, and the
second run changes nothing.

Contract-level checks live in `src/Relego.Tests/Api/HighlightEndpointsTests.cs`,
`ChapterEndpointsTests.cs`, and `TagEndpointsTests.cs`; marker parsing in
`src/Relego.Tests/Parsing/AnnotationMarkerParserTests.cs`. The web UI's expectations are covered by
`src/relego.web` Playwright tests (`cd src/relego.web && pnpm test`).

## After validation

Nothing to clean up: imports never modify source files, and the scratch fixtures live in `/tmp`.
To start over, remove the Docker volume behind `/data/relego.db` and restart the container.
