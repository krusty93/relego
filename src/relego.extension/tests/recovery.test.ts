/**
 * Service-worker recovery tests.
 *
 * The worker runs here against a fake `chrome` and a fake `fetch`, so the whole recovery path is
 * exercised end to end without a browser: a heartbeat-returned command reaches the runner, a
 * sign-in redirect surfaces as `auth_expired`, the notebook tab is opened for the user to sign in
 * on Amazon, and the short retry alarm brings the sync back by itself. Credentials are never filled
 * by the extension and are never part of these tests.
 */

import { readFileSync } from "node:fs";
import { afterEach, describe, expect, it, vi } from "vitest";

import { PROVIDER_ID } from "../src/client";
import { CONFIGURED_MESSAGE } from "../src/messages";
import { DEFAULT_INTERVAL_MINUTES, notebookUrl, resolveRegion } from "../src/profile";
import type { KindleRegion } from "../src/profile";

function region(id: string): KindleRegion {
  const resolved = resolveRegion(id);
  if (resolved === null) throw new Error(`The ${id} region must be part of the bundled profile.`);

  return resolved;
}

const us = region("us");

const SERVER = "http://relego.test";

function fixture(name: string): string {
  return readFileSync(new URL(`./fixtures/us/${name}`, import.meta.url), "utf8");
}

interface FakeAlarm {
  readonly name: string;
  readonly periodInMinutes: number | null;
  readonly delayInMinutes: number | null;
  readonly scheduledTime: number;
}

interface FakeChrome {
  readonly alarms: Map<string, FakeAlarm>;
  readonly tabs: { readonly url: string; readonly active: boolean }[];
  readonly alarmListeners: ((alarm: { name: string }) => void)[];
  readonly messageListeners: ((message: unknown) => void)[];
  readonly api: unknown;
}

function createChrome(): FakeChrome {
  const alarms = new Map<string, FakeAlarm>();
  const tabs: { url: string; active: boolean }[] = [];
  const alarmListeners: ((alarm: { name: string }) => void)[] = [];
  const messageListeners: ((message: unknown) => void)[] = [];

  const api = {
    alarms: {
      get: (name: string) => Promise.resolve(alarms.get(name)),
      create: (name: string, info: { delayInMinutes?: number; periodInMinutes?: number }) => {
        const period = info.periodInMinutes ?? info.delayInMinutes ?? 0;
        alarms.set(name, {
          name,
          periodInMinutes: info.periodInMinutes ?? null,
          delayInMinutes: info.delayInMinutes ?? null,
          scheduledTime: Date.now() + period * 60_000,
        });
        return Promise.resolve();
      },
      clear: (name: string) => Promise.resolve(alarms.delete(name)),
      onAlarm: {
        addListener: (listener: (alarm: { name: string }) => void) => {
          alarmListeners.push(listener);
        },
      },
    },
    tabs: {
      create: (info: { url: string; active?: boolean }) => {
        tabs.push({ url: info.url, active: info.active ?? true });
        return Promise.resolve({ id: tabs.length });
      },
    },
    storage: {
      local: {
        get: () => Promise.resolve({ serverUrl: SERVER, token: "pairing-token" }),
        set: () => Promise.resolve(),
        remove: () => Promise.resolve(),
      },
    },
    runtime: {
      onInstalled: { addListener: () => undefined },
      onStartup: { addListener: () => undefined },
      onMessage: {
        addListener: (listener: (message: unknown) => void) => {
          messageListeners.push(listener);
        },
      },
    },
  };

  return { alarms, tabs, alarmListeners, messageListeners, api };
}

interface FakeResponse {
  readonly ok: boolean;
  readonly status: number;
  readonly url: string;
  json: () => Promise<unknown>;
  text: () => Promise<string>;
}

function jsonResponse(body: unknown, status = 200): FakeResponse {
  return {
    ok: status >= 200 && status < 300,
    status,
    url: `${SERVER}/`,
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(JSON.stringify(body)),
  };
}

function pageResponse(html: string, url: string): FakeResponse {
  return { ok: true, status: 200, url, json: () => Promise.resolve(null), text: () => Promise.resolve(html) };
}

/** Mutable state the fake server reads, so a test can sign the user in mid-flight. */
interface ServerState {
  command: string;
  signedIn: boolean;
  library: string;
  intervalMinutes: number;
}

interface Recorded {
  readonly starts: { idempotencyKey: string; mode: string; trigger: string }[];
  readonly failures: { code: string; diagnostics: unknown }[];
  readonly completions: { booksTotal: number; booksDone: number; truncatedBooks: number }[];
  readonly heartbeats: { profileVersion: string; appliedIntervalMinutes: number }[];
  batches: number;
  statusCalls: number;
}

