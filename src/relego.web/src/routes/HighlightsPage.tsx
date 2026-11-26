import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { AsyncSection, EmptyState, Tag, WeightPips } from "../components/ui";
import { api } from "../lib/api";
import { copyToClipboard } from "../lib/clipboard";
import {
  describeChapterPath,
  describeHeadingLevel,
  formatCount,
  formatReadingDate,
  formatReadingPosition,
  plural,
  UNKNOWN_AUTHOR,
} from "../lib/format";
import { useListKeys } from "../lib/listkeys";
import { useDebounced, useSearch } from "../lib/search";
import { useToasts } from "../lib/toasts";
import type {
  BookItem,
  ChapterItem,
  HighlightItem,
  TagSummary,
  UnknownChapterItem,
} from "../lib/types";

/** The chapter the list is narrowed to; `null` is the whole book. */
type ChapterFilter = number | "unknown" | null;

/** Stable identity, so downstream `useMemo`s do not re-run on every render. */
const NO_CHAPTERS: ChapterItem[] = [];
const NO_TAGS: TagSummary[] = [];

/**
 * One line of the list. A stitched passage is read as a single quoted item, so it is one line
 * even though it owns several annotations; its fragments stay reachable when it is opened.
 */
type ListRow =
  | { kind: "annotation"; item: HighlightItem }
  | {
      kind: "passage";
      id: number;
      lead: HighlightItem;
      fragments: HighlightItem[];
    };

type PassageRow = Extract<ListRow, { kind: "passage" }>;

/**
 * One chapter and the highlights hanging on it. A chapter is a linkage the page can draw, so
 * it is drawn as a rail and its name is stated once at the head — not repeated as a crumb on
 * every card, which is what forced the reader to decode metadata to see the connection.
 */
type Region = {
  /** Path folded into a key, so two chapters that share a title cannot merge. */
  key: string;
  /** Root to leaf; empty when the annotation sits outside every chapter. */
  path: string[];
  /** The annotation that titles the chapter, promoted to the region head. */
  head: { item: HighlightItem; index: number } | null;
  stops: { row: ListRow; index: number }[];
};

/** The annotation a row's curation keys act on: the fragment that opens a passage. */
function primaryOf(row: ListRow): HighlightItem {
  return row.kind === "passage" ? row.lead : row.item;
}

/** Tags as the reader wrote them, first spelling wins, compared without regard to case. */
function unionTags(items: HighlightItem[]): string[] {
  const seen = new Set<string>();
  const tags: string[] = [];

  for (const item of items) {
    for (const tag of item.tags) {
      const key = tag.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      tags.push(tag);
    }
  }

  return tags;
}

/** Tags are matched without regard to letter case, exactly as the tag filter does. */
function sameTag(tag: string, other: string | null): boolean {
  return other !== null && tag.toLowerCase() === other.toLowerCase();
}

/** A drag that selects text also fires a click; expanding then would swallow the selection. */
function isSelectingWithin(container: HTMLElement): boolean {
  const selection = window.getSelection();
  if (!selection || selection.isCollapsed) return false;
  const anchor = selection.anchorNode;
  return anchor !== null && container.contains(anchor);
}

