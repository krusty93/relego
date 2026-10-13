/**
 * Notebook parse-profile and sync-runner tests.
 *
 * The fixtures under `tests/fixtures/<regionId>/` are the committed shape of the notebook profile;
 * a fixture change requires a profile version bump. Every invariant the profile contract lists is
 * asserted here, including the promise that a failure carries selector ids and counts but never
 * page text.
 */

import { readFileSync } from "node:fs";
import { describe, expect, it, vi } from "vitest";

import { RelegoApiError, PROVIDER_ID } from "../src/client";
import type { RelegoBatch, RelegoBatchResult, RelegoClient, RelegoKnownBook } from "../src/client";
import {
  ALLOWED_INTERVAL_MINUTES,
  DEFAULT_INTERVAL_MINUTES,
  PROFILE_VERSION,
  REGIONS,
  clampIntervalMinutes,
  isSignInUrl,
  normalizeBookKey,
  notebookBookUrl,
  notebookUrl,
  resolveRegion,
} from "../src/profile";
import type { KindleRegion } from "../src/profile";
import { isSignInPage, parseBookPage, parseLibrary } from "../src/notebook-parser";
import { chunkBooks, isSyncRunning, runSync } from "../src/sync-runner";
import type { PageResponse, SyncRunOptions, SyncRunnerDeps } from "../src/sync-runner";

const us = resolveRegion("us");
if (us === null) throw new Error("The US region must be part of the bundled profile.");

function fixture(name: string): string {
  return readFileSync(new URL(`./fixtures/us/${name}`, import.meta.url), "utf8");
}

function page(html: string, url: string = notebookUrl(us as KindleRegion)): PageResponse {
  return { url, html };
}

describe("parse profile", () => {
  it("bundles exactly the eight allowlisted regions with their official hosts", () => {
    expect(REGIONS.map((region) => [region.id, region.notebookHost])).toEqual([
      ["us", "read.amazon.com"],
      ["ca", "read.amazon.ca"],
      ["uk", "read.amazon.co.uk"],
      ["de", "lesen.amazon.de"],
      ["fr", "lire.amazon.fr"],
      ["it", "leggi.amazon.it"],
      ["es", "leer.amazon.es"],
      ["br", "ler.amazon.com.br"],
    ]);
  });

  it("rejects unsupported, host-shaped and empty region ids", () => {
    for (const regionId of ["jp", "cn", "dk", "ie", "pl", "", "  ", "https://read.amazon.com", "read.amazon.com", "us/../evil"]) {
      expect(resolveRegion(regionId)).toBeNull();
    }

    expect(resolveRegion("US")?.id).toBeUndefined();
    expect(resolveRegion(null)).toBeNull();
    expect(resolveRegion(undefined)).toBeNull();
  });

  it("derives every notebook URL from the allowlist host", () => {
    expect(notebookUrl(us)).toBe("https://read.amazon.com/notebook");
    expect(notebookBookUrl(us, "B00 EXAMPLE")).toBe("https://read.amazon.com/notebook?bookKey=B00%20EXAMPLE");
  });

  it("recognises the region sign-in host without treating the notebook host as sign-in", () => {
    expect(isSignInUrl(us, "https://www.amazon.com/ap/signin")).toBe(true);
    expect(isSignInUrl(us, notebookUrl(us))).toBe(false);
    expect(isSignInUrl(us, "not a url")).toBe(false);
  });

  it("normalizes book keys exactly like the server's SyncBookKey", () => {
    expect(normalizeBookKey("  The   Pragmatic\tProgrammer \n")).toBe("the pragmatic programmer");
    expect(normalizeBookKey("Thinking,   Fast and Slow")).toBe("thinking, fast and slow");
    expect(normalizeBookKey(null)).toBe("");
    expect(normalizeBookKey(undefined)).toBe("");
  });

  it("clamps frequencies to the ten the server accepts", () => {
    expect(ALLOWED_INTERVAL_MINUTES).toEqual([15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440]);
    for (const minutes of ALLOWED_INTERVAL_MINUTES) expect(clampIntervalMinutes(minutes)).toBe(minutes);

    expect(clampIntervalMinutes(7)).toBe(DEFAULT_INTERVAL_MINUTES);
  });
});

