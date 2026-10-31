/**
 * Sync runner: one routine or full-resync pass over the user's Kindle notebook.
 *
 * The runner is transport-agnostic: it takes a `RelegoClient` and a `fetchPage` function so the
 * service worker can hand it the real browser fetch while tests drive it with fixtures. It never
 * writes Amazon cookies, session data or page HTML anywhere; only the normalized batch leaves the
 * browser.
 *
 * Invariants enforced here:
 *
 * - At most one sync runs at a time in this extension (single-flight lock), so an overdue or
 *   overlapping alarm firing coalesces instead of starting a second pass.
 * - A run either reports an explicit `complete` with `booksTotal === booksDone` or reports a
 *   failure with one of the five recovery codes. A partial run is never a silent success.
 * - Routine runs skip books whose library highlight count matches the server's `knownBooks`
 *   fingerprint; full resyncs re-read every book.
 */

import type { RelegoBatch, RelegoBatchBook, RelegoBatchHighlight, RelegoClient, RelegoKnownBook } from "./client";
import { PROVIDER_ID, statusRegion } from "./client";
import { isSignInUrl, notebookBookUrl, notebookUrl, normalizeBookKey, resolveRegion } from "./profile";
import type { KindleRegion } from "./profile";
import { parseBookPage, parseLibrary } from "./notebook-parser";

/** Default highlights carried by one upload batch. */
export const DEFAULT_BATCH_HIGHLIGHTS = 200;

/** Hard cap on highlights per batch, mirrored from the server's `MaxHighlightsPerBatch`. */
export const MAX_BATCH_HIGHLIGHTS = 1_000;

/** Hard cap on the serialized size of one upload batch. */
export const MAX_BATCH_BYTES = 2 * 1024 * 1024;

/** Sync modes accepted by the server. */
export type SyncMode = "routine" | "full_resync";

/** Reasons a run can be started. */
export type SyncTrigger = "scheduled" | "manual" | "initial" | "retry" | "region_change";

/** The five recovery codes the server understands. */
export type SyncFailureCode =
  | "auth_expired"
  | "provider_contract_changed"
  | "network"
  | "server_rejected"
  | "interrupted";

/** A page as fetched from the user's own authenticated notebook session. */
export interface PageResponse {
  /** Final URL after redirects; compared against the region's sign-in hosts. */
  readonly url: string;
  /** Page HTML. Never persisted and never sent anywhere. */
  readonly html: string;
}

/** Dependencies the runner needs, injected so the flow is testable without a browser. */
export interface SyncRunnerDeps {
  /** Authenticated client for the user's Relego server. */
  readonly client: RelegoClient;
  /** Fetches a notebook page in the user's own Amazon session. */
  readonly fetchPage: (url: string) => Promise<PageResponse>;
  /** Aborts the run, for example when the browser is shutting down. */
  readonly signal?: AbortSignal;
}

/** Options for one run. */
export interface SyncRunOptions {
  /** `routine` for incremental runs, `full_resync` when the user asked for a full re-read. */
  readonly mode: SyncMode;
  /** Why the run started. */
  readonly trigger: SyncTrigger;
  /** Replay key; a repeat with the same key returns the same server-side job. */
  readonly idempotencyKey: string;
}

/** Outcome of one run. */
export type SyncRunOutcome =
  | { readonly kind: "skipped"; readonly reason: "already_running" }
  | { readonly kind: "skipped"; readonly reason: "not_connected" }
  | {
      readonly kind: "completed";
      readonly jobId: number;
      readonly newHighlights: number;
      readonly booksTotal: number;
      readonly truncatedBooks: number;
    }
  | { readonly kind: "failed"; readonly jobId: number | null; readonly code: SyncFailureCode };

let inFlight: Promise<SyncRunOutcome> | null = null;

/** Reports whether a sync is currently running in this extension instance. */
export function isSyncRunning(): boolean {
  return inFlight !== null;
}

/**
 * Runs one sync pass, or joins the pass already running.
 *
 * Overlapping callers share the single in-flight run, so an overdue alarm firing cannot start a
 * second pass; the server coalesces as well, which keeps this correct across worker restarts.
 *
 * @param deps Client and page fetcher for the run.
 * @param options Mode, trigger and replay key for the run.
 * @returns The run outcome.
 */
export function runSync(deps: SyncRunnerDeps, options: SyncRunOptions): Promise<SyncRunOutcome> {
  if (inFlight !== null) return inFlight;

  const run = execute(deps, options).finally(() => {
    inFlight = null;
  });

  inFlight = run;
  return run;
}

