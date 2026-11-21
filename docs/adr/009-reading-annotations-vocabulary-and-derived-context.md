# ADR-009: Reading Annotations — Marker Vocabulary, Derived Chapter/Stitch Context, and Provenance Split

**Status:** Accepted
**Date:** 2026-11-19
**Updated:** 2026-11-21 — corrected against the delivered code: marker edge cases (`.h0`, `.h4`, `.c0`), how a source with no position is matched, and the recap composers, which carry the note with the passage they already selected rather than deriving context.

## Context

Relego stores a highlight's passage `text` and nothing else of the reader's own annotations. Everything a reader writes on the device — a thought about a passage, the title of a section, the word they use to file a passage, the marker that joins two passages into one argument — either ends up inside the quoted text or is lost. A note becomes a second highlight whose text is prefixed `[my note] `, adrift from the passage it explains; a heading becomes a quote that happens to start with `.h1`; a tag becomes stray commentary; a `.c2` fragment becomes an unrelated passage that follows nothing. Chapter structure, continuous passages and the reader's own vocabulary never reach the library, so no downstream surface can present them.

The pipeline already receives more than it keeps. The Kindle cloud provider and the Kobo reader both collect notes and positions, and the device database stores them in their own columns (`Bookmark.Text`, `Bookmark.Annotation`); the import path flattens them into text, so the discard happens on Relego's side, not on the source's. Existing libraries additionally carry the legacy `[my note] ` prefix *inside* `text`, which makes the prefix part of the annotation's identity and of its search index.

Three further facts constrain any solution:

1. **The vocabulary is the reader's, not Relego's.** A reader coming from Readwise has already annotated a library with `.h1`–`.h3`, `.c1`–`.cN` and dot-prefixed tag words. Relego either reads that vocabulary the same way or invalidates everything the reader wrote before.
2. **Anything derived from the reader's markers can go stale.** Chapter structure and stitched passages change whenever a heading marker is edited or deleted, a fragment is excluded, an annotation is deleted, or a book is re-imported (FR-037).
3. **A missing value and a substituted value are different facts.** Today `created_at` absorbs both the reading date and the learning date, and `"Unknown Author"` stands in for a source that reported no author — so the library cannot tell silence from data (FR-007, FR-008).

Key tensions to resolve:

1. **Interpretation**: where the marker grammar lives, and whether every client interprets it independently.
2. **Derivation**: whether chapters, tags and stitched passages are stored structures or read-time projections.
3. **Identity**: how an annotation is recognised once notes and markers are stripped out of `text`.
4. **Provenance**: whether the learning date and the reported reading date share one column, and whether a missing author is named.
5. **Upgrade**: how pre-existing libraries are reclassified without losing curation state or delivery history.

Alternatives rejected:

- **A Relego-specific marker dialect.** Inventing private prefixes would avoid matching anyone else's grammar, but it makes an existing Readwise library import with different meanings, so the fidelity the feature exists for is lost.
- **Persisted `chapters`, `stitch_groups` and `highlight_tags` tables.** Any cache needs an owner for every write path that changes it — import, marker edit, exclusion toggle, deletion — and a backfill for existing libraries; at this scale the invalidation risk costs more than the derivation it saves.
- **Deriving chapters or stitches in the web UI, the CLI or each composer.** Four independent implementations of the same algorithm is exactly the disagreement the spec forbids (SC-011, SC-013, SC-016).
- **Stripping `[my note] ` at read time instead of migrating it.** The prefix would stay the identity key and stay searchable, and the import would still have to decide what the prefix means (FR-055).
- **Keeping one `created_at` and treating it as the reading date.** It is wrong for every annotation imported without a date — the majority — and unrecoverable once written (FR-007, FR-009).
- **Naming a missing author with a sentinel or a translated default.** A generated value is indistinguishable from a real author of that name and forbids an honest "unknown" (FR-008).

This ADR builds on **ADR-003** (SQLite as the only storage engine), **ADR-005** (`My Clippings.txt` as the reading-file source) and **ADR-008** (`KoboReader.sqlite` as the device-database source). It partially revises ADR-005 and ADR-008 in one respect only: neither source prepends the `[my note] ` prefix any longer, and neither treats a note as a separate quote.

## Decision

### 1. The marker vocabulary is the annotation model, and it is interpreted on the server, once

A note is not free text. It is a small, closed grammar, interpreted identically for every ingestion path:

