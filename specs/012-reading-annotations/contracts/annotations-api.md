# Contract: Annotations API

**Feature**: 012-reading-annotations | **Spec**: [../spec.md](../spec.md) | **Model**: [../data-model.md](../data-model.md)

All endpoints are unauthenticated (Constitution V), return JSON, and act on the implicit MVP user
resolved by `UserRepository.EnsureUserAsync()`. Existing behaviour and error shapes are preserved;
everything here is additive except the two fields that become nullable (`authorName`) and the note
that moves out of `text` — both consequences of [research.md](../research.md) R7 and R10.

---

## `GET /highlights` (extended)

One paginated, filterable list of annotations. This is the single read used by the web highlights
page and `relego highlights list`.

**Query parameters** (all optional)

| Name | Type | Default | Rules |
|------|------|---------|-------|
| `page` | int | 1 | `≥ 1`, else 422 `page` |
| `pageSize` | int | 50 | `1…200`, else 422 `pageSize` |
| `q` | string | – | case-insensitive search over passage text, note, book title, author name |
| `bookId` | int | – | restrict to one book (existing web behaviour, now server-side) |
| `chapterId` | int or `unknown` | – | restrict to one chapter: the `id` of the titling annotation, or the literal `unknown` for annotations before the first heading. Ignored when `bookId` is absent, because there is no book whose chapter tree could be resolved |
| `tag` | string | – | restrict to annotations carrying the tag, case-insensitive (FR-048, FR-049) |

`chapterId` and `tag` may be combined with each other and with `q`; the result is their conjunction.

**Response** `200 application/json` → `HighlightsResponse`

```jsonc
{
  "total": 812,          // matching annotations, after every filter
  "page": 1,
  "pageSize": 50,
  "items": [
    {
      "id": 4411,
      "bookId": 12,
      "authorId": 3,
      "text": "Simplicity is prerequisite for reliability.",
      "bookTitle": "Epigrams on Programming",
      "authorName": "Alan Perlis",        // null when the source reported no author (FR-008)

      "note": "the whole design principle in one line",  // null = no note (FR-011)
      "tags": ["prob", "review"],                        // always an array, [] = no tags (FR-046)

      "headingLevel": 2,      // null = not a chapter title (FR-018)
      "chapterId": 4409,      // null = unknown chapter (FR-022)
      "chapterPath": ["Foundations", "The Eight Fallacies"],

      "stitchGroupId": 4409,  // null = not stitched (FR-043)
      "stitchIndex": 0,
      "stitchedText": "first fragment … second fragment … third fragment", // null except on a group's first fragment

      "position": "Location 1234-1236",       // null = unknown (FR-006)
      "addedOn": "2026-03-02T21:14:00+00:00"  // null = unknown, never the import time (FR-007)
    }
  ]
}
```

**Field notes**

- `text` is the passage only. A note-only annotation has `"text": ""` and a `note`; it is a valid
  item and appears in listings like any other annotation (Story 1 AS-3).
- `chapterPath` is ordered root → leaf and always ends with the current chapter's title; it is `[]`
  for the unknown chapter.
- `stitchedText` is populated on the first fragment of a group only, so a page shows the joined
  passage once while the fragments beneath it stay individually readable (Story 6 AS-5).
- Chapter and stitch fields are resolved against the *whole* book, not the current page, so a page of
  50 annotations still knows its chapters (R9).
- Ordering within a book follows source order; across books it stays `id asc` as today.

**Errors**: `422` validation problem (`page`, `pageSize`, and `bookId ≤ 0`); `200` with an empty
`items` array and `total: 0` when a filter matches nothing.

---

## `GET /books/{id}/chapters` (new)

The table of contents for one book, or an explicit "this book has none".

**Response** `200 application/json`

```jsonc
{
  "bookId": 12,
  "hasTableOfContents": true,   // false when the book has no level-1 heading (FR-026)
  "chapters": [
    { "id": 4409, "title": "Foundations", "level": 1, "parentId": null,
      "note": null, "position": "Location 210", "addedOn": "2026-03-02T20:41:00+00:00",
      "annotationCount": 31 },
    { "id": 4420, "title": "The Eight Fallacies", "level": 2, "parentId": 4409,
      "note": "written in 1993 and still true", "position": "Location 980",
      "addedOn": null, "annotationCount": 9 }
  ],
  "unknownChapter": { "id": null, "title": "Unknown chapter", "level": 0, "parentId": null,
                      "note": null, "position": null, "addedOn": null, "annotationCount": 4 }
}
```

- `chapters` is empty and `hasTableOfContents` is `false` for a book with no headings, and also for a
  book whose only headings are level 2 or 3 (FR-026). The reader then sees no chapter navigation at
  all, rather than a navigation with one entry.
- `unknownChapter` is always present; its `id` is `null` and it is passed back as
  `chapterId=unknown` (Story 3).
- `annotationCount` includes descendant chapters, so a parent's count is the size of its section.
- Two chapters with the same title stay distinct, because identity is the annotation `id`.

**Errors**: `404` problem (`Book 12 not found.`) when the book does not exist or belongs to another
user.

---

## `GET /tags` (new)

Every tag in the library, for the cross-book filter (FR-049).

**Response** `200 application/json`

```jsonc
{
  "tags": [
    { "name": "prob", "count": 84 },
    { "name": "Review", "count": 12 }
  ]
}
```

- `name` is one representative spelling — the one from the lowest annotation `id` — so the reader
  sees their own words rather than a normalised form (FR-051).
- Matching is case-insensitive: filtering with `--tag review` also finds `Review`.
- Ordered by `count desc`, then `lower(name) asc`.
- Empty array when the library has no tags; the web UI then offers no tag filter (Story 7 AS-5).

---

## `DELETE /highlights/{id}` (unchanged)

Kept as-is. Deleting a fragment of a stitched passage leaves the rest stitched (by construction,
because stitching is derived), and deleting a chapter's titling annotation moves its annotations to
the nearest remaining chapter or to the unknown chapter (edge cases).

---

## Contract stability

| Consumer | Depends on |
|----------|-----------|
| `src/relego.web` | `GET /highlights` (+ filters), `GET /books`, `GET /books/{id}/chapters`, `GET /tags` |
| `src/Relego.Cli` | the same four, through `RelegoHttpClient` + `RelegoJsonContext` |
| `POST /imports` | unchanged shape (see [import-contract.md](import-contract.md)) |
| recap composers | not HTTP; they consume the same derived model in-process |

Every new DTO must be added to `RelegoJsonContext` (`[JsonSerializable]`), or the CLI's
source-generated serializer will not see it.
