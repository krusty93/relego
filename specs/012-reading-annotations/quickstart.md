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

Write `fixture.txt` — one book, one heading, one tagged passage, one stitched pair, one note-only
annotation. The Kindle separator is exactly ten `=` characters.

Markers belong in the **note**, which Kindle writes as its own `Your Note` clipping at the passage's
location: the marker decides what the annotated passage becomes (FR-016, FR-019), so a marker typed
into the highlight itself would stay ordinary passage text (FR-018).

```text
Epigrams on Programming (Alan Perlis)
- Your Highlight on Location 210-214 | Added on Monday, March 2, 2026 8:41:00 PM

Foundations
==========
Epigrams on Programming (Alan Perlis)
- Your Note on Location 210 | Added on Monday, March 2, 2026 8:41:30 PM

.h1
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
- Your Note on Location 1300 | Added on Monday, March 2, 2026 9:31:30 PM

.c1
==========
Epigrams on Programming (Alan Perlis)
- Your Highlight on Location 1303-1305 | Added on Monday, March 2, 2026 9:32:00 PM

second fragment
==========
Epigrams on Programming (Alan Perlis)
- Your Note on Location 1303 | Added on Monday, March 2, 2026 9:32:30 PM

.c2
```

Import and inspect:

```sh
relego import fixture.txt          # or: curl -F "file=@fixture.txt" $RELEGO_URL/imports
curl -s "$RELEGO_URL/highlights?pageSize=100" | jq
```

**Expected**

- Four stored annotations — the `Foundations` passage titled by its `.h1` note (heading level 1),
  the tagged passage, and the two fragments. The three note clippings are **not** extra records:
  each attached to the passage at its own location (FR-010, R2).
- The tagged passage has `note: "the whole design principle in one line"`, `tags: ["prob","review"]`,
  and its `text` is exactly `Simplicity is prerequisite for reliability.` — no `.prob` left in the
  passage and none left in the note (FR-005, FR-046, FR-047).
- The heading annotation has `headingLevel: 1` and sits in the chapter it titles (`chapterId: 1`,
  `chapterPath: ["Foundations"]`). Its note contains only the marker, so `note` is `null` (FR-017).
- `addedOn` equals the clipping's printed date, not the import time (FR-007).
- Neither `text` nor `note` contains `[my note]` (FR-053, R10).

Then, with the *passage* clipping at `Location 1234-1236` removed from the file, import again into an
empty server: the note clipping has nothing to attach to, so the passage must appear with
`text: ""`, the note and its tags, as a standalone annotation (Story 1 AS-3, FR-011).

## Scenario 2 — Chapters and the unknown chapter (Story 3; FR-018…FR-031)

```sh
curl -s "$RELEGO_URL/books/$BOOK/chapters" | jq
curl -s "$RELEGO_URL/highlights?bookId=$BOOK&chapterId=$TITLE_ID" | jq '.total'
curl -s "$RELEGO_URL/highlights?bookId=$BOOK&chapterId=unknown" | jq '.total'
```

**Expected**

- One chapter (`Foundations`, level 1, parent `null`) with `annotationCount: 4` — the annotation
  that titles the chapter counts in it, together with every annotation after it in source order,
  including the later fragments.
- `hasTableOfContents: true`, `unknownChapter.annotationCount: 0` — nothing precedes the heading.
- Filtering by the chapter returns all 4 annotations; filtering by `unknown` returns 0. The two
  totals sum to the book's total (FR-031).
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
- Place an unrelated highlight between the fragments and re-import: the pair still stitches, joined in
  reading order with the unrelated annotation left in place between them.
- Remove the `.c1`/`.c2` notes and re-import: both fragments come out as individual annotations
  (`stitchGroupId: null`), the total record count does not change and nothing is lost (SC-013).
  Restore the markers and re-import — the pair stitches again, because stitching is derived and
  never has to be rebuilt by hand (FR-042, FR-043).

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