async function execute(deps: SyncRunnerDeps, options: SyncRunOptions): Promise<SyncRunOutcome> {
  const status = await deps.client.getStatus(PROVIDER_ID);
  const reported = statusRegion(status);
  const region = resolveRegion(reported?.id ?? null);

  if (region === null) {
    // No stored region means the connection is not paired yet; a region the extension does not
    // know means the server and the extension disagree on the profile.
    return reported === null
      ? { kind: "skipped", reason: "not_connected" }
      : { kind: "failed", jobId: null, code: "provider_contract_changed" };
  }

  const start = await deps.client.startJob(options);
  const jobId = start.jobId;

  try {
    const library = await readLibrary(deps, region);

    if (library.kind === "failed") {
      await reportFailure(deps, jobId, library.code, library.diagnostics);
      return { kind: "failed", jobId, code: library.code };
    }

    const books = selectBooks(library.books, start.knownBooks, options.mode);
    const total = books.length;

    await deps.client.reportProgress(jobId, { phase: "reading", booksTotal: total, booksDone: 0 });

    const read: RelegoBatchBook[] = [];
    let truncatedBooks = 0;

    for (const book of books) {
      if (deps.signal?.aborted === true) {
        await reportFailure(deps, jobId, "interrupted", null);
        return { kind: "failed", jobId, code: "interrupted" };
      }

      const page = await readBook(deps, region, book.externalKey, book.title, book.author);

      if (page.kind === "failed") {
        await reportFailure(deps, jobId, page.code, page.diagnostics);
        return { kind: "failed", jobId, code: page.code };
      }

      if (page.book.truncated) truncatedBooks++;

      read.push(page.book);
      await deps.client.reportProgress(jobId, {
        phase: "reading",
        booksTotal: total,
        booksDone: read.length,
      });
    }

    await deps.client.reportProgress(jobId, { phase: "uploading", booksTotal: total, booksDone: total });

    let newHighlights = 0;

    for (const batch of chunkBooks(read)) {
      const result = await deps.client.postBatch(jobId, batch);
      newHighlights += result.newHighlights;
    }

    await deps.client.completeJob(jobId, {
      booksTotal: total,
      booksDone: total,
      truncatedBooks,
    });

    return { kind: "completed", jobId, newHighlights, booksTotal: total, truncatedBooks };
  } catch (error) {
    const code = classify(error);
    await reportFailure(deps, jobId, code, null);
    return { kind: "failed", jobId, code };
  }
}

interface LibraryRead {
  readonly kind: "ok";
  readonly books: readonly {
    readonly externalKey: string;
    readonly title: string;
    readonly author: string | null;
    readonly highlightCount: number | null;
  }[];
}

type LibraryOutcome =
  | LibraryRead
  | { readonly kind: "failed"; readonly code: SyncFailureCode; readonly diagnostics: ContractDiagnostics | null };

interface BookRead {
  readonly kind: "ok";
  readonly book: RelegoBatchBook;
}

type BookOutcome =
  | BookRead
  | { readonly kind: "failed"; readonly code: SyncFailureCode; readonly diagnostics: ContractDiagnostics | null };

type ContractDiagnostics = {
  readonly profileVersion: string;
  readonly check: string;
  readonly counts: Record<string, number>;
};

async function readLibrary(deps: SyncRunnerDeps, region: KindleRegion): Promise<LibraryOutcome> {
  const page = await fetchOrNetworkFailure(deps, notebookUrl(region));
  if (page === null) return { kind: "failed", code: "network", diagnostics: null };
  if (isSignInUrl(region, page.url)) return { kind: "failed", code: "auth_expired", diagnostics: null };

  const outcome = parseLibrary(page.html, region);

  switch (outcome.kind) {
    case "ok":
      return { kind: "ok", books: outcome.books };
    case "sign_in":
      return { kind: "failed", code: "auth_expired", diagnostics: null };
    default:
      return { kind: "failed", code: "provider_contract_changed", diagnostics: outcome.diagnostics };
  }
}