export function HighlightsPage() {
  const params = useParams<{ bookId?: string }>();
  const bookId = params.bookId ? Number(params.bookId) : null;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { push } = useToasts();
  const { query } = useSearch();
  const debounced = useDebounced(query);

  const [openId, setOpenId] = useState<number | null>(null);
  const [confirmingId, setConfirmingId] = useState<number | null>(null);
  const [cursor, setCursor] = useState(0);
  const [chapterFilter, setChapterFilter] = useState<ChapterFilter>(null);
  const [tagFilter, setTagFilter] = useState<string | null>(null);
  const listRef = useRef<HTMLDivElement>(null);

  // The server narrows the list, so the whole book arrives in one page rather than
  // being filtered here: chapter paths are derived from every annotation in the book.
  const scope = useMemo(() => {
    if (bookId === null && tagFilter === null) return undefined;

    return {
      ...(bookId === null ? {} : { bookId }),
      ...(bookId === null || chapterFilter === null
        ? {}
        : { chapterId: chapterFilter }),
      ...(tagFilter === null ? {} : { tag: tagFilter }),
    };
  }, [bookId, chapterFilter, tagFilter]);

  const highlights = useQuery({
    queryKey: ["highlights", debounced, scope],
    queryFn: () => api.highlights(debounced || undefined, 1, 200, scope),
    retry: false,
  });

  // Every tag in the library, so the filter offers the reader's own spellings rather than
  // whatever this one book happens to contain.
  const tags = useQuery({
    queryKey: ["tags"],
    queryFn: api.tags,
    retry: false,
  });

  const toc = useQuery({
    queryKey: ["chapters", bookId],
    queryFn: async () => api.chapters(bookId as number),
    enabled: bookId !== null,
    retry: false,
  });

  const books = useQuery({
    queryKey: ["books", ""],
    queryFn: () => api.books(),
    retry: false,
  });
  const weights = useQuery({
    queryKey: ["weights"],
    queryFn: api.weights,
    retry: false,
  });
  const exclusions = useQuery({
    queryKey: ["exclusions"],
    queryFn: api.exclusions,
    retry: false,
  });

  const book: BookItem | undefined = bookId
    ? books.data?.items.find((candidate) => candidate.id === bookId)
    : undefined;

  const weightById = useMemo(() => {
    const map = new Map<number, number>();
    for (const entry of weights.data ?? []) map.set(entry.id, entry.weight);
    return map;
  }, [weights.data]);

  const excludedIds = useMemo(
    () => new Set((exclusions.data?.highlights ?? []).map((entry) => entry.id)),
    [exclusions.data],
  );

  const items = useMemo(() => {
    const all = highlights.data?.items ?? [];
    return bookId ? all.filter((item) => item.bookId === bookId) : all;
  }, [highlights.data, bookId]);

  // Stitched fragments are stored as their own annotations; the list reads them as one quoted
  // item, in the order their markers were read. A passage of one is the annotation it is.
  const rows = useMemo<ListRow[]>(() => {
    const built: ListRow[] = [];
    const passages = new Map<number, PassageRow>();

    for (const item of items) {
      if (item.stitchGroupId === null) {
        built.push({ kind: "annotation", item });
        continue;
      }

      const open = passages.get(item.stitchGroupId);
      if (open) {
        open.fragments.push(item);
        continue;
      }

      const row = {
        kind: "passage" as const,
        id: item.stitchGroupId,
        lead: item,
        fragments: [item],
      };
      passages.set(item.stitchGroupId, row);
      built.push(row);
    }

    return built.flatMap((row): ListRow[] => {
      if (row.kind !== "passage") return [row];

      // Each fragment reports its own index; the list order is not the reading order.
      row.fragments.sort(
        (left, right) => (left.stitchIndex ?? 0) - (right.stitchIndex ?? 0),
      );

      return row.fragments.length < 2
        ? [{ kind: "annotation", item: row.lead }]
        : [row];
    });
  }, [items]);

  // Grouping happens here, not on the server: the chapter of a highlight arrives with it, and
  // rebuilding the tree as rails is a reading concern. Library-wide there is no single book's
  // structure to draw, so that list stays flat.
  const regions = useMemo<Region[]>(() => {
    if (bookId === null) return [];

    const byPath = new Map<string, Region>();
    const ordered: Region[] = [];
    const remainder: Region[] = [];

    rows.forEach((row, index) => {
      const path = primaryOf(row).chapterPath;
      const key = path.join("\u0000");

      let region = byPath.get(key);
      if (!region) {
        region = { key, path, head: null, stops: [] };
        byPath.set(key, region);
        // Annotations outside every chapter are the book's remainder, so they close the list
        // the same way the contents list puts the unknown chapter last.
        if (path.length === 0) remainder.push(region);
        else ordered.push(region);
      }

      const lead = primaryOf(row);
      // A chapter is titled by the annotation that carries its `.hN`, so that row is the head.
      // Rendering it again as a stop would say the chapter's name twice.
      if (
        region.head === null &&
        row.kind === "annotation" &&
        lead.headingLevel !== null &&
        path.length > 0 &&
        lead.text === path[path.length - 1]
      ) {
        region.head = { item: lead, index };
        return;
      }

      region.stops.push({ row, index });
    });

    return [...ordered, ...remainder];
  }, [rows, bookId]);

  // A book with no structure yields a single region spanning everything, which is a flat list
  // wearing a heading. Drawing rails there would promise a connection the book does not have.
  const grouped =
    regions.length > 0 && (regions.length > 1 || regions[0]!.path.length > 0);

  const tagItems = tags.data?.tags ?? NO_TAGS;

  const chapters = useMemo(
    () => (toc.data?.hasTableOfContents ? toc.data.chapters : NO_CHAPTERS),
    [toc.data],
  );
  // Only children add to the total: a parent already counts its descendants.
  const bookTotal = useMemo(
    () =>
      chapters
        .filter((chapter) => chapter.parentId === null)
        .reduce((sum, chapter) => sum + chapter.annotationCount, 0) +
      (toc.data?.unknownChapter.annotationCount ?? 0),
    [chapters, toc.data],
  );

  const activeChapter: ChapterItem | UnknownChapterItem | null =
    chapterFilter === null
      ? null
      : chapterFilter === "unknown"
        ? (toc.data?.unknownChapter ?? null)
        : (chapters.find((chapter) => chapter.id === chapterFilter) ?? null);

  useEffect(() => {
    setCursor(0);
    setOpenId(null);
  }, [debounced, bookId, chapterFilter, tagFilter]);

  // A chapter belongs to one book; carrying the filter into another would show an
  // empty list with no visible cause.
  useEffect(() => {
    setChapterFilter(null);
  }, [bookId]);

  // A pending delete confirmation belongs to one open row; collapsing or moving
  // to another row must not leave a live "Yes, delete" behind.
  useEffect(() => {
    setConfirmingId(null);
  }, [openId]);

  function refreshAll() {
    void queryClient.invalidateQueries({ queryKey: ["highlights"] });
    void queryClient.invalidateQueries({ queryKey: ["tags"] });
    void queryClient.invalidateQueries({ queryKey: ["exclusions"] });
    void queryClient.invalidateQueries({ queryKey: ["weights"] });
    void queryClient.invalidateQueries({ queryKey: ["books"] });
    void queryClient.invalidateQueries({ queryKey: ["status"] });
  }

  function reportError(error: unknown) {
    push(error instanceof Error ? error.message : "That didn't work.", {
      tone: "bad",
    });
  }

  async function copyHighlight(text: string) {
    const copied = await copyToClipboard(text);
    push(copied ? "Highlight copied." : "Couldn't copy that highlight.", {
      tone: copied ? "ok" : "bad",
    });
  }

  const setWeight = useMutation({
    mutationFn: ({ id, weight }: { id: number; weight: number }) =>
      api.setWeight(id, weight),
    onSuccess: refreshAll,
    onError: reportError,
  });

  const toggleHighlight = useMutation({
    mutationFn: ({ id, excluded }: { id: number; excluded: boolean }) =>
      excluded ? api.include("highlights", id) : api.exclude("highlights", id),
    onSuccess: (_result, variables) => {
      refreshAll();
      push(
        variables.excluded
          ? "Highlight is back in your recaps."
          : "Highlight won't appear in recaps.",
        {
          action: {
            label: "Undo",
            run: () =>
              toggleHighlight.mutate({
                id: variables.id,
                excluded: !variables.excluded,
              }),
          },
        },
      );
    },
    onError: reportError,
  });

  const removeHighlight = useMutation({
    mutationFn: (id: number) => api.deleteHighlight(id),
    onSuccess: () => {
      setConfirmingId(null);
      refreshAll();
      push("Highlight deleted. Re-import the file to bring it back.");
    },
    onError: reportError,
  });

  const toggleBook = useMutation({
    mutationFn: ({ id, excluded }: { id: number; excluded: boolean }) =>
      excluded ? api.include("books", id) : api.exclude("books", id),
    onSuccess: (_result, variables) => {
      refreshAll();
      push(
        variables.excluded
          ? "Book is back in your recaps."
          : "Book excluded from recaps.",
      );
    },
    onError: reportError,
  });

  const toggleAuthor = useMutation({
    mutationFn: ({ id, excluded }: { id: number; excluded: boolean }) =>
      excluded ? api.include("authors", id) : api.exclude("authors", id),
    onSuccess: (_result, variables) => {
      refreshAll();
      push(
        variables.excluded
          ? "Author is back in your recaps."
          : "Author excluded from recaps.",
      );
    },
    onError: reportError,
  });

  function focusCard(index: number) {
    const clamped = Math.max(0, Math.min(index, rows.length - 1));
    setCursor(clamped);
    // Regions reorder the rows on screen (a chapter's remainder closes the list), so DOM order
    // is no longer row order. Each summary carries its row index instead of relying on it.
    const cards = listRef.current?.querySelectorAll<HTMLElement>(
      ".hl-summary[data-row]",
    );
    Array.from(cards ?? [])
      .find((card) => card.dataset.row === String(clamped))
      ?.focus();
  }

  useListKeys({
    count: rows.length,
    cursor,
    containerRef: listRef,
    focusAt: focusCard,
    actions: {
      e: (index) => {
        const row = rows[index];
        if (row) {
          const item = primaryOf(row);
          toggleHighlight.mutate({
            id: item.id,
            excluded: excludedIds.has(item.id),
          });
        }
      },
      ...Object.fromEntries(
        [1, 2, 3, 4, 5].map((weight) => [
          String(weight),
          (index: number) => {
            const row = rows[index];
            if (row) setWeight.mutate({ id: primaryOf(row).id, weight });
          },
        ]),
      ),
    },
  });

  // Escape is contextual to the focused card, so it stays here rather than moving to the
  // page-level layer where it would collide with dialogs and the search field.
  function onCardKeyDown(event: React.KeyboardEvent, item: HighlightItem) {
    if (event.key !== "Escape") return;

    if (confirmingId === item.id) {
      event.preventDefault();
      setConfirmingId(null);
    } else if (openId === item.id) {
      event.preventDefault();
      setOpenId(null);
    }
  }

  /**
   * One annotation. Inside a chapter region it renders as a stop on that chapter's rail; the
   * annotation that titles the chapter renders as the region head instead, where the chapter's
   * name and the count of what hangs below it replace the crumb every card used to repeat.
   */
  function annotationCard(
    item: HighlightItem,
    index: number,
    region?: { ancestors: string[] },
  ) {
    const open = openId === item.id;
    const excluded = excludedIds.has(item.id);
    const weight = weightById.get(item.id) ?? 3;

    return (
      <article
        className={region ? "hl hl--region-head" : "hl"}
        key={item.id}
        data-open={open}
      >
        {/* A div rather than a button: button labels can't be selected, and the quote
            is exactly what people want to drag-select and copy. */}
        <div
          className="hl-summary"
          role="button"
          data-row={index}
          aria-expanded={open}
          aria-controls={`hlb-${item.id}`}
          tabIndex={index === cursor ? 0 : -1}
          onFocus={() => setCursor(index)}
          onClick={(event) => {
            if (isSelectingWithin(event.currentTarget)) return;
            setOpenId(open ? null : item.id);
          }}
          onKeyDown={(event) => {
            if (event.key === "Enter" || event.key === " ") {
              event.preventDefault();
              setOpenId(open ? null : item.id);
              return;
            }
            onCardKeyDown(event, item);
          }}
        >
          <span>
            {item.headingLevel !== null ? (
              <span className="hl-heading" data-level={item.headingLevel}>
                <span className="hl-eyebrow">
                  {describeHeadingLevel(item.headingLevel)}
                </span>
                <span className="hl-quote hl-quote--heading">{item.text}</span>
              </span>
            ) : item.text ? (
              <span className="hl-quote">{item.text}</span>
            ) : null}
            {item.note ? (
              <span
                className={
                  item.headingLevel !== null
                    ? "hl-note hl-note--summary"
                    : "hl-note"
                }
              >
                <span className="sr-only">
                  {item.headingLevel !== null ? "Section summary: " : "Note: "}
                </span>
                {item.note}
              </span>
            ) : null}
            <span className="hl-meta">
              {book ? null : (
                <span className="subtle">
                  {`${item.bookTitle} · ${item.authorName ?? UNKNOWN_AUTHOR}`}
                </span>
              )}
              {/* Only the library-wide list shows a path per item: inside a book the chapter
                  is the rail the item hangs from. The head names it and counts what it holds. */}
              {region === undefined &&
              book === undefined &&
              item.chapterPath.length > 0 &&
              item.headingLevel === null ? (
                <span className="subtle hl-crumb">
                  <span className="sr-only">Chapter: </span>
                  {describeChapterPath(item.chapterPath)}
                </span>
              ) : null}
              {region && region.ancestors.length > 0 ? (
                <span className="subtle hl-crumb">
                  <span className="sr-only">Part of: </span>
                  {describeChapterPath(region.ancestors)}
                </span>
              ) : null}

              {excluded ? <Tag tone="excluded">Excluded</Tag> : null}
              {/* Tags are shown as the reader spelled them, and name the filter that
                  would narrow the list to them. */}
              {item.tags.length > 0 ? (
                <span className="hl-tags">
                  {item.tags.map((tag) => (
                    <span
                      className="hl-tag"
                      key={tag}
                      data-active={sameTag(tag, tagFilter)}
                    >
                      {tag}
                    </span>
                  ))}
                </span>
              ) : null}
            </span>
          </span>
          <span className="hl-side">
            <WeightPips value={weight} />
          </span>
        </div>

        <div className="hl-body" id={`hlb-${item.id}`} hidden={!open}>
          <div className="hl-actions">
            <span className="seg-label" id={`w-${item.id}`}>
              Weight
            </span>
            <div className="seg" role="group" aria-labelledby={`w-${item.id}`}>
              {[1, 2, 3, 4, 5].map((step) => (
                <button
                  key={step}
                  type="button"
                  aria-pressed={weight === step}
                  onClick={() =>
                    setWeight.mutate({ id: item.id, weight: step })
                  }
                >
                  {step}
                </button>
              ))}
            </div>
            <button
              className="btn btn--sm"
              type="button"
              onClick={() => toggleHighlight.mutate({ id: item.id, excluded })}
            >
              {excluded ? "Include again" : "Exclude highlight"}
            </button>
            <button
              className="btn btn--sm"
              type="button"
              aria-label="Copy highlight text"
              onClick={() => void copyHighlight(item.text)}
            >
              Copy
            </button>
            {/* Deleting is not reversible on the server, so it takes two
                deliberate steps rather than one stray click. */}
            {confirmingId === item.id ? (
              <span
                className="confirm"
                role="group"
                aria-label="Confirm delete"
              >
                <span className="confirm-q">Delete permanently?</span>
                <button
                  className="btn btn--sm btn--danger"
                  type="button"
                  disabled={removeHighlight.isPending}
                  onClick={() => removeHighlight.mutate(item.id)}
                >
                  {removeHighlight.isPending ? "Deleting…" : "Yes, delete"}
                </button>
                <button
                  className="btn btn--sm btn--ghost"
                  type="button"
                  onClick={() => setConfirmingId(null)}
                >
                  Keep
                </button>
              </span>
            ) : (
              <button
                className="btn btn--sm btn--danger"
                type="button"
                onClick={() => setConfirmingId(item.id)}
              >
                Delete
              </button>
            )}
          </div>
          {/* Where and when the source said this was marked. Reference data, so it
              closes the card; a value the source never reported keeps its row and says
              it is unknown instead of vanishing (FR-006, FR-007). */}
          <dl className="hl-provenance">
            <div>
              <dt>Position</dt>
              <dd data-unknown={item.position === null}>
                {formatReadingPosition(item.position)}
              </dd>
            </div>
            <div>
              <dt>Added</dt>
              <dd data-unknown={item.addedOn === null}>
                {formatReadingDate(item.addedOn)}
              </dd>
            </div>
          </dl>
        </div>
      </article>
    );
  }

  function rowCard(row: ListRow, index: number) {
    return row.kind === "passage"
      ? passageCard(row, index)
      : annotationCard(row.item, index);
  }

  /**
   * A stitched passage reads as one quoted item, because that is how it was read. Opening it
   * hands back the fragments: each keeps its own note, position and curation controls, so
   * nothing the reader wrote is folded away.
   */
  function passageCard(row: PassageRow, index: number) {
    const { lead, fragments } = row;
    const open = openId === lead.id;
    const weight = weightById.get(lead.id) ?? 3;
    // The server joins the passage so every client shows the same text; the local join is
    // only a fallback for a server that predates stitching.
    const passage =
      lead.stitchedText ?? fragments.map((part) => part.text).join(" … ");
    const parts = passage.split(" … ");
    const passageTags = unionTags(fragments);
    const excludedCount = fragments.filter((item) =>
      excludedIds.has(item.id),
    ).length;

    function fragmentActions(item: HighlightItem) {
      const fragmentExcluded = excludedIds.has(item.id);
      const fragmentWeight = weightById.get(item.id) ?? 3;

      return (
        <div className="hl-actions">
          <span className="seg-label" id={`w-${item.id}`}>
            Weight
          </span>
          <div className="seg" role="group" aria-labelledby={`w-${item.id}`}>
            {[1, 2, 3, 4, 5].map((step) => (
              <button
                key={step}
                type="button"
                aria-pressed={fragmentWeight === step}
                onClick={() => setWeight.mutate({ id: item.id, weight: step })}
              >
                {step}
              </button>
            ))}
          </div>
          <button
            className="btn btn--sm"
            type="button"
            onClick={() =>
              toggleHighlight.mutate({
                id: item.id,
                excluded: fragmentExcluded,
              })
            }
          >
            {fragmentExcluded ? "Include again" : "Exclude fragment"}
          </button>
          <button
            className="btn btn--sm"
            type="button"
            aria-label="Copy fragment text"
            onClick={() => void copyHighlight(item.text)}
          >
            Copy
          </button>
          {confirmingId === item.id ? (
            <span className="confirm" role="group" aria-label="Confirm delete">
              <span className="confirm-q">Delete permanently?</span>
              <button
                className="btn btn--sm btn--danger"
                type="button"
                disabled={removeHighlight.isPending}
                onClick={() => removeHighlight.mutate(item.id)}
              >
                {removeHighlight.isPending ? "Deleting…" : "Yes, delete"}
              </button>
              <button
                className="btn btn--sm btn--ghost"
                type="button"
                onClick={() => setConfirmingId(null)}
              >
                Keep
              </button>
            </span>
          ) : (
            <button
              className="btn btn--sm btn--danger"
              type="button"
              onClick={() => setConfirmingId(item.id)}
            >
              Delete
            </button>
          )}
        </div>
      );
    }

    return (
      <article
        className="hl hl--stitch"
        key={`passage-${row.id}`}
        data-open={open}
      >
        <div
          className="hl-summary"
          role="button"
          data-row={index}
          aria-expanded={open}
          aria-controls={`hlb-${lead.id}`}
          tabIndex={index === cursor ? 0 : -1}
          onFocus={() => setCursor(index)}
          onClick={(event) => {
            if (isSelectingWithin(event.currentTarget)) return;
            setOpenId(open ? null : lead.id);
          }}
          onKeyDown={(event) => {
            if (event.key === "Enter" || event.key === " ") {
              event.preventDefault();
              setOpenId(open ? null : lead.id);
              return;
            }
            onCardKeyDown(event, lead);
          }}
        >
          <span>
            <span className="hl-quote hl-quote--passage">
              {parts.map((part, position) => (
                <Fragment key={part + position}>
                  {position > 0 ? (
                    <span className="hl-stitch-sep" aria-hidden="true">
                      …
                    </span>
                  ) : null}
                  {part}
                </Fragment>
              ))}
            </span>
            <span className="hl-meta">
              {book ? null : (
                <span className="subtle">
                  {`${lead.bookTitle} · ${lead.authorName ?? UNKNOWN_AUTHOR}`}
                </span>
              )}
              {/* Inside a book the chapter is the rail this hangs from, so the path is stated
                  once at the region head instead of on every item. */}
              {book === undefined && lead.chapterPath.length > 0 ? (
                <span className="subtle hl-crumb">
                  <span className="sr-only">Chapter: </span>
                  {describeChapterPath(lead.chapterPath)}
                </span>
              ) : null}
              <span className="hl-stitch-count">
                {plural(fragments.length, "fragment")} stitched
              </span>
              {excludedCount > 0 ? (
                <Tag tone="excluded">
                  {excludedCount === fragments.length
                    ? "Excluded"
                    : `${excludedCount} of ${fragments.length} excluded`}
                </Tag>
              ) : null}
              {passageTags.length > 0 ? (
                <span className="hl-tags">
                  {passageTags.map((tag) => (
                    <span
                      className="hl-tag"
                      key={tag}
                      data-active={sameTag(tag, tagFilter)}
                    >
                      {tag}
                    </span>
                  ))}
                </span>
              ) : null}
            </span>
          </span>
          <span className="hl-side">
            <WeightPips value={weight} />
          </span>
        </div>

        <div className="hl-body" id={`hlb-${lead.id}`} hidden={!open}>
          <p className="subtle hl-stitch-intro">
            {plural(fragments.length, "fragment")} stitched into one passage.
            Each keeps its own note, position and weight.
          </p>
          <ol className="hl-fragments">
            {fragments.map((item, position) => (
              <li className="hl-fragment" key={item.id}>
                <span className="hl-fragment-mark" aria-hidden="true">
                  {position + 1}
                </span>
                <div className="hl-fragment-body">
                  <span className="hl-quote">{item.text}</span>
                  {item.note ? (
                    <span className="hl-note">
                      <span className="sr-only">Note: </span>
                      {item.note}
                    </span>
                  ) : null}
                  <dl className="hl-provenance">
                    <div>
                      <dt>Position</dt>
                      <dd data-unknown={item.position === null}>
                        {formatReadingPosition(item.position)}
                      </dd>
                    </div>
                    <div>
                      <dt>Added</dt>
                      <dd data-unknown={item.addedOn === null}>
                        {formatReadingDate(item.addedOn)}
                      </dd>
                    </div>
                  </dl>
                  {fragmentActions(item)}
                </div>
              </li>
            ))}
          </ol>
          {/* Exclusion and weight are per annotation, so they belong to the fragments below.
              `e` and `1`–`5` on a passage row act on its first fragment, which is the
              annotation the group's id and text come from. */}
          <div className="hl-actions">
            <button
              className="btn btn--sm"
              type="button"
              aria-label="Copy the whole passage"
              onClick={() => void copyHighlight(passage)}
            >
              Copy passage
            </button>
          </div>
        </div>
      </article>
    );
  }

  const heading = book ? book.title : "Highlights";
  const isLoading =
    highlights.isPending || weights.isPending || exclusions.isPending;

  return (
    <section className="view" aria-labelledby="hl-h">
      {book ? (
        <nav className="breadcrumb" aria-label="Breadcrumb">
          <button
            className="btn btn--ghost btn--sm"
            type="button"
            onClick={() => navigate("/app")}
          >
            Library
          </button>
          <span aria-hidden="true">/</span>
          <b>{book.title}</b>
        </nav>
      ) : null}

      <div className="view-head">
        <div>
          <h1 id="hl-h">{heading}</h1>
          <p className="muted">
            {book ? (
              <>
                {book.authorName ?? UNKNOWN_AUTHOR} ·{" "}
                {plural(items.length, "highlight")}
                {activeChapter ? (
                  <>
                    {" in "}
                    <b>{activeChapter.title}</b>
                  </>
                ) : null}{" "}
                ·{" "}
                {book.excluded ? (
                  <Tag tone="excluded">Excluded</Tag>
                ) : (
                  <Tag tone="ok">In recaps</Tag>
                )}
              </>
            ) : debounced ? (
              `${plural(items.length, "highlight")} matching “${debounced}”.`
            ) : (
              "Every highlight Relego knows about. Weight the ones you want to see more often."
            )}
          </p>
        </div>

        <div className="actions">
          {book ? (
            <>
              <button
                className="btn btn--sm"
                type="button"
                onClick={() =>
                  toggleBook.mutate({ id: book.id, excluded: book.excluded })
                }
              >
                {book.excluded ? "Include book" : "Exclude book"}
              </button>
              <button
                className="btn btn--sm"
                type="button"
                onClick={() =>
                  toggleAuthor.mutate({
                    id: book.authorId,
                    excluded: book.authorExcluded,
                  })
                }
              >
                {book.authorExcluded ? "Include author" : "Exclude author"}
              </button>
            </>
          ) : null}
          {/* Sized to match its neighbours: full size on its own, small beside the book actions. */}
          <button
            className={book ? "btn btn--sm" : "btn"}
            type="button"
            onClick={() => navigate("/app/import")}
          >
            Import highlights
          </button>
        </div>
      </div>

      {toc.data ? (
        <TableOfContents
          chapters={chapters}
          unknown={toc.data.unknownChapter}
          total={bookTotal}
          selected={chapterFilter}
          onSelect={setChapterFilter}
        />
      ) : null}

      {/* A library with no tags offers no tag filter, rather than an empty row that implies
          tagging is a thing this reader has forgotten to do (Story 7 AS-5). */}
      {tagItems.length > 0 ? (
        <div className="tag-filter" role="group" aria-label="Filter by tag">
          <span className="seg-label">Tag</span>
          <div className="tag-filter-chips">
            {tagItems.map((tag) => {
              const active = sameTag(tag.name, tagFilter);

              return (
                <button
                  className="tag-filter-chip"
                  key={tag.name}
                  type="button"
                  aria-pressed={active}
                  data-active={active}
                  onClick={() => {
                    setTagFilter(active ? null : tag.name);
                    setOpenId(null);
                  }}
                >
                  {tag.name}
                  <span className="tag-filter-count">{tag.count}</span>
                </button>
              );
            })}
          </div>
        </div>
      ) : null}

      <AsyncSection
        isLoading={isLoading}
        error={highlights.error ?? weights.error ?? exclusions.error}
        onRetry={() => void highlights.refetch()}
        isEmpty={items.length === 0}
        empty={
          tagFilter !== null ? (
            <EmptyState
              title="Nothing tagged that"
              action={
                <button
                  className="btn btn--sm"
                  type="button"
                  onClick={() => setTagFilter(null)}
                >
                  Show every tag
                </button>
              }
            >
              No highlight tagged “{tagFilter}”
              {activeChapter ? ` in “${activeChapter.title}”` : ""}
              {debounced ? `, and none of those match “${debounced}”` : ""}.
            </EmptyState>
          ) : activeChapter ? (
            <EmptyState
              title="No highlights here"
              action={
                <button
                  className="btn btn--sm"
                  type="button"
                  onClick={() => setChapterFilter(null)}
                >
                  Show the whole book
                </button>
              }
            >
              {debounced
                ? `Nothing in “${activeChapter.title}” matches “${debounced}”.`
                : toc.data?.hasTableOfContents
                  ? `“${activeChapter.title}” has nothing of its own. Pick another entry in the contents above.`
                  : `“${activeChapter.title}” has nothing of its own yet.`}
            </EmptyState>
          ) : debounced ? (
            <EmptyState title="No highlights match that search">
              Nothing matches “{debounced}”. Try a shorter word, or clear the
              search box.
            </EmptyState>
          ) : (
            <EmptyState
              title="No highlights yet"
              action={
                <button
                  className="btn btn--primary"
                  type="button"
                  onClick={() => navigate("/app/import")}
                >
                  Import highlights
                </button>
              }
            >
              Connect your Kindle or Kobo by USB and drop its highlight file in.
              Relego takes it from there.
            </EmptyState>
          )
        }
      >
        <p className="subtle hint-line">
          <kbd>j</kbd> <kbd>k</kbd> move · <kbd>Enter</kbd> expand ·{" "}
          <kbd>e</kbd> exclude · <kbd>1</kbd>–<kbd>5</kbd> weight
        </p>

        <div
          className={grouped ? "hl-list hl-list--grouped" : "hl-list"}
          ref={listRef}
        >
          {grouped
            ? regions.map((region) => {
                // The contents list and the CLI both call this bucket the unknown chapter; the
                // page must not invent a third name for it.
                const label =
                  region.path.length > 0
                    ? describeChapterPath(region.path)
                    : "Unknown chapter";

                return (
                  <div
                    className="hl-group"
                    key={region.key}
                    role="group"
                    aria-label={label}
                    data-loose={region.path.length === 0 ? "true" : undefined}
                  >
                    {region.head ? (
                      annotationCard(region.head.item, region.head.index, {
                        ancestors: region.path.slice(0, -1),
                      })
                    ) : (
                      <header className="hl-group-head">
                        <h2 className="hl-group-title">{label}</h2>
                      </header>
                    )}
                    {region.stops.length > 0 ? (
                      <div className="hl-group-body">
                        {region.stops.map(({ row, index }) =>
                          rowCard(row, index),
                        )}
                      </div>
                    ) : null}
                  </div>
                );
              })
            : rows.map((row, index) => rowCard(row, index))}
        </div>
      </AsyncSection>
    </section>
  );
}