| Note | Interpretation |
|---|---|
| `.h1`, `.h2`, `.h3` (any case) | heading at that level; the passage becomes a chapter title |
| `.h1` followed by text on the same or the next line | heading at that level, remaining text is the chapter's section note |
| `.c1` … `.cN` | explicit stitch level for a fragment of one continuous passage |
| `.prob`, `.prob .review` | one or more tags; no commentary |
| `.prob a real comment` | tag `prob`, note `a real comment` |
| bare `.` (or `. `) | commentary carrying no passage — the dot stays in the note |
| `.h0`, `.h4`, `.c0` | heading- or stitch-shaped but not a valid level: the note is prose from that token on |
| `[my note] …` | legacy encoding: retired on upgrade, never recognised at import |

Leading tokens only, whitespace-separated, case-insensitive for matching and spelling-preserving for display. The first token that is not a marker starts the prose, so nothing after it is ever reinterpreted. The grammar lives in one place, `Relego.Core.Parsing.AnnotationMarkerParser`, which turns a note into `(Note, HeadingLevel, StitchLevel, Tags)`. `HighlightAggregator` attaches a source's note to the right passage — matched on book and position, never on note text, and a source that reports no position (Kobo) is matched on the passage the clipping is anchored to — and every client sends annotations **raw**: the CLI, the web portal, uploads, cloud sync and any future client transport the note as written, and the server classifies it. Nothing but that one parser decides what a dot-prefixed token means, and there is no setting to turn recognition off.

The deliberate exception: Relego-specific prefixes are **retired rather than added**. `[my note] ` disappears from the aggregator and from stored rows (§3) instead of gaining a sibling.

**Rationale**: this is Readwise's own Kindle grammar, so an existing Readwise user's library imports with the same meaning it had before the reader moved — the point of the vocabulary is fidelity to what the reader already wrote, not a Relego dialect they would have to learn. Interpreting markers in one server-side parser is what keeps web, CLI, email and any future client from disagreeing: a client that parsed markers itself would be a second, silently diverging definition of what a note *is*, and the surfaces would then disagree at exactly the moment the reader corrects a marker. A setting would create two incompatible meanings for the same stored note, which contradicts the same goal. Retiring the prefix rather than adding to it follows from the same reasoning: every Relego-only token is a permanent second meaning that no other tool shares, and the migration is what lets the library hold one meaning.

### 2. Chapters and stitched passages are derived on read, never persisted

There is no chapter table, no tag table and no stitch table. Two pure builders in `Relego.Core.Annotations` project them from a book's stored annotations in source order: `ChapterTreeBuilder` derives the table of contents (a heading annotation titles a chapter; parent is the nearest preceding heading of a strictly smaller level; annotations before the first heading belong to the unknown chapter; a table of contents exists only when at least one level-1 heading exists, FR-026), and `StitchedPassageBuilder` joins `.cN` fragments (a level-1 fragment opens a group, later levels append to it, an orphan `.cN` stays out of any group and surfaces its marker as a tag, FR-042).

Both sit behind a single service, `Relego.Server.Services.AnnotationContextService`, which is the only path from stored rows to presented annotations. It serves the REST API (`GET /highlights` with `bookId`, `chapterId` and `tag`, plus `GET /books/{id}/chapters` and `GET /tags`), and the CLI and the web portal are thin clients over those endpoints. The recap composers are the deliberate exception: they render the notes that come attached to the passages a candidate already selected (FR-033) and never build a chapter tree or a stitched passage, so no email surface acquires a second derivation.

Consequences that follow from deriving:

- Derivation is bounded by **one book's annotation count** per request, because a chapter tree cannot be computed from a page of annotations. The query is per book and indexed for source order; the page size contract only governs how much is returned, not what is read.
- Every fragment keeps its own row, position, note, tags, weight, exclusion and delivery history (FR-043). Stitching is a reading of the fragments, not a replacement for them, so an excluded fragment is omitted from the joined text while the rest still stitch (FR-044).
- Removing a marker on the device degrades gracefully: the annotation stays, its `heading_level` or `stitch_level` becomes `NULL`, and its followers fall back to the nearest remaining chapter or to the unknown chapter. No record is lost (SC-013).