async function readBook(
  deps: SyncRunnerDeps,
  region: KindleRegion,
  externalKey: string,
  title: string,
  author: string | null,
): Promise<BookOutcome> {
  const page = await fetchOrNetworkFailure(deps, notebookBookUrl(region, externalKey));
  if (page === null) return { kind: "failed", code: "network", diagnostics: null };
  if (isSignInUrl(region, page.url)) return { kind: "failed", code: "auth_expired", diagnostics: null };

  const outcome = parseBookPage(page.html, region, externalKey);

  switch (outcome.kind) {
    case "ok": {
      const book = outcome.result.books[0];
      if (book === undefined) return { kind: "failed", code: "provider_contract_changed", diagnostics: null };

      // The library row selected this book, so its title and author name the book Relego files the
      // highlights under; the page contributes the highlights and the export-limit flag only.
      return {
        kind: "ok",
        book: {
          externalKey: book.externalKey,
          title,
          author: author ?? book.author,
          truncated: book.truncated,
          highlights: book.highlights,
        },
      };
    }
    case "sign_in":
      return { kind: "failed", code: "auth_expired", diagnostics: null };
    default:
      return { kind: "failed", code: "provider_contract_changed", diagnostics: outcome.diagnostics };
  }
}

async function fetchOrNetworkFailure(deps: SyncRunnerDeps, url: string): Promise<PageResponse | null> {
  try {
    return await deps.fetchPage(url);
  } catch {
    return null;
  }
}

/**
 * Selects the books to read for this run.
 *
 * A routine run skips a book only when the server already knows it and the library advertises the
 * same highlight count, which means nothing changed upstream; a book with no advertised count is
 * always read. A full resync reads every book.
 */
function selectBooks(
  books: LibraryRead["books"],
  known: readonly RelegoKnownBook[],
  mode: SyncMode,
): LibraryRead["books"] {
  if (mode === "full_resync") return books;

  const fingerprint = new Map<string, number>();
  for (const entry of known) fingerprint.set(entry.key, entry.highlightCount);

  return books.filter((book) => {
    if (book.highlightCount === null) return true;

    return fingerprint.get(normalizeBookKey(book.title)) !== book.highlightCount;
  });
}

/**
 * Splits the read books into upload batches.
 *
 * Highlights are accumulated one at a time so a batch respects both the highlight ceiling and the
 * server's body cap, including when a single book is larger than either. Splitting a book across
 * batches is safe because the server deduplicates inserts.
 *
 * @param books Books read in this run.
 * @param maxHighlights Highlights per batch, clamped to the server maximum.
 * @returns Batches ready to upload, in order.
 */
export function chunkBooks(
  books: readonly RelegoBatchBook[],
  maxHighlights: number = DEFAULT_BATCH_HIGHLIGHTS,
): RelegoBatch[] {
  const limit = Math.min(Math.max(maxHighlights, 1), MAX_BATCH_HIGHLIGHTS);
  const batches: RelegoBatch[] = [];
  let current: MutableBook[] = [];
  let count = 0;

  const flush = (): void => {
    if (count === 0) return;

    batches.push({ batchIndex: batches.length, books: current });
    current = [];
    count = 0;
  };

  const append = (book: RelegoBatchBook, highlight: RelegoBatchHighlight): void => {
    const last = current.at(-1);
    if (last !== undefined && last.externalKey === book.externalKey) {
      last.highlights.push(highlight);
    } else {
      current.push({ ...book, highlights: [highlight] });
    }
    count++;
  };

  const dropLast = (): void => {
    const last = current.at(-1);
    if (last === undefined) return;

    last.highlights.pop();
    if (last.highlights.length === 0) current.pop();
    count--;
  };

  for (const book of books) {
    for (const highlight of book.highlights) {
      if (count >= limit) flush();

      append(book, highlight);
      if (!exceedsByteBudget(current)) continue;

      if (count === 1) {
        // A single highlight larger than the cap cannot be split further; the server rejects it.
        flush();
        continue;
      }

      dropLast();
      flush();
      append(book, highlight);
    }
  }

  flush();
  return batches;
}

type MutableBook = Omit<RelegoBatchBook, "highlights"> & { highlights: RelegoBatchHighlight[] };

function exceedsByteBudget(books: readonly RelegoBatchBook[]): boolean {
  return new TextEncoder().encode(JSON.stringify(books)).length > MAX_BATCH_BYTES;
}
function classify(error: unknown): SyncFailureCode {
  if (typeof error === "object" && error !== null && "status" in error) {
    const status = (error as { readonly status: unknown }).status;

    if (typeof status === "number") {
      if (status === 401 || status === 403) return "auth_expired";
      return "server_rejected";
    }
  }

  return "network";
}

async function reportFailure(
  deps: SyncRunnerDeps,
  jobId: number,
  code: SyncFailureCode,
  diagnostics: ContractDiagnostics | null,
): Promise<void> {
  try {
    await deps.client.failJob(jobId, { code, diagnostics });
  } catch {
    // The failure is already known locally; an unreachable server must not turn it into a success.
    // The server's stale window marks the job interrupted on its own.
  }
}
