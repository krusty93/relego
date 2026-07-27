import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { AsyncSection, EmptyState, Tag } from "../components/ui";
import { api } from "../lib/api";
import { formatCount, plural } from "../lib/format";
import { useListKeys } from "../lib/listkeys";
import { useDebounced, useSearch } from "../lib/search";
import { useToasts } from "../lib/toasts";
import type { BookItem } from "../lib/types";

export function LibraryPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { push } = useToasts();
  const { query } = useSearch();
  const debounced = useDebounced(query);

  const [cursor, setCursor] = useState(0);
  const [renaming, setRenaming] = useState<BookItem | null>(null);
  const rowsRef = useRef<HTMLTableSectionElement>(null);

  const books = useQuery({
    queryKey: ["books", debounced],
    queryFn: () => api.books(debounced || undefined),
    retry: false,
  });

  const items = books.data?.items ?? [];

  const toggleExclusion = useMutation({
    mutationFn: (book: BookItem) =>
      book.excluded ? api.include("books", book.id) : api.exclude("books", book.id),
    onSuccess: (_result, book) => {
      void queryClient.invalidateQueries({ queryKey: ["books"] });
      void queryClient.invalidateQueries({ queryKey: ["status"] });
      push(
        book.excluded
          ? `“${book.title}” is back in your recaps.`
          : `“${book.title}” won't appear in recaps.`,
        {
          action: {
            label: "Undo",
            run: () => toggleExclusion.mutate({ ...book, excluded: !book.excluded }),
          },
        },
      );
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "That didn't work.", { tone: "bad" }),
  });

  const rename = useMutation({
    mutationFn: ({ id, title }: { id: number; title: string }) => api.renameBook(id, title),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["books"] });
      setRenaming(null);
      push("Title updated.");
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not rename that book.", {
        tone: "bad",
      }),
  });

  useEffect(() => {
    setCursor(0);
  }, [debounced]);

  function focusRow(index: number) {
    const clamped = Math.max(0, Math.min(index, items.length - 1));
    setCursor(clamped);
    const row = rowsRef.current?.querySelectorAll<HTMLTableRowElement>("tr")[clamped];
    row?.focus();
  }

  useListKeys({
    count: items.length,
    cursor,
    containerRef: rowsRef,
    focusAt: focusRow,
    actions: {
      e: (index) => {
        const book = items[index];
        if (book) toggleExclusion.mutate(book);
      },
      n: (index) => {
        const book = items[index];
        if (book) setRenaming(book);
      },
    },
  });

  // Enter belongs to the focused row rather than to the page: hijacking it globally would
  // steal activation from every other control.
  function onRowKeyDown(event: React.KeyboardEvent<HTMLTableRowElement>, book: BookItem) {
    if (event.key !== "Enter") return;
    event.preventDefault();
    navigate(`/books/${book.id}`);
  }

  const totalHighlights = items.reduce((sum, book) => sum + book.highlightCount, 0);
  const excludedCount = items.filter((book) => book.excluded || book.authorExcluded).length;

  return (
    <section className="view" aria-labelledby="lib-h">
      <div className="view-head">
        <div>
          <h1 id="lib-h">Library</h1>
          <p>
            {books.data
              ? `${plural(books.data.total, "book")} · ${plural(totalHighlights, "highlight")}${
                  excludedCount > 0 ? ` · ${formatCount(excludedCount)} kept out of recaps` : ""
                }.`
              : "Every book Relego has highlights for."}
          </p>
        </div>
        <div className="actions">
          <button className="btn" type="button" onClick={() => navigate("/import")}>
            Import highlights
          </button>
        </div>
      </div>

      <AsyncSection
        isLoading={books.isPending}
        error={books.error}
        onRetry={() => void books.refetch()}
        isEmpty={items.length === 0}
        empty={
          debounced ? (
            <EmptyState title="No books match that search">
              Nothing in your library matches “{debounced}”. Try a shorter word, or clear the
              search box.
            </EmptyState>
          ) : (
            <EmptyState
              title="No books yet"
              action={
                <button className="btn btn--primary" type="button" onClick={() => navigate("/import")}>
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
        <div className="table-wrap">
          <table>
            <caption className="sr-only">Books in your library</caption>
            <thead>
              <tr>
                <th scope="col">Title</th>
                <th scope="col">Author</th>
                <th scope="col" className="num">
                  Highlights
                </th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody className="book-rows" ref={rowsRef}>
              {items.map((book, index) => (
                <tr
                  key={book.id}
                  tabIndex={index === cursor ? 0 : -1}
                  aria-selected={index === cursor}
                  onFocus={() => setCursor(index)}
                  onClick={() => navigate(`/books/${book.id}`)}
                  onKeyDown={(event) => onRowKeyDown(event, book)}
                >
                  <td className="book-title">{book.title}</td>
                  <td className="book-author">{book.authorName}</td>
                  <td className="num">{formatCount(book.highlightCount)}</td>
                  <td>
                    {book.excluded ? (
                      <Tag tone="excluded">Excluded</Tag>
                    ) : book.authorExcluded ? (
                      <Tag tone="excluded">Author excluded</Tag>
                    ) : (
                      <Tag tone="ok">In recaps</Tag>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <p className="subtle hint-line">
          <kbd>j</kbd> <kbd>k</kbd> move · <kbd>Enter</kbd> open · <kbd>n</kbd> rename ·{" "}
          <kbd>e</kbd> exclude book
        </p>
      </AsyncSection>

      {renaming ? (
        <RenameDialog
          book={renaming}
          pending={rename.isPending}
          onCancel={() => setRenaming(null)}
          onSave={(title) => rename.mutate({ id: renaming.id, title })}
        />
      ) : null}
    </section>
  );
}

function RenameDialog({
  book,
  pending,
  onCancel,
  onSave,
}: {
  book: BookItem;
  pending: boolean;
  onCancel: () => void;
  onSave: (title: string) => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [title, setTitle] = useState(book.title);

  useEffect(() => {
    dialogRef.current?.showModal();
  }, []);

  return (
    <dialog ref={dialogRef} aria-labelledby="rename-h" onClose={onCancel}>
      <form
        className="sheet"
        method="dialog"
        onSubmit={(event) => {
          event.preventDefault();
          if (title.trim()) onSave(title.trim());
        }}
      >
        <h2 id="rename-h">Rename book</h2>
        <div className="field">
          <label htmlFor="rename-title">Title</label>
          {/* eslint-disable-next-line jsx-a11y/no-autofocus -- the dialog exists to edit this one field */}
          <input
            className="control"
            id="rename-title"
            value={title}
            autoFocus
            onChange={(event) => setTitle(event.target.value)}
          />
          <span className="help">This only changes how the book appears in Relego.</span>
        </div>
        <div className="inline dialog-actions">
          <button className="btn btn--primary" type="submit" disabled={pending || !title.trim()}>
            {pending ? "Saving…" : "Save"}
          </button>
          <button className="btn" type="button" onClick={onCancel}>
            Cancel
          </button>
        </div>
      </form>
    </dialog>
  );
}
