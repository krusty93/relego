import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { AsyncSection, EmptyState, Tag, WeightPips } from "../components/ui";
import { api } from "../lib/api";
import { plural } from "../lib/format";
import { useDebounced, useSearch } from "../lib/search";
import { useToasts } from "../lib/toasts";
import type { BookItem, HighlightItem } from "../lib/types";

export function HighlightsPage() {
  const params = useParams<{ bookId?: string }>();
  const bookId = params.bookId ? Number(params.bookId) : null;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { push } = useToasts();
  const { query } = useSearch();
  const debounced = useDebounced(query);

  const [openId, setOpenId] = useState<number | null>(null);
  const [cursor, setCursor] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  const highlights = useQuery({
    queryKey: ["highlights", debounced],
    queryFn: () => api.highlights(debounced || undefined),
    retry: false,
  });

  const books = useQuery({ queryKey: ["books", ""], queryFn: () => api.books(), retry: false });
  const weights = useQuery({ queryKey: ["weights"], queryFn: api.weights, retry: false });
  const exclusions = useQuery({ queryKey: ["exclusions"], queryFn: api.exclusions, retry: false });

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

  useEffect(() => {
    setCursor(0);
    setOpenId(null);
  }, [debounced, bookId]);

  function refreshAll() {
    void queryClient.invalidateQueries({ queryKey: ["highlights"] });
    void queryClient.invalidateQueries({ queryKey: ["exclusions"] });
    void queryClient.invalidateQueries({ queryKey: ["weights"] });
    void queryClient.invalidateQueries({ queryKey: ["books"] });
    void queryClient.invalidateQueries({ queryKey: ["status"] });
  }

  function reportError(error: unknown) {
    push(error instanceof Error ? error.message : "That didn't work.", { tone: "bad" });
  }

  const setWeight = useMutation({
    mutationFn: ({ id, weight }: { id: number; weight: number }) => api.setWeight(id, weight),
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
              toggleHighlight.mutate({ id: variables.id, excluded: !variables.excluded }),
          },
        },
      );
    },
    onError: reportError,
  });

  const removeHighlight = useMutation({
    mutationFn: (id: number) => api.deleteHighlight(id),
    onSuccess: () => {
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
      push(variables.excluded ? "Book is back in your recaps." : "Book excluded from recaps.");
    },
    onError: reportError,
  });

  const toggleAuthor = useMutation({
    mutationFn: ({ id, excluded }: { id: number; excluded: boolean }) =>
      excluded ? api.include("authors", id) : api.exclude("authors", id),
    onSuccess: (_result, variables) => {
      refreshAll();
      push(variables.excluded ? "Author is back in your recaps." : "Author excluded from recaps.");
    },
    onError: reportError,
  });

  function focusCard(index: number) {
    const clamped = Math.max(0, Math.min(index, items.length - 1));
    setCursor(clamped);
    listRef.current?.querySelectorAll<HTMLButtonElement>(".hl-summary")[clamped]?.focus();
  }

  function onCardKeyDown(event: React.KeyboardEvent, item: HighlightItem, index: number) {
    const excluded = excludedIds.has(item.id);

    if (event.key >= "1" && event.key <= "5") {
      event.preventDefault();
      setWeight.mutate({ id: item.id, weight: Number(event.key) });
      return;
    }

    switch (event.key) {
      case "j":
      case "ArrowDown":
        event.preventDefault();
        focusCard(index + 1);
        break;
      case "k":
      case "ArrowUp":
        event.preventDefault();
        focusCard(index - 1);
        break;
      case "e":
        event.preventDefault();
        toggleHighlight.mutate({ id: item.id, excluded });
        break;
      case "Escape":
        if (openId === item.id) {
          event.preventDefault();
          setOpenId(null);
        }
        break;
      default:
        break;
    }
  }

  const heading = book ? book.title : "Highlights";
  const isLoading = highlights.isPending || weights.isPending || exclusions.isPending;

  return (
    <section className="view" aria-labelledby="hl-h">
      {book ? (
        <nav className="breadcrumb" aria-label="Breadcrumb">
          <button className="btn btn--ghost btn--sm" type="button" onClick={() => navigate("/")}>
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
                {book.authorName} · {plural(items.length, "highlight")} ·{" "}
                {book.excluded ? <Tag tone="excluded">Excluded</Tag> : <Tag tone="ok">In recaps</Tag>}
              </>
            ) : debounced ? (
              `${plural(items.length, "highlight")} matching “${debounced}”.`
            ) : (
              "Every highlight Relego knows about. Weight the ones you want to see more often."
            )}
          </p>
        </div>

        {book ? (
          <div className="actions">
            <button
              className="btn btn--sm"
              type="button"
              onClick={() => toggleBook.mutate({ id: book.id, excluded: book.excluded })}
            >
              {book.excluded ? "Include book" : "Exclude book"}
            </button>
            <button
              className="btn btn--sm"
              type="button"
              onClick={() =>
                toggleAuthor.mutate({ id: book.authorId, excluded: book.authorExcluded })
              }
            >
              {book.authorExcluded ? "Include author" : "Exclude author"}
            </button>
          </div>
        ) : null}
      </div>

      <AsyncSection
        isLoading={isLoading}
        error={highlights.error ?? weights.error ?? exclusions.error}
        onRetry={() => void highlights.refetch()}
        isEmpty={items.length === 0}
        empty={
          debounced ? (
            <EmptyState title="No highlights match that search">
              Nothing matches “{debounced}”. Try a shorter word, or clear the search box.
            </EmptyState>
          ) : (
            <EmptyState
              title="No highlights yet"
              action={
                <button
                  className="btn btn--primary"
                  type="button"
                  onClick={() => navigate("/import")}
                >
                  Import highlights
                </button>
              }
            >
              Connect your Kindle or Kobo by USB and drop its highlight file in. Relego takes it
              from there.
            </EmptyState>
          )
        }
      >
        <div className="hl-list" ref={listRef}>
          {items.map((item, index) => {
            const open = openId === item.id;
            const excluded = excludedIds.has(item.id);
            const weight = weightById.get(item.id) ?? 3;

            return (
              <article className="hl" key={item.id} data-open={open}>
                <button
                  className="hl-summary"
                  type="button"
                  aria-expanded={open}
                  aria-controls={`hlb-${item.id}`}
                  tabIndex={index === cursor ? 0 : -1}
                  onFocus={() => setCursor(index)}
                  onClick={() => setOpenId(open ? null : item.id)}
                  onKeyDown={(event) => onCardKeyDown(event, item, index)}
                >
                  <span>
                    <span className="hl-quote">{item.text}</span>
                    <span className="hl-meta">
                      {book ? null : (
                        <span className="subtle">
                          {item.bookTitle} · {item.authorName}
                        </span>
                      )}
                      {excluded ? <Tag tone="excluded">Excluded</Tag> : null}
                    </span>
                  </span>
                  <span className="hl-side">
                    <WeightPips value={weight} />
                  </span>
                </button>

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
                          onClick={() => setWeight.mutate({ id: item.id, weight: step })}
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
                      className="btn btn--sm btn--danger"
                      type="button"
                      onClick={() => removeHighlight.mutate(item.id)}
                    >
                      Delete
                    </button>
                  </div>
                </div>
              </article>
            );
          })}
        </div>

        <p className="subtle hint-line">
          <kbd>j</kbd> <kbd>k</kbd> move · <kbd>Enter</kbd> expand · <kbd>e</kbd> exclude ·{" "}
          <kbd>1</kbd>–<kbd>5</kbd> weight
        </p>
      </AsyncSection>
    </section>
  );
}