function installFetch(state: ServerState): Recorded {
  const server: Recorded = { starts: [], failures: [], completions: [], heartbeats: [], batches: 0, statusCalls: 0 };
  const region = { id: "us", displayName: "United States", host: us.notebookHost };
  // Exact hostnames, so `evilamazon.com` is never mistaken for the notebook.
  const amazonHosts = new Set<string>([us.notebookHost, ...us.signInHostPatterns]);

  vi.stubGlobal("fetch", (input: string | URL, init?: RequestInit): Promise<FakeResponse> => {
    const url = new URL(typeof input === "string" ? input : input.toString());

    if (amazonHosts.has(url.hostname)) {
      if (!state.signedIn) return Promise.resolve(pageResponse(fixture("sign-in.html"), "https://www.amazon.com/ap/signin"));

      const key = url.searchParams.get("asin");
      if (key !== null) {
        return Promise.resolve(pageResponse(fixture("book.html").replace("B00EXAMPLE01", key), url.toString()));
      }

      return Promise.resolve(pageResponse(fixture(state.library), url.toString()));
    }

    const body = init?.body === undefined ? {} : JSON.parse(String(init.body));
    const path = url.pathname;

    if (path === `/sync/${PROVIDER_ID}/heartbeat`) {
      server.heartbeats.push(body);
      return Promise.resolve(
        jsonResponse({
          command: state.command,
          region,
          intervalMinutes: state.intervalMinutes,
          heartbeatSeconds: 60,
        }),
      );
    }

    if (path === `/sync/${PROVIDER_ID}`) {
      server.statusCalls++;
      return Promise.resolve(jsonResponse({ providerId: PROVIDER_ID, status: "connected", connection: { state: "active", region } }));
    }

    if (path === `/sync/${PROVIDER_ID}/jobs`) {
      server.starts.push(body);
      return Promise.resolve(jsonResponse({ jobId: 42, knownBooks: [] }));
    }

    if (path.endsWith("/batches")) {
      server.batches++;
      return Promise.resolve(
        jsonResponse({ batchIndex: body.batchIndex, replayed: false, newHighlights: 3, duplicateHighlights: 0, newBooks: 1, newAuthors: 1 }),
      );
    }

    if (path.endsWith("/complete")) {
      server.completions.push(body);
      return Promise.resolve(jsonResponse(null, 204));
    }

    if (path.endsWith("/fail")) {
      server.failures.push(body);
      return Promise.resolve(jsonResponse(null, 204));
    }

    return Promise.resolve(jsonResponse(null, 204));
  });

  return server;
}