describe("notebook parser - library page", () => {
  it("parses every book with a title, author, key and advertised count (invariants 1 and 2)", () => {
    const outcome = parseLibrary(fixture("library.html"), us);

    expect(outcome.kind).toBe("ok");
    if (outcome.kind !== "ok") return;

    expect(outcome.books).toHaveLength(3);
    expect(outcome.books.every((book) => book.title.length > 0)).toBe(true);
    expect(outcome.books[0]).toEqual({
      externalKey: "B00EXAMPLE01",
      title: "The Pragmatic Programmer",
      author: "Andrew Hunt & David Thomas",
      highlightCount: 3,
      truncated: false,
    });
  });

  it("decodes entities and keeps the raw title for the shared key", () => {
    const outcome = parseLibrary(fixture("library.html"), us);
    if (outcome.kind !== "ok") throw new Error("Expected the library fixture to parse.");

    const third = outcome.books[2];
    expect(third?.author).toBe("Daniel Kahneman");
    expect(third?.title).toBe("Thinking, Fast and Slow");
    expect(normalizeBookKey(third?.title)).toBe("thinking, fast and slow");
  });

  it("flags the export limit as truncated instead of failing (invariant 4)", () => {
    const outcome = parseLibrary(fixture("library.html"), us);
    if (outcome.kind !== "ok") throw new Error("Expected the library fixture to parse.");

    expect(outcome.books[2]?.truncated).toBe(true);
    expect(outcome.books[0]?.truncated).toBe(false);
  });

  it("accepts an explicitly empty library", () => {
    const outcome = parseLibrary(fixture("empty.html"), us);

    expect(outcome).toEqual({ kind: "ok", books: [] });
  });

  it("reports a contract break when the library list anchor is gone (invariant 1)", () => {
    const outcome = parseLibrary("<html><body><div id=\"kp-notebook-main\"></div></body></html>", us);

    expect(outcome.kind).toBe("contract_changed");
    if (outcome.kind !== "contract_changed") return;

    expect(outcome.diagnostics.check).toBe("libraryList");
    expect(outcome.diagnostics.profileVersion).toBe(PROFILE_VERSION);
  });

  it("reports a contract break when the rows no longer match instead of looking empty (invariant 2)", () => {
    const outcome = parseLibrary(fixture("changed-layout.html"), us);

    expect(outcome.kind).toBe("contract_changed");
    if (outcome.kind !== "contract_changed") return;

    expect(outcome.diagnostics.check).toBe("bookRow");
    expect(outcome.diagnostics.counts).toEqual({ bookRows: 0 });
  });

  it("never leaks page text into the diagnostics (invariant 5)", () => {
    const outcome = parseLibrary(fixture("changed-layout.html"), us);
    if (outcome.kind !== "contract_changed") throw new Error("Expected a contract break.");

    const serialized = JSON.stringify(outcome.diagnostics);
    expect(serialized).not.toContain("The Pragmatic Programmer");
    expect(serialized).not.toContain("kp-notebook-book-row");

    expect(Object.keys(outcome.diagnostics).sort()).toEqual(["check", "counts", "profileVersion"]);
    for (const value of Object.values(outcome.diagnostics.counts)) {
      expect(typeof value).toBe("number");
    }
  });

  it("detects the Amazon sign-in redirect instead of an empty notebook", () => {
    const html = fixture("sign-in.html");

    expect(parseLibrary(html, us)).toEqual({ kind: "sign_in" });
    expect(isSignInPage(html, us)).toBe(true);
  });
});

