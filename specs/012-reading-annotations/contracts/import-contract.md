# Contract: Import

**Feature**: 012-reading-annotations | **Spec**: [../spec.md](../spec.md) | **Decisions**: [../research.md](../research.md) R2, R5, R7, R10

Three import paths exist today and all three must produce byte-identical annotation records
(FR-038, FR-039). This document pins what each sends, what the server stores, and what the caller is
told.

| Path | Entry point | Producer | Request type |
|------|-------------|----------|--------------|
| Reading-file upload (web / API) | `POST /imports` | `UploadImportService` → `ClippingsParser` / `KoboReaderSource` | `SyncRequest` |
| CLI import | `relego import` | `ClippingsImportWorkflow` → same parsers | `SyncRequest` |
| Kindle cloud sync | `POST /sync/jobs/{id}/batches` | `KindleCloudProvider.Normalize` | `SyncRequest` |

All three converge on `SyncRepository.ImportAsync`, which is therefore the only writer of
`highlights` (one write path, FR-039).

---

## `SyncRequest` (extended, additive)

```jsonc
{
  "books": [
    {
      "title": "Epigrams on Programming",   // unchanged: exactly as parsed
      "author": "Alan Perlis",              // unchanged: null when the source reports none
      "highlights": [
        {
          "text": "Simplicity is prerequisite for reliability.",  // passage only, markers stripped by the client
          "addedOn": "2026-03-02T21:14:00+00:00",                 // unchanged: null when unknown
          "note": ".h2 The Eight Fallacies",                      // NEW, optional, raw note incl. markers
          "position": "Location 1234-1236"                        // NEW, optional, raw position
        }
      ]
    }
  ]
}
```

**Client responsibilities** (in `Relego.Core`, so every client gets them for free):

| Responsibility | Where |
|----------------|-------|
| Strip the legacy `[my note] ` prefix and attach a Kindle note clipping to its highlight by position | `HighlightAggregator` (R2) |
| Emit note-only annotations as `text: ""` + `note` when no highlight matches | `HighlightAggregator` (Story 1 AS-3) |
| Keep passage text exactly as reported, never trimmed of internal line breaks | `ClippingsParser` (FR-005) |
| Pass `position` through from the clippings metadata / Kobo source / notebook page | `ClippingsParser`, `KoboReaderSource`, `KindleCloudProvider` (FR-006) |
| Never send `created_at`; that is the server's own bookkeeping | — (FR-007) |

**Important**: clients send the note **raw** (marker tokens included). Marker interpretation happens
once, on the server, so a client can never change what `.h2` means and a re-import from a different
client classifies identically.

`note` and `position` are optional. A client that does not send them still imports passages; the
annotation simply has no note and no position (unknown, not invented — FR-009).

Kobo: `KoboReaderSource` already maps a Kobo `Annotation` to a note; it now sends it as `note`
instead of prefixing the text, and keeps sending `position: null` because Kobo's `Bookmark` table has
no equivalent (no position is invented).

Kindle cloud: `KindleCloudProvider.ToSyncRequest` maps `CloudSyncHighlight.Note` and
`.Location`, both of which the provider already collects and the current code discards. This is the
bug that makes FR-039 false today.

---

## `SyncResponse` (unchanged shape)

```jsonc
{ "newHighlights": 41, "duplicateHighlights": 3, "newBooks": 1, "newAuthors": 0 }
```

- `newHighlights` counts rows inserted with a new identity.
- `duplicateHighlights` counts annotations **already known** — both an identical re-import and an
  in-place update caused by an edited note, marker or tag. The number the reader sees stays
  "how many were already here", which is what FR-004 asks for; no new counter is introduced.
- Note clippings consumed by their highlight count as processed entries, and superseded versions of
  the same note count as duplicates (R2), so the totals still add up to the number of entries in the
  file.

## `ImportResponse` (unchanged)

`entriesProcessed` and `duplicatesInFile` keep their meaning over the same entry list, including the
note clippings that were attached rather than stored separately.

---

## Failure reporting (FR-003, FR-004)

Unchanged. `UploadImportService` reports each detected source independently; a source that cannot be
read produces its own actionable message and the other sources still report their outcome. No new
error code is introduced by this feature.

---

## What must NOT change

- A source file is opened read-only and is never modified, moved or deleted (FR-002, ADR-008).
- `weight` defaults to 3, `excluded` to 0, `delivery_count` to 0 on insert and is never reset by an
  update.
- Nothing is deleted by an import, ever.
- `POST /imports` keeps its multipart shape, its `source` detection and its status codes.