/** Loads a fresh worker module bound to a fresh fake browser. */
async function loadWorker(overrides: Partial<ServerState> = {}) {
  const state: ServerState = {
    command: "none",
    signedIn: true,
    library: "library.html",
    intervalMinutes: DEFAULT_INTERVAL_MINUTES,
    ...overrides,
  };
  const chrome = createChrome();
  vi.stubGlobal("chrome", chrome.api);
  const server = installFetch(state);

  vi.resetModules();
  const worker = await import("../src/background");
  await new Promise((resolve) => setTimeout(resolve, 0));

  return { worker, server, state, chrome };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("service worker recovery", () => {
  it("executes the sync command the heartbeat returned", async () => {
    const { worker, server } = await loadWorker({ command: "sync" });

    await worker.heartbeat();

    expect(server.starts).toHaveLength(1);
    expect(server.starts[0]?.mode).toBe("routine");
    expect(server.starts[0]?.trigger).toBe("manual");
    expect(server.completions).toEqual([{ booksTotal: 3, booksDone: 3, truncatedBooks: 0 }]);
    expect(server.failures).toEqual([]);
  });

  it("executes a full_resync command as a full re-read", async () => {
    const { worker, server } = await loadWorker({ command: "full_resync" });

    await worker.heartbeat();

    expect(server.starts[0]?.mode).toBe("full_resync");
    expect(server.starts[0]?.trigger).toBe("manual");
    expect(server.completions).toEqual([{ booksTotal: 3, booksDone: 3, truncatedBooks: 0 }]);
    expect(server.batches).toBeGreaterThan(0);
  });

  it("never starts a sync from a heartbeat that carries no command", async () => {
    const { worker, server } = await loadWorker({ command: "none" });

    await worker.heartbeat();

    expect(server.heartbeats).toHaveLength(1);
    expect(server.starts).toEqual([]);
    expect(server.completions).toEqual([]);
  });

  it("opens the notebook tab and arms the retry when Amazon asks for sign-in", async () => {
    const { worker, server, chrome } = await loadWorker({ command: "sync", signedIn: false });

    await worker.heartbeat();

    expect(server.failures).toEqual([{ code: "auth_expired", diagnostics: null }]);
    expect(chrome.tabs).toEqual([{ url: notebookUrl(us), active: true }]);

    const retry = chrome.alarms.get(worker.AUTH_RETRY_ALARM);
    expect(retry?.delayInMinutes).toBe(worker.AUTH_RETRY_MINUTES);
    expect(retry?.periodInMinutes).toBe(worker.AUTH_RETRY_MINUTES);
    expect(retry?.scheduledTime).toBeGreaterThan(Date.now());
  });

  it("keeps retrying without opening a second tab while sign-in is still pending", async () => {
    const { worker, server, chrome } = await loadWorker({ command: "sync", signedIn: false });

    await worker.heartbeat();
    await worker.heartbeat();

    expect(chrome.tabs).toHaveLength(1);
    expect(server.failures).toHaveLength(2);
    expect(chrome.alarms.has(worker.AUTH_RETRY_ALARM)).toBe(true);
  });

  it("keys a sign-in retry on the retry window so a replayed firing reuses the job", async () => {
    const { worker, server, chrome } = await loadWorker({ command: "sync", signedIn: false });

    await worker.heartbeat();
    chrome.alarmListeners.forEach((listener) => listener({ name: worker.AUTH_RETRY_ALARM }));
    await new Promise((resolve) => setTimeout(resolve, 0));
    chrome.alarmListeners.forEach((listener) => listener({ name: worker.AUTH_RETRY_ALARM }));
    await new Promise((resolve) => setTimeout(resolve, 0));

    const retries = server.starts.filter((start) => start.trigger === "retry");
    expect(retries).toHaveLength(2);
    expect(retries[0]?.idempotencyKey).toBe(retries[1]?.idempotencyKey);
    expect(retries[0]?.idempotencyKey).toMatch(new RegExp(`^${PROVIDER_ID}-retry-\\d+$`));
  });

  it("resumes the sync and clears the retry once the user has signed in", async () => {
    const { worker, server, state, chrome } = await loadWorker({ command: "sync", signedIn: false });

    await worker.heartbeat();
    expect(chrome.alarms.has(worker.AUTH_RETRY_ALARM)).toBe(true);

    state.signedIn = true;
    chrome.alarmListeners.forEach((listener) => listener({ name: worker.AUTH_RETRY_ALARM }));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(server.completions).toEqual([{ booksTotal: 3, booksDone: 3, truncatedBooks: 0 }]);
    expect(chrome.alarms.has(worker.AUTH_RETRY_ALARM)).toBe(false);
  });

  it("does not open a sign-in tab for a failure the user cannot fix by signing in", async () => {
    const { worker, server, chrome } = await loadWorker({ command: "sync", library: "changed-layout.html" });

    await worker.heartbeat();

    expect(server.failures.map((failure) => failure.code)).toEqual(["provider_contract_changed"]);
    expect(chrome.tabs).toEqual([]);
    expect(chrome.alarms.has(worker.AUTH_RETRY_ALARM)).toBe(false);
  });

  it("keeps the routine alarm period while a sign-in retry is armed", async () => {
    const { worker, chrome } = await loadWorker({ command: "sync", signedIn: false });

    await worker.heartbeat();

    expect(chrome.alarms.get(worker.SYNC_ALARM)?.periodInMinutes).toBe(DEFAULT_INTERVAL_MINUTES);
    expect(chrome.alarms.get(worker.HEARTBEAT_ALARM)?.periodInMinutes).toBe(worker.HEARTBEAT_MINUTES);
  });

  it("applies a configuration change from the popup and runs the queued command at once", async () => {
    const { worker, server, chrome } = await loadWorker({ command: "full_resync", intervalMinutes: 30 });

    chrome.messageListeners.forEach((listener) =>
      listener({ type: CONFIGURED_MESSAGE, intervalMinutes: 30 }),
    );
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(chrome.alarms.get(worker.SYNC_ALARM)?.periodInMinutes).toBe(30);
    expect(server.heartbeats.map((beat) => beat.appliedIntervalMinutes)).toEqual([30]);
    expect(server.starts.map((start) => start.mode)).toEqual(["full_resync"]);
  });

  it("lets the server's frequency win over the one the popup reported", async () => {
    const { worker, server, chrome } = await loadWorker({ command: "none", intervalMinutes: 15 });

    chrome.messageListeners.forEach((listener) =>
      listener({ type: CONFIGURED_MESSAGE, intervalMinutes: 30 }),
    );
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(server.heartbeats[0]?.appliedIntervalMinutes).toBe(30);
    expect(chrome.alarms.get(worker.SYNC_ALARM)?.periodInMinutes).toBe(15);
  });

  it("ignores a message that is not a configuration change", async () => {
    const { server, chrome } = await loadWorker({ command: "none" });

    chrome.messageListeners.forEach((listener) => listener({ type: "relego:unknown" }));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(server.heartbeats).toEqual([]);
  });
});