describe("notebook parser - book page", () => {
  it("parses highlights with their id, note, location and color (invariant 3)", () => {
    const outcome = parseBookPage(fixture("book.html"), us, "fallback");

    expect(outcome.kind).toBe("ok");
    if (outcome.kind !== "ok") return;

    const book = outcome.result.books[0];
    expect(book?.title).toBe("The Pragmatic Programmer");
    expect(book?.externalKey).toBe("B00EXAMPLE01");
    expect(book?.truncated).toBe(false);
    expect(book?.highlights).toHaveLength(3);
    expect(book?.highlights.every((highlight) => highlight.text.length > 0)).toBe(true);
    expect(book?.highlights[1]).toEqual({
      externalId: "H-0002",
      text: "Provide options, don't make lame excuses.",
      note: "Say it once, then move on.",
      location: "Location 40-41",
      color: "blue",
    });
    expect(book?.highlights[2]?.location).toBeNull();
    expect(book?.highlights[2]?.color).toBeNull();
  });

  it("marks a book truncated when the export limit is shown (invariant 4)", () => {
    const outcome = parseBookPage(fixture("truncation-warning.html"), us, "fallback");

    expect(outcome.kind).toBe("ok");
    if (outcome.kind !== "ok") return;

    const book = outcome.result.books[0];
    expect(book?.truncated).toBe(true);
    expect(book?.highlights).toHaveLength(1);
  });

  it("reports a contract break when a highlight has no text (invariant 3)", () => {
    const html = fixture("book.html").replace("<span id=\"highlight\">Care about your craft.</span>", "<span id=\"highlight\">   </span>");
    const outcome = parseBookPage(html, us, "fallback");

    expect(outcome.kind).toBe("contract_changed");
    if (outcome.kind !== "contract_changed") return;

    expect(outcome.diagnostics.check).toBe("highlightText");
    expect(outcome.diagnostics.counts).toEqual({ annotations: 3, missingText: 1 });
  });

  it("reports a contract break when the annotations container is gone", () => {
    const html = fixture("book.html").replace('id="kp-notebook-annotations"', 'id="kp-notebook-annotations-moved"');
    const outcome = parseBookPage(html, us, "fallback");

    expect(outcome.kind).toBe("contract_changed");
    if (outcome.kind !== "contract_changed") return;

    expect(outcome.diagnostics.check).toBe("annotations");
  });

  it("falls back to the library key when the page does not carry one", () => {
    const html = fixture("book.html").replace('data-book-key="B00EXAMPLE01"', "");
    const outcome = parseBookPage(html, us, "B00EXAMPLE01");

    expect(outcome.kind).toBe("ok");
    if (outcome.kind !== "ok") return;

    expect(outcome.result.books[0]?.externalKey).toBe("B00EXAMPLE01");
  });

  it("detects the sign-in redirect on a book page", () => {
    expect(parseBookPage(fixture("sign-in.html"), us, "fallback")).toEqual({ kind: "sign_in" });
  });
});

describe("chunked upload", () => {
  const book = (title: string, highlights: number): Parameters<typeof chunkBooks>[0][number] => ({
    externalKey: title,
    title,
    author: null,
    truncated: false,
    highlights: Array.from({ length: highlights }, (_, index) => ({
      externalId: `${title}-${index}`,
      text: `text ${index}`,
      note: null,
      location: null,
      color: null,
    })),
  });

  it("defaults to 200 highlights per batch and keeps book order", () => {
    const batches = chunkBooks([book("A", 150), book("B", 150)]);

    expect(batches.map((batch) => batch.batchIndex)).toEqual([0, 1]);
    expect(batches.map((batch) => batch.books.flatMap((entry) => entry.highlights).length)).toEqual([200, 100]);
    expect(batches[1]?.books[0]?.title).toBe("B");
  });

  it("splits a single oversized book across batches", () => {
    const batches = chunkBooks([book("A", 450)]);

    expect(batches).toHaveLength(3);
    expect(batches.map((batch) => batch.books[0]?.highlights.length)).toEqual([200, 200, 50]);
    expect(batches.every((batch) => batch.books[0]?.title === "A")).toBe(true);
  });

  it("never exceeds the server maximum, even when asked to", () => {
    const batches = chunkBooks([book("A", 2_500)], 5_000);

    expect(batches).toHaveLength(3);
    expect(batches.every((batch) => batch.books.flatMap((entry) => entry.highlights).length <= 1_000)).toBe(true);
  });

  it("splits a batch that would exceed the two megabyte body cap", () => {
    const huge = {
      ...book("A", 40),
      highlights: Array.from({ length: 40 }, (_, index) => ({
        externalId: `A-${index}`,
        text: "x".repeat(100_000),
        note: null,
        location: null,
        color: null,
      })),
    };

    const batches = chunkBooks([huge]);
    expect(batches.length).toBeGreaterThan(1);
    for (const batch of batches) {
      expect(JSON.stringify(batch.books).length).toBeLessThan(2 * 1024 * 1024);
    }
  });

  it("returns no batches for a run with nothing to upload", () => {
    expect(chunkBooks([])).toEqual([]);
  });
});

