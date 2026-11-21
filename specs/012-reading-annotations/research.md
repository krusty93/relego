# Research: Reading Annotations, Notes, and Chapters

**Feature**: 012-reading-annotations | **Spec**: [spec.md](spec.md) | **PRD**: FR-02 in `docs/prds/prd-v1.md`

All Technical Context unknowns are resolved below. Nothing is left as NEEDS CLARIFICATION. Items
marked **Open decision** are the ones a reviewer should confirm; the plan proceeds on the stated
default.

**Verified sources** (fetched 2026-11-19, the same pages used to revise the spec):

- Readwise, "How to Use Readwise's Annotation Features" — the `.h1`–`.h3` / `.c1`–`.cN` /
  `.tag` vocabulary and its reserved-token rules.
- Readwise, "Notes and Highlighting" and "Chapters" — heading markers are attached as notes to a
  highlighted passage, are a *prefix*, and may be followed by a section summary.
- Readwise, "Tags" — a note that is only dot-words becomes tags; a tag may precede a real note.

## R1. Where does marker recognition happen, and is it optional?

**Decision**: one pure parser, `Relego.Core.Parsing.AnnotationMarkerParser`, applied to every note
string that enters the system. It returns `(Note, HeadingLevel, StitchLevel, Tags)` and is the only
place that knows the grammar. Recognition is always on and has no setting.

**Grammar** (reproducing the verified Readwise behaviour):

| Input note | Result |
|---|---|
| `.h1`, `.H2`, `.h3` | heading level 1–3; remainder is the chapter summary |
| `.h1\nA summary.` or `.h1 A summary.` | heading level 1, note `A summary.` |
| `.h0`, `.h4`, `.hx` | not a heading; `.h0` / `.h4` are not valid levels, so the token is ordinary commentary |
| `.c1`, `.C12` | stitching level; a later `.c2` continues the passage |
| `.prob` | tag `prob`, no note |
| `.prob .review` | tags `prob`, `review` |
| `.prob a real comment` | tag `prob`, note `a real comment` |
| `.` or `. ` | ordinary commentary (the dot is part of the note) |
| `[my note] …` | legacy: retired by migration (R8), never recognised at import |
| empty or whitespace | no note, no markers (FR-011) |

**Rules**: leading tokens only; tokens are separated by whitespace; a token is a heading/stitch
marker only if it is exactly `.h<1-3>` / `.c<digits>`; otherwise a token starting with a dot and
containing no further dot or whitespace is a tag; the first non-token text ends token scanning and the
rest (whitespace-trimmed at the edges, line breaks inside preserved) becomes the note. Case is
ignored for marker and tag *matching*; the reader's spelling is kept for display. Duplicate tags in
one note collapse case-insensitively, first spelling wins (FR-051).

**Rationale**: the vocabulary is the reader's, not Relego's. Keeping the parse in Core means the
import path, the legacy migration and the API all agree by construction (FR-039) and the grammar can
be tested table-first without a database. A setting would create two incompatible meanings for the
same note, which is exactly the cultural change the feature is trying to avoid.