**Rationale**: FR-037 requires the structure to be recomputed after *any* change — re-import, marker edit, level correction, exclusion toggle, deletion — and each of those paths would need its own invalidation rule against a persisted cache. Such a cache has no legitimate owner: records change under it from imports, deletions and curation edits, not from one writer that can be trusted to update it, and the failure mode is silent (a stale chapter tree), which is the worst kind for a feature whose whole value is that the structure is trustworthy. Derivation makes "two chapters with the same title stay distinct", "a re-levelled chapter re-parents its descendants" and "a deleted marker falls back to the nearest chapter" true by construction rather than by reconciliation. The single-user scale (≤ 20 000 annotations, ≤ 500 books) makes the cost of deriving on read comfortably smaller than the cost of being wrong.

### 3. Reported facts are stored as reported, and the legacy encoding is repaired exactly once

`created_at` means **when Relego learned about the row**, and nothing else. A new nullable `added_on` holds the date the **source reported**, or `NULL` when the source reported none — never the import time. `source_position` keeps the raw position string, with `position_kind` (`location` | `page`) and a parsed `position_start` for ordering. Silent fields stay silent: `authors.name` becomes nullable and the literal `"Unknown Author"` sentinel is dropped, with existing rows migrated to `NULL`, because a sentinel is indistinguishable from a real author of that name and forbids presenting "unknown" (FR-008, FR-009).

The same migration repairs the legacy encoding. The codebase's first data migration is guarded by a new `schema_migrations` marker table: one guarded migration splits the legacy `[my note] ` prefix out of `text` into `note` exactly once, then runs the marker parser on the remainder so a migrated `.h2` or `.prob` note means what a fresh import would mean. It preserves ids, books, authors, curation state, delivery history and provenance rows, reduces no visible record, never runs again on restart, and is never applied to records received afterwards (FR-053–FR-056, SC-008). The `authors` rebuild requires `PRAGMA foreign_keys` to be toggled **outside** the migration transaction — SQLite ignores the pragma inside one — and restored in a `finally`, because `books.author_id` and `excluded_authors.author_id` reference the rebuilt table.

**Rationale**: chapters need source order and recaps need a trustworthy "since when", and both depend on not confusing arrival with reading; the current fallback to `DateTimeOffset.UtcNow` quietly publishes the import time as a reading date for every annotation whose source reported none. Splitting the two facts is the only way to satisfy FR-007 without deleting history, and `NULL` is the honest representation of "unknown" in a column that already has one. Reclassifying the note during the migration rather than at read time means the reader's data is clean on disk and every consumer — API, CLI, web, both composers — is correct without a special case, while `text` stops carrying a marker that FR-055 forbids treating as one. The marker table is the smallest mechanism that makes a non-idempotent step run once and gives later features somewhere to record their own; relying on "no row is still prefixed" as the guard would re-run forever on a library whose notes were all deleted.

## Consequences

- **Annotation identity changes.** The unique index moves from `(user, book, text)` to `(user, book, text, coalesce(position, ''))` for passages, with a second partial index on `(user, book, note)` for note-only rows. Imports select-by-identity and update in place instead of `INSERT OR IGNORE`, so a re-import that adds a note, marker or tag updates the existing row and leaves weight, exclusion and delivery count untouched; two identical passages at different positions stay two rows.
- **`GET /highlights` gains filters** (`bookId`, `chapterId`, `tag`, `q`) and now resolves chapter and tag context **per book** rather than per page, because a table of contents cannot be derived from one page of annotations.
- **Two new read endpoints** — `GET /books/{id}/chapters` and `GET /tags` — and a new `relego highlights` CLI group (`list`, `chapters`) over exactly those endpoints.
- **The web highlight DTO gains** `note`, `tags`, `headingLevel`, chapter path, stitch fields, `position` and `addedOn`; `authorName` becomes nullable and both consumers render a localised "unknown" instead of a name that does not exist.
- **The first data migration in the codebase**, with a `schema_migrations` marker table, a dropped-and-recreated index, and the `PRAGMA foreign_keys` handling confined to `SchemaBootstrap`. It is one-way: rolling back means restoring the pre-upgrade `/data` volume that ADR-008 already asks readers to back up.
- **ADR-005 and ADR-008 now describe a `[my note] ` behaviour that no longer exists.** They remain Accepted because their decisions (which source to read, how to detect it, how to deliver) are unchanged; only the note handling is partially revised here, and they carry a forward link.
- **Sources that report no position** keep text-only identity, so indistinguishable records still collapse — the same rule the current code already applies, now stated rather than implicit. A source that changes its reported position for the same passage produces a second annotation.
- **`docs/ARCHITECTURE.md` and the import documentation** describe the annotation vocabulary and the derived-context model, since both are now promises the product makes to a reader's existing library.