interface FakeCalls {
  readonly started: SyncRunOptions[];
  readonly batches: RelegoBatch[];
  readonly progress: { phase: string; booksTotal: number | null; booksDone: number | null }[];
  readonly completed: { booksTotal: number; booksDone: number; truncatedBooks: number }[];
  readonly failed: { code: string; diagnostics?: unknown }[];
  readonly statusCalls: number;
}

function fakeClient(options: {
  readonly region?: string | null;
  readonly knownBooks?: readonly RelegoKnownBook[];
  readonly batchStatus?: number;
} = {}): { client: RelegoClient; calls: FakeCalls } {
  const calls = {
    started: [] as SyncRunOptions[],
    batches: [] as RelegoBatch[],
    progress: [] as FakeCalls["progress"][number][],
    completed: [] as FakeCalls["completed"][number][],
    failed: [] as FakeCalls["failed"][number][],
    statusCalls: 0,
  };

  const client = {
    getStatus: () => {
      calls.statusCalls++;
      const region = options.region === undefined ? "us" : options.region;

      return Promise.resolve({
        providerId: PROVIDER_ID,
        status: "connected",
        region: region === null ? null : { id: region, displayName: region, host: "read.amazon.com" },
      });
    },
    startJob: (request: SyncRunOptions) => {
      calls.started.push(request);
      return Promise.resolve({ jobId: 42, knownBooks: options.knownBooks ?? [] });
    },
    postBatch: (_jobId: number, batch: RelegoBatch) => {
      calls.batches.push(batch);

      if (options.batchStatus !== undefined) {
        throw new RelegoApiError("rejected", options.batchStatus);
      }

      const result: RelegoBatchResult = {
        batchIndex: batch.batchIndex,
        replayed: false,
        newHighlights: batch.books.flatMap((book) => book.highlights).length,
        duplicateHighlights: 0,
        newBooks: batch.books.length,
        newAuthors: 0,
      };

      return Promise.resolve(result);
    },
    reportProgress: (_jobId: number, progress: FakeCalls["progress"][number]) => {
      calls.progress.push(progress);
      return Promise.resolve();
    },
    completeJob: (_jobId: number, completion: FakeCalls["completed"][number]) => {
      calls.completed.push(completion);
      return Promise.resolve();
    },
    failJob: (_jobId: number, failure: FakeCalls["failed"][number]) => {
      calls.failed.push(failure);
      return Promise.resolve();
    },
  };

  return { client: client as unknown as RelegoClient, calls };
}

function pages(overrides: Record<string, string> = {}): (url: string) => Promise<PageResponse> {
  return (url) => {
    const override = overrides[url];
    if (override !== undefined) return Promise.resolve(page(override, url));

    const key = new URL(url).searchParams.get("bookKey");
    if (key !== null) return Promise.resolve(page(fixture("book.html").replace("B00EXAMPLE01", key), url));

    return Promise.resolve(page(fixture("library.html"), url));
  };
}