**Alternatives considered**: regex-only single pass (rejected: cannot express "reserved token, then
tags, then real note" without backtracking and is untestable in parts); parsing in the server
(rejected: the CLI would have to duplicate it, and the migration needs it too).

## R2. How does a reader's note reach its highlighted passage?

**Decision**: `HighlightAggregator` groups raw clippings by
`(title, author, position kind, position start)` and, for each group that contains at least one
highlight clipping, attaches the note clipping with the greatest `AddedOn` as that highlight's note.
Note clippings that were attached, and earlier versions of the same note, are consumed — they count as
processed entries and the superseded ones as duplicates (FR-004). A note clipping with no matching
highlight becomes a standalone annotation with `text = ''` and the note text (Story 1 AS-3). The
`"[my note] "` prefix is deleted from `HighlightAggregator` (Decision R8).

**Rationale**: today a Kindle note becomes a *second* highlight whose `text` is `"[my note] …"`. That
is the reason notes read as quotes and why the prefix exists at all. Attaching on position — not on
note text — is what makes a note belong to its passage even when the reader edited it, and it is the
one behaviour that cannot be approximated later because the raw clippings are gone after import.

Positions are compared as `(kind, start)`, not as raw strings, because Kindle writes the note as
`Location 1234` and the highlight as `Location 1234-1236`: the ranges differ, the start does not.

**Alternatives considered**: attach by note text (rejected: a note edited on the device no longer
matches, and two passages can share a note); keep notes as separate rows and join at read time
(rejected: the note would still occupy a delivery slot, a weight and an exclusion, which contradicts
FR-013–FR-015); match on raw position string (rejected: ranges and singles never match).

## R3. Chapters: derived on read, or a persisted `chapters` table?

**Decision**: derived on read by `Relego.Core.Annotations.ChapterTreeBuilder` from a book's
annotations ordered by source order. A chapter's title is the passage text of the annotation that
carries a heading marker; its note is that annotation's note (the section summary). Parent is the
nearest preceding annotation with a strictly smaller level. A chapter with no passage text creates no
chapter (edge case). Annotations before the first heading belong to the unknown chapter. The table of
contents exists only when the book has at least one level-1 heading (FR-026).

**Source order**: if every positioned annotation in the book reports the same position kind, order by
`position_start`; otherwise order by `added_on` (missing dates last); ties break on `id` (arrival
order). This satisfies the "different units" and "later sync supplies earlier pages" edge cases
without a re-sync ever rewriting stored order.

**Rationale**: FR-037 requires chapter structure to be recomputed after *any* change — re-import,
marker edit, exclusion toggle, deletion. A `chapters` table would need an owner, an invalidation
rule and a backfill for every one of those paths (and for existing libraries), and the single-user
scale (≤ 20 000 annotations, ≤ 500 books) makes derivation cheap. Deriving also means "two chapters
with the same title stay distinct" and "a deleted marker falls back to the nearest chapter" are free.

**Alternatives considered**: materialised `chapters` table with a `chapter_id` on `highlights`
(rejected: ownerless cache, invalidation bugs, and it contradicts the "no synthesised attribute"
constraint in FR-009); deriving in the web UI (rejected: duplicates the algorithm in TypeScript and
breaks SC-011/SC-013); a read-only SQLite view (rejected: the tree needs a recursive walk plus
marker knowledge that SQL cannot express cleanly, and the rule would then live in SQL rather than in
tested Core code).

## R4. How are tags stored and filtered?

**Decision**: a single nullable `tags` column on `highlights`, holding the tags space-separated in
the reader's own spelling (for example `prob review`). Matching and filtering are case-insensitive
(`LIKE` on a padded, lower-cased form), display is verbatim, and `GET /tags` aggregates the library
with `GROUP BY lower(tag)` and one representative spelling.

**Rationale**: at this scale a join table buys nothing but adds a second write path (every import,
every re-import, every delete) that must stay consistent with the annotation row for FR-037.
Space-separated is safe because the grammar forbids whitespace inside a single tag token; a
comma-separated form would collide with notes containing commas. Keeping the reader's spelling
satisfies FR-051/FR-052 and the "`prob` and `Prob` are both shown as written" edge case, while the
lower-cased comparison gives the required case-insensitive filtering.

**Alternatives considered**: `tags` + `highlight_tags` tables (rejected: three extra statements per
import for a filter that scans ≤ 20 000 rows); JSON column (rejected: cannot be LIKE-filtered without
JSON1 dependency assumptions, and it hides the reader's spelling behind encoding); storing tags
inside `note` and re-parsing on every read (rejected: makes the note column non-idempotent and forces
the parser into every query).

## R5. What identifies an annotation once markers and notes are stripped?

**Decision**: `highlights` keeps `text` as the passage only, and identity becomes two partial unique
indexes:

- passages: `uq_highlights_user_book_passage ON highlights(user_id, book_id, text, coalesce(source_position, '')) WHERE text <> ''`
- note-only rows: `uq_highlights_user_book_note ON highlights(user_id, book_id, note) WHERE text = ''`

The old `uq_highlights_user_book_text` is dropped. Import performs `select-by-identity → update |
insert` inside the existing transaction instead of `INSERT OR IGNORE`, so a re-import that only adds a
note, a heading marker, a stitching marker or a tag updates the row in place (FR-034, FR-036) and the
weight, exclusion and delivery count are untouched.

**Rationale**: the spec's identity rule is "book + passage text, independently of note, marker and
tags", which the first index implements *except* for the edge case "two fragments carry identical
passage text in the same book; both are stitching members". A purely textual identity would collapse
them, so the reported position is folded into the key: a marker change never changes the position, so
in-place updates still work, while two identical passages at different positions stay two rows. Rows
without a position keep the text-only behaviour the current code already has (indistinguishable
records collapse).

Note-only rows need their own index because `text = ''` would otherwise collide across every note.

**Alternatives considered**: identity on `text` only (rejected: violates the identical-text edge
case); identity on `(text, note)` (rejected: adding a note would create a second annotation, directly
violating FR-034); identity including the marker or tag (rejected: a level correction would create a
duplicate instead of re-levelling, violating FR-036); a hash column (rejected: no benefit at this
scale, and it hides the key from `EXPLAIN`/debugging).

**Known trade-off (disclosed)**: a source that reports a *different* position for the same passage
creates a second annotation. Kindle and Kobo both report a stable position per highlight, and Kobo
reports none at all, so this is a theoretical rather than an observed case.

## R6. Stitched passages: derived, and how are they joined?

**Decision**: derived. `Relego.Core.Annotations.StitchedPassageBuilder` walks a book's annotations in
source order and groups them: a `stitch_level = 1` opens a group, any later level ≥ 2 with an open
group joins the most recent one, and a level ≥ 2 with no open group stays out of any group and
surfaces its marker as a tag (Story 6 AS-2, so the reader's `.c2` is never silently dropped). Text is
the fragments' texts joined with ` … ` in source order; a single-member group is shown without a
trailing ellipsis. Fragments keep their own row, position, note, tags, weight, exclusion and delivery
history; an excluded fragment is omitted from the joined text while the rest still stitch (FR-044).

**Rationale**: FR-043 and FR-044 both say the fragments must survive independently — the web UI
shows them individually (Story 6 AS-5) while recaps present the joined form (Story 6 AS-1). A
destructive merge would destroy the exclusion and delivery semantics that recap billing depends on and
would be unrecoverable when the reader removes a marker on the device.

**Alternatives considered**: destructive merge at import (rejected: loses per-fragment weight,
exclusion, delivery count and note, and cannot be undone by the next import); a `stitch_groups` table
(rejected: same invalidation problem as R3, for a grouping that is a single forward pass); joining in
the EPUB composer only (rejected: the web UI and CLI would then disagree, breaking SC-013/SC-015).

## R7. Provenance: positions, dates, and the split of `created_at`

**Decision**: on `highlights`, keep `created_at` meaning *when Relego learned about the row* and add
`added_on` for the date the source reported. `source_position` stores the raw string exactly as
reported, `position_kind` is `'location' | 'page'`, and `position_start` is the parsed leading number
(null when the raw value does not start with a number). None of these is ever filled from another
field, and `null` is reported as unknown everywhere (FR-006, FR-007, FR-009).

Today `SyncRepository` writes `created_at = (highlight.AddedOn ?? DateTimeOffset.UtcNow)`, i.e. it
substitutes the import time for a missing reading date and hides the learning date. The split is the
behaviour change FR-007 demands; existing rows keep `created_at` untouched and get
`added_on = NULL`, which reports "unknown" rather than a date Relego invented.

`SyncHighlightRequest` gains optional `Note`, `Position`, and carries `AddedOn` as today. Both
producers that currently drop provenance (`UploadImportService.ToSyncRequest`,
`KindleCloudProvider.ToSyncRequest`) start mapping `Note` and `Location` from
`CloudSyncHighlight`/the parsed clipping, which is what makes FR-038/FR-039 true.

**Rationale**: chapters need source order (R3) and recaps need a trustworthy "since when" to age
highlights; both depend on not confusing arrival with reading. Parsing the leading integer once at
import keeps ordering cheap and keeps `page 12` and `Location 1234` from ever being compared as
strings.

**Alternatives considered**: keep one `created_at` and treat it as the reading date (rejected: it is
wrong for every highlight imported without a date, and unrecoverable); store a boolean "date known"
(rejected: nullable column already says that); order by `created_at` (rejected: arrival order, which
the "later sync supplies earlier pages" edge case forbids).

## R8. How does the upgrade reclassify existing notes, and how is it guaranteed to run once?

**Decision**: introduce `schema_migrations (id TEXT PRIMARY KEY, applied_at TEXT NOT NULL)` as the
codebase's first migration marker, then run one guarded migration
`012-note-split-and-identity` inside a single transaction:

1. `authors.name` becomes nullable (see below) and gets `uq_authors_name` replaced by
   `uq_authors_name ON authors(name)` plus `uq_authors_unknown ON authors(name) WHERE name IS NULL`,
   so the single "unknown author" row cannot be duplicated and the import can resolve it with one
   `SELECT`.
2. Every legacy `"Unknown Author"` row becomes `NULL`.
3. Every `highlights` row whose `text` starts with the exact legacy prefix `[my note] ` is split: the
   remainder becomes the note, `text` becomes `''`, and the marker parser (R1) runs on the remainder
   so a migrated `.h2` or `.prob` note is recognised exactly like a fresh import would.
4. `uq_highlights_user_book_text` is dropped and the two identity indexes from R5 are created.
5. The migration id is inserted into `schema_migrations` in the same transaction.

`weight`, `excluded`, `delivery_count`, `book_id`, `created_at` and provenance rows are never
touched (FR-054, SC-008). On restart the marker row short-circuits the migration (Story 4 AS-3), and
the import path never applies the prefix, so FR-055 holds.

The `authors` rebuild needs `PRAGMA foreign_keys = OFF` outside the transaction (SQLite ignores the
pragma inside one, and `books.author_id` / `excluded_authors.author_id` reference the table), followed
by a restore in a `finally`. Row ids are preserved, so no foreign key value changes.

**Rationale**: the codebase only has additive `EnsureColumnAsync` helpers, which cannot reclassify
existing rows or relax a `NOT NULL`. A marker table is the smallest mechanism that makes the
non-idempotent part run exactly once, and it gives future features a place to record their own
one-time steps. Splitting the prefix during the migration — rather than at read time — means the
reader's data is clean on disk and every consumer (API, CLI, web, composers) is correct without
special-casing.

**Alternatives considered**: keep the prefix and strip it at read time (rejected: `text` would still
be the identity key, `--search` would still match the prefix, and FR-055 forbids the prefix being
treated as a marker); a `schema_version` pragma via `PRAGMA user_version` (rejected: a single integer
cannot name the migration that ran, which makes diagnosing an upgrade harder, and this feature will
not be the last one); no marker, rely on "is any row still prefixed?" as the guard (rejected: a
library whose notes were all deleted would re-run forever, and step 4 of the migration is not
idempotent-safe that way).

## R9. How do the four surfaces keep one representation?

**Decision**: `Relego.Server/Services/AnnotationContextService.cs` is the only place that turns stored
rows into presented annotations: it loads a book's rows (or the rows of the books on a page), calls
`ChapterTreeBuilder` and `StitchedPassageBuilder`, and returns the same shape to every consumer. Three
REST surfaces expose it — `GET /highlights` (with `bookId`, `chapterId`, `tag`, `q`, `page`,
`pageSize`), `GET /books/{id}/chapters`, `GET /tags` — and the CLI (`relego highlights list`,
`relego highlights chapters`) and the web UI are thin clients over exactly those endpoints. Recaps
reuse the same builders on `SelectionCandidate`, so the EPUB and the HTML email render the joined
passage and the note the reader sees in the portal.

**Rationale**: FR-033, SC-011, SC-013 and SC-015 all reduce to "the surfaces must not disagree". One
service plus one set of DTOs makes disagreement a compile error rather than a bug report.

**Alternatives considered**: each endpoint deriving its own context (rejected: the exact failure
Story 8 describes); shipping a pre-rendered HTML fragment from the server (rejected: the CLI is a
table, not HTML); letting the web UI derive chapters from the annotation page it already has
(rejected: a page of 50 annotations cannot know the book's headings).

## R10. What is retired, and what must not break?

**Decision**: the legacy `[my note] ` prefix is retired completely — removed from
`HighlightAggregator`, removed by migration from stored rows, never recognised by the marker parser.
`ResolveAuthorName` stops producing the `"Unknown Author"` sentinel: a missing author resolves to the
single `name IS NULL` row, and `HighlightItemDto.AuthorName` becomes `string?` (`null` = unknown) in
the API, the CLI table (rendered as `-`) and the web UI (rendered as "Unknown author"). Book and author
names that *do* exist stay exactly as imported (FR-008).

**Rationale**: the prefix is the only Relego-specific annotation behaviour and the whole point of the
upgrade is to stop needing it; leaving the two behaviours side by side would mean the same note had
two meanings depending on when it was imported. The author sentinel is the same class of problem: a
generated value standing in for missing data, forbidden by FR-008/FR-009 and impossible to
distinguish from a real author literally named "Unknown Author".

**Alternatives considered**: keep recognising the prefix for back-compat (rejected: FR-055); migrate
to a synthetic "Unknown" author name in another language (rejected: same problem, translated); leave
`AuthorName` non-null and substitute at render time only (rejected: the CLI and EPUB would each need
their own copy of the decision, and `GET /books` would keep reporting a name that does not exist).

## R11. Test strategy for a change this wide

**Decision**: drive the feature from Core (pure, table-driven) outwards. `AnnotationMarkerParser`
tests are a data table of note → expected `(note, heading, stitch, tags)` including every edge case
listed in the spec. `ChapterTreeBuilder` and `StitchedPassageBuilder` tests build small annotation
lists in memory and assert structure, not strings. `SchemaBootstrap` gets a test that seeds a
pre-upgrade database (legacy prefix, sentinel author, weight/excluded/delivery values), runs
`ApplyAsync` twice, and asserts the data is split once and the values survive. Endpoint tests use
`RelegoTestApplicationFactory` for the filters and the two new endpoints; CLI tests use MockHttp with
recorded payloads; one Playwright scenario covers heading → note → tag chip → TOC navigation.

**Rationale**: Constitution IV. The migration and the derived builders are the two places where a
regression is silent (data loss, wrong chapter), so they get the strongest tests; the surfaces are
thin enough that one scenario each is honest coverage rather than a claim of completeness.

**Alternatives considered**: end-to-end-only coverage (rejected: a marker grammar cannot be
characterised through HTTP without a combinatorial fixture set).
