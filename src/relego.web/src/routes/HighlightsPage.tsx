import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
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
  UnknownChapterItem,
} from "../lib/types";

/** The chapter the list is narrowed to; `null` is the whole book. */
type ChapterFilter = number | "unknown" | null;

/** Stable identity, so downstream `useMemo`s do not re-run on every render. */
const NO_CHAPTERS: ChapterItem[] = [];

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
  const listRef = useRef<HTMLDivElement>(null);

  // The server narrows the list, so the whole book arrives in one page rather than
  // being filtered here: chapter paths are derived from every annotation in the book.
  const scope = useMemo(
    () =>
      bookId === null
        ? undefined
        : chapterFilter === null
          ? { bookId }
          : { bookId, chapterId: chapterFilter },
    [bookId, chapterFilter],
  );

  const highlights = useQuery({
    queryKey: ["highlights", debounced, scope],
    queryFn: () => api.highlights(debounced || undefined, 1, 200, scope),
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
  }, [debounced, bookId, chapterFilter]);

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
    const clamped = Math.max(0, Math.min(index, items.length - 1));
    setCursor(clamped);
    const cards = listRef.current?.querySelectorAll<HTMLElement>(".hl-summary");
    cards?.[clamped]?.focus();
  }

  useListKeys({
    count: items.length,
    cursor,
    containerRef: listRef,
    focusAt: focusCard,
    actions: {
      e: (index) => {
        const item = items[index];
        if (item)
          toggleHighlight.mutate({
            id: item.id,
            excluded: excludedIds.has(item.id),
          });
      },
      ...Object.fromEntries(
        [1, 2, 3, 4, 5].map((weight) => [
          String(weight),
          (index: number) => {
            const item = items[index];
            if (item) setWeight.mutate({ id: item.id, weight });
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

      <AsyncSection
        isLoading={isLoading}
        error={highlights.error ?? weights.error ?? exclusions.error}
        onRetry={() => void highlights.refetch()}
        isEmpty={items.length === 0}
        empty={
          activeChapter ? (
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

        <div className="hl-list" ref={listRef}>
          {items.map((item, index) => {
            const open = openId === item.id;
            const excluded = excludedIds.has(item.id);
            const weight = weightById.get(item.id) ?? 3;

            return (
              <article className="hl" key={item.id} data-open={open}>
                {/* A div rather than a button: button labels can't be selected, and the quote
                    is exactly what people want to drag-select and copy. */}
                <div
                  className="hl-summary"
                  role="button"
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
                      <span
                        className="hl-heading"
                        data-level={item.headingLevel}
                      >
                        <span className="hl-eyebrow">
                          {describeHeadingLevel(item.headingLevel)}
                        </span>
                        <span className="hl-quote hl-quote--heading">
                          {item.text}
                        </span>
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
                          {item.headingLevel !== null
                            ? "Section summary: "
                            : "Note: "}
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
                      {/* The heading is its own chapter, so its path would only repeat it. */}
                      {item.chapterPath.length > 0 &&
                      item.headingLevel === null ? (
                        <span className="subtle hl-crumb">
                          <span className="sr-only">Chapter: </span>
                          {describeChapterPath(item.chapterPath)}
                        </span>
                      ) : null}
                      {excluded ? <Tag tone="excluded">Excluded</Tag> : null}
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
                    <div
                      className="seg"
                      role="group"
                      aria-labelledby={`w-${item.id}`}
                    >
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
                      onClick={() =>
                        toggleHighlight.mutate({ id: item.id, excluded })
                      }
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
                          {removeHighlight.isPending
                            ? "Deleting…"
                            : "Yes, delete"}
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
          })}
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