const routine: SyncRunOptions = { mode: "routine", trigger: "scheduled", idempotencyKey: "key-1" };

describe("sync runner", () => {
  it("reads the library, uploads in batches and completes with matching counts", async () => {
    const { client, calls } = fakeClient();
    const deps: SyncRunnerDeps = { client, fetchPage: pages() };

    const outcome = await runSync(deps, routine);

    expect(outcome).toEqual({
      kind: "completed",
      jobId: 42,
      newHighlights: 9,
      booksTotal: 3,
      truncatedBooks: 0,
    });
    expect(calls.batches).toHaveLength(1);
    expect(calls.batches[0]?.books).toHaveLength(3);
    expect(calls.completed).toEqual([{ booksTotal: 3, booksDone: 3, truncatedBooks: 0 }]);
    expect(calls.failed).toEqual([]);
    expect(calls.progress.at(-1)).toEqual({ phase: "uploading", booksTotal: 3, booksDone: 3 });
  });

  it("counts the books that hit the publisher export limit", async () => {
    const { client, calls } = fakeClient();
    const deps: SyncRunnerDeps = {
      client,
      fetchPage: pages({ [notebookBookUrl(us, "B00EXAMPLE03")]: fixture("truncation-warning.html") }),
    };

    const outcome = await runSync(deps, routine);

    expect(outcome).toEqual({
      kind: "completed",
      jobId: 42,
      newHighlights: 7,
      booksTotal: 3,
      truncatedBooks: 1,
    });
    expect(calls.completed).toEqual([{ booksTotal: 3, booksDone: 3, truncatedBooks: 1 }]);
  });

  it("skips books whose advertised count matches the fingerprint on a routine run", async () => {
    const { client, calls } = fakeClient({
      knownBooks: [
        { key: "the pragmatic programmer", highlightCount: 3 },
        { key: "dune", highlightCount: 99 },
      ],
    });

    const outcome = await runSync({ client, fetchPage: pages() }, routine);

    expect(outcome).toEqual({
      kind: "completed",
      jobId: 42,
      newHighlights: 6,
      booksTotal: 2,
      truncatedBooks: 0,
    });
    expect(calls.completed).toEqual([{ booksTotal: 2, booksDone: 2, truncatedBooks: 0 }]);
  });

  it("re-reads every book on a full resync even when the server sends a fingerprint", async () => {
    const { client, calls } = fakeClient({ knownBooks: [{ key: "the pragmatic programmer", highlightCount: 3 }] });

    const outcome = await runSync({ client, fetchPage: pages() }, { ...routine, mode: "full_resync", trigger: "manual" });

    expect(outcome.kind).toBe("completed");
    expect(calls.completed).toEqual([{ booksTotal: 3, booksDone: 3, truncatedBooks: 0 }]);
  });

  it("runs at most one sync at a time and coalesces overlapping firings", async () => {
    const { client, calls } = fakeClient();
    const deps: SyncRunnerDeps = { client, fetchPage: pages() };

    expect(isSyncRunning()).toBe(false);

    const [first, second, third] = await Promise.all([runSync(deps, routine), runSync(deps, routine), runSync(deps, routine)]);

    expect(first).toEqual(second);
    expect(second).toEqual(third);
    expect(calls.statusCalls).toBe(1);
    expect(calls.started).toHaveLength(1);
    expect(isSyncRunning()).toBe(false);

    await runSync(deps, { ...routine, idempotencyKey: "key-2" });
    expect(calls.started).toHaveLength(2);
  });

  it("releases the lock when a run fails, so the next cycle retries", async () => {
    const { client, calls } = fakeClient();
    const failing: SyncRunnerDeps = {
      client,
      fetchPage: () => Promise.resolve(page(fixture("sign-in.html"), "https://www.amazon.com/ap/signin")),
    };

    const outcome = await runSync(failing, routine);

    expect(outcome).toEqual({ kind: "failed", jobId: 42, code: "auth_expired" });
    expect(calls.failed).toEqual([{ code: "auth_expired", diagnostics: null }]);
    expect(isSyncRunning()).toBe(false);
  });

  it("reports provider_contract_changed with selector diagnostics and no page text", async () => {
    const { client, calls } = fakeClient();
    const deps: SyncRunnerDeps = {
      client,
      fetchPage: () => Promise.resolve(page(fixture("changed-layout.html"))),
    };

    const outcome = await runSync(deps, routine);

    expect(outcome).toEqual({ kind: "failed", jobId: 42, code: "provider_contract_changed" });
    expect(calls.failed[0]?.diagnostics).toEqual({
      profileVersion: PROFILE_VERSION,
      check: "bookRow",
      counts: { bookRows: 0 },
    });
    expect(JSON.stringify(calls.failed[0])).not.toContain("Pragmatic");
  });

  it("reports network when the notebook cannot be fetched", async () => {
    const { client, calls } = fakeClient();
    const deps: SyncRunnerDeps = { client, fetchPage: () => Promise.reject(new Error("offline")) };

    const outcome = await runSync(deps, routine);

    expect(outcome).toEqual({ kind: "failed", jobId: 42, code: "network" });
    expect(calls.failed).toEqual([{ code: "network", diagnostics: null }]);
  });

  it("reports auth_expired when the notebook redirects to Amazon sign-in", async () => {
    const { client, calls } = fakeClient();
    const deps: SyncRunnerDeps = {
      client,
      fetchPage: () => Promise.resolve(page(fixture("library.html"), "https://www.amazon.com/ap/signin")),
    };

    const outcome = await runSync(deps, routine);

    expect(outcome).toEqual({ kind: "failed", jobId: 42, code: "auth_expired" });
    expect(calls.failed).toEqual([{ code: "auth_expired", diagnostics: null }]);
  });

  it("reports server_rejected when a batch is refused", async () => {
    const { client, calls } = fakeClient({ batchStatus: 422 });

    const outcome = await runSync({ client, fetchPage: pages() }, routine);

    expect(outcome).toEqual({ kind: "failed", jobId: 42, code: "server_rejected" });
    expect(calls.failed).toEqual([{ code: "server_rejected", diagnostics: null }]);
    expect(calls.completed).toEqual([]);
  });

  it("reports interrupted when the run is aborted mid-way", async () => {
    const { client, calls } = fakeClient();
    const controller = new AbortController();
    const fetchPage = vi.fn((url: string) => {
      controller.abort();
      return pages()(url);
    });

    const outcome = await runSync({ client, fetchPage, signal: controller.signal }, routine);

    expect(outcome).toEqual({ kind: "failed", jobId: 42, code: "interrupted" });
    expect(calls.failed).toEqual([{ code: "interrupted", diagnostics: null }]);
    expect(calls.completed).toEqual([]);
  });

  it("skips without starting a job when the connection is not paired", async () => {
    const { client, calls } = fakeClient({ region: null });

    const outcome = await runSync({ client, fetchPage: pages() }, routine);

    expect(outcome).toEqual({ kind: "skipped", reason: "not_connected" });
    expect(calls.started).toEqual([]);
  });

  it("fails when the server reports a region this build does not know", async () => {
    const { client, calls } = fakeClient({ region: "jp" });

    const outcome = await runSync({ client, fetchPage: pages() }, routine);

    expect(outcome).toEqual({ kind: "failed", jobId: null, code: "provider_contract_changed" });
    expect(calls.started).toEqual([]);
  });

  it("still resolves as failed when the failure cannot be reported", async () => {
    const { client } = fakeClient();
    const failing = {
      ...client,
      failJob: () => Promise.reject(new Error("server down")),
    } as unknown as RelegoClient;

    const outcome = await runSync({ client: failing, fetchPage: () => Promise.reject(new Error("offline")) }, routine);

    expect(outcome).toEqual({ kind: "failed", jobId: 42, code: "network" });
  });
});