/**
 * The book's chapter structure as a filterable contents list. Levels nest by indentation
 * rather than nesting lists: the reading order is already linear, and a flat list of
 * buttons keeps the keyboard path obvious.
 */
function TableOfContents({
  chapters,
  unknown,
  total,
  selected,
  onSelect,
}: {
  chapters: ChapterItem[];
  unknown: UnknownChapterItem;
  total: number;
  selected: ChapterFilter;
  onSelect: (next: ChapterFilter) => void;
}) {
  // Books without a level-1 heading have no table of contents at all: `.h2`/`.h3` alone
  // is not a structure a reader can navigate.
  if (chapters.length === 0) return null;

  return (
    <nav className="toc" aria-labelledby="toc-h">
      <div className="toc-head">
        <h2 className="toc-title" id="toc-h">
          Contents
        </h2>
        <span className="subtle">
          {plural(chapters.length, "chapter")} in this book
        </span>
      </div>

      <ul className="toc-list">
        <li>
          <button
            className="toc-entry"
            type="button"
            aria-current={selected === null ? "true" : undefined}
            onClick={() => onSelect(null)}
          >
            <span className="toc-label">All highlights</span>
            <span className="toc-count">{formatCount(total)}</span>
          </button>
        </li>

        {chapters.map((chapter) => (
          <li key={chapter.id} data-level={Math.min(chapter.level, 4)}>
            <button
              className="toc-entry"
              type="button"
              aria-current={selected === chapter.id ? "true" : undefined}
              onClick={() => onSelect(chapter.id)}
            >
              <span className="toc-label">
                <span className="sr-only">
                  {describeHeadingLevel(chapter.level)}:{" "}
                </span>
                {chapter.title}
              </span>
              <span className="toc-count">
                {formatCount(chapter.annotationCount)}
              </span>
            </button>
          </li>
        ))}

        {unknown.annotationCount > 0 ? (
          <li data-level={1}>
            <button
              className="toc-entry toc-entry--unknown"
              type="button"
              aria-current={selected === "unknown" ? "true" : undefined}
              onClick={() => onSelect("unknown")}
            >
              <span className="toc-label">{unknown.title}</span>
              <span className="toc-count">
                {formatCount(unknown.annotationCount)}
              </span>
            </button>
          </li>
        ) : null}
      </ul>
    </nav>
  );
}
