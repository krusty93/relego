import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchNotebookPage } from "../src/background";
import { RelegoClient } from "../src/client";
import { isSyncRunning, runSync } from "../src/sync-runner";

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function waitForAbort(signal: AbortSignal): Promise<never> {
  return new Promise((_resolve, reject) => {
    if (signal.aborted) reject(signal.reason);
    else signal.addEventListener("abort", () => reject(signal.reason), { once: true });
  });
}

describe("notebook fetch deadline", () => {
  it.each(["headers", "body"])("bounds stalled %s and cleans up the timeout", async (stage) => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn(async (_url: string, init: RequestInit) => {
      const signal = init.signal!;
      if (stage === "headers") return waitForAbort(signal);
      return { ok: true, url: "https://read.amazon.com/notebook", text: () => waitForAbort(signal) };
    }));
    const request = fetchNotebookPage("https://read.amazon.com/notebook");
    const rejected = expect(request).rejects.toMatchObject({ name: "TimeoutError" });
    await vi.advanceTimersByTimeAsync(60_000);
    await rejected;
    expect(vi.getTimerCount()).toBe(0);
  });

  it("propagates cancellation and removes the deadline", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn((_url: string, init: RequestInit) => waitForAbort(init.signal!)));
    const controller = new AbortController();
    const request = fetchNotebookPage("https://read.amazon.com/notebook", controller.signal);
    const rejected = expect(request).rejects.toMatchObject({ name: "AbortError" });
    controller.abort();
    await rejected;
    expect(vi.getTimerCount()).toBe(0);
  });

  it("returns successful content and removes the deadline", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn(async () => new Response("notebook")));
    expect((await fetchNotebookPage("https://read.amazon.com/notebook")).html).toBe("notebook");
    expect(vi.getTimerCount()).toBe(0);
  });

  it("reports a timed out run and releases the lock for the next run", async () => {
    vi.useFakeTimers();
    const failures: string[] = [];
    vi.stubGlobal("fetch", vi.fn(async (url: string, init: RequestInit) => {
      if (url.startsWith("https://read.amazon.com/")) return waitForAbort(init.signal!);
      if (init.method === "GET") return Response.json({
        providerId: "kindle-cloud", status: "connected",
        connection: { region: { id: "us", displayName: "United States", host: "read.amazon.com" } },
      });
      if (url.endsWith("/jobs")) return Response.json({
        jobId: 42, knownBooks: [], reused: false, status: "running",
      });
      if (url.endsWith("/fail")) failures.push(JSON.parse(String(init.body)).code);
      return new Response(null, { status: 204 });
    }));
    const client = new RelegoClient({ baseUrl: "https://relego.test", token: "pairing-test" });
    const options = { mode: "routine", trigger: "scheduled", idempotencyKey: "test" } as const;
    const run = runSync({ client, fetchPage: fetchNotebookPage }, options);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(await run).toEqual({ kind: "failed", jobId: 42, code: "network" });
    expect(failures).toEqual(["network"]);
    expect(isSyncRunning()).toBe(false);
    const next = await runSync({
      client,
      fetchPage: async (url) => ({ url, html: '<div id="kp-notebook-library"><div id="kp-notebook-empty"></div></div>' }),
    }, { ...options, idempotencyKey: "next" });
    expect(next.kind).toBe("completed");
    expect(isSyncRunning()).toBe(false);
  });
});
