# Contract: CLI

**Feature**: 012-reading-annotations | **Spec**: [../spec.md](../spec.md) (Story 8, FR-033, FR-049) | **Decisions**: [../research.md](../research.md) R9

The CLI reads the same four HTTP endpoints as the web portal, so parity (SC-015, SC-016) is a
property of the shared `AnnotationContextService`, not of duplicated rendering code. This document
pins the command surface and the exact text a user sees.

---

## New command group: `relego highlights`

Registered in `Program.cs` next to the existing branches, implemented under
`src/Relego.Cli/Commands/Highlights/`:

```
relego highlights list [options]
relego highlights chapters --book <id>
```

### `relego highlights list`

| Option | Type | Default | Maps to |
|--------|------|---------|---------|
| `--book <id>` | int | – | `GET /highlights?bookId=` |
| `--chapter <id\|unknown>` | string | – | `GET /highlights?chapterId=`; requires `--book` — without it the command fails with `--chapter requires --book <id>.` |
| `--tag <name>` | string | – | `GET /highlights?tag=`; matched case-insensitively |
| `--search <text>` | string | – | `GET /highlights?q=` |
| `--page <n>` | int | 1 | `GET /highlights?page=` |
| `--page-size <n>` | int | 50 | `GET /highlights?pageSize=` |

`--tag` and `--search` may be combined with `--book` and `--chapter`; the server applies their
conjunction.

**Output** — one block per annotation, in source order within a book:

```
4411  Epigrams on Programming — Alan Perlis
      ❝ Simplicity is prerequisite for reliability.
      ✎ the whole design principle in one line
      #prob #review
      Chapter: Foundations › The Eight Fallacies    Location 1234-1236    2026-03-02

4412  Epigrams on Programming — Alan Perlis
      ❝ first fragment … second fragment … third fragment
      (stitched from 3 highlights)

4413  Epigrams on Programming — Unknown author
      ❝ Epigrams on Programming
      ⬒ heading (level 2)
```

Rules the renderer must obey, because these are the parity assertions of SC-016:

- `❝` is the passage; a note-only annotation prints an empty passage line, never omits the block.
- `✎` appears only when there is a note; `#tag` only when there are tags.
- `Chapter:` appears only when the chapter is known; the unknown chapter is not printed as a
  pseudo-chapter, and `Location`/date are printed only when known.
- An unknown author prints as `Unknown author`, never as an empty string or the retired
  `"Unknown Author"` sentinel.
- A heading annotation prints its `⬒ heading (level N)` marker so it is distinguishable from a
  passage (FR-032), and a stitched group prints its joined text on the first fragment only, followed
  by its fragment count (FR-045).
- A trailing line reports `812 annotations · page 1 of 17`. With zero results, the command prints
  `No annotations found.` and exits `0`, because an empty result is not a failure.

`--json` is out of scope: no existing command offers machine output, and this feature adds none.

**Exit codes**: `0` on success (including empty results), `1` on a transport or 4xx/5xx failure, with
the server's message surfaced through Spectre's markup-escaped error line, matching the other
commands.

### `relego highlights chapters`

| Option | Type | Required |
|--------|------|----------|
| `--book <id>` | int | yes |

Maps to `GET /books/{id}/chapters`.

```
Foundations
  └ The Eight Fallacies                       9 annotations
  ├ (rest of Foundations)                    31 annotations
Unknown chapter                               4 annotations
```

- Level-1 headings are flush left; each deeper level indents two spaces and prefixes `└`.
- `annotationCount` is printed per chapter and includes descendants, matching the API.
- When `hasTableOfContents` is `false`, the command prints `Epigrams on Programming has no chapters
  yet. Add .h1 markers to create them.` — an actionable line, not an empty table (FR-026).
- `Unknown chapter` is always listed last, and only when it has at least one annotation.

---

## What the CLI must reuse, not re-derive

| Concern | Reused from |
|---------|-------------|
| marker parsing, note attachment, stitching, chapter trees | `Relego.Core.Parsing.AnnotationMarkerParser`, `Relego.Core.Annotations` — the CLI performs **no** marker interpretation of its own |
| HTTP calls | `RelegoHttpClient` (typed client with the shared resilience handler) |
| JSON | `RelegoJsonContext` — every new DTO needs a `[JsonSerializable]` entry |

`relego import` needs no new flags: it already sends the whole clipping file, and the enriched
`SyncRequest` (see [import-contract.md](import-contract.md)) now carries notes and positions
automatically.

---

## Backwards compatibility

`relego status`, `relego import`, `relego exclude *`, `relego weight *`, `relego recap`, and
`relego config *` are untouched. The new group is additive, so scripted invocations keep working.
