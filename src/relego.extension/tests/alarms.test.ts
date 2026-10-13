/**
 * Alarm scheduling tests.
 *
 * Routine timing belongs to the extension alone: the `kindle-sync` alarm carries the user's chosen
 * frequency, the `kindle-heartbeat` alarm is a fixed sixty seconds that never varies with it, and a
 * heartbeat never starts a routine sync. These tests pin the four promises that make the frequency
 * trustworthy: the alarm matches the frequency the server reports, the heartbeat stays independent,
 * a browser that was away across many due runs pays for one sync instead of a backlog, and
 * overlapping firings coalesce into a single pass.
 */

import { readFileSync } from "node:fs";
import { afterEach, describe, expect, it, vi } from "vitest";

import { PROVIDER_ID } from "../src/client";
import {
  AUTH_RETRY_ALARM,
  HEARTBEAT_ALARM,
  HEARTBEAT_MINUTES,
  SYNC_ALARM,
  catchUpIfDue,
  start,
} from "../src/background";
import { ALLOWED_INTERVAL_MINUTES, DEFAULT_INTERVAL_MINUTES, resolveRegion } from "../src/profile";
import type { KindleRegion } from "../src/profile";

const SERVER = "http://relego.test";

function region(id: string): KindleRegion {
  const resolved = resolveRegion(id);
  if (resolved === null) throw new Error(`The ${id} region must be part of the bundled profile.`);

  return resolved;
}

const us = region("us");

function fixture(name: string): string {
  return readFileSync(new URL(`./fixtures/us/${name}`, import.meta.url), "utf8");
}

interface FakeAlarm {
  readonly name: string;
  readonly periodInMinutes: number | null;
  readonly delayInMinutes: number | null;
  scheduledTime: number;
}

interface FakeChrome {
  readonly alarms: Map<string, FakeAlarm>;
  readonly alarmListeners: ((alarm: { readonly name: string }) => void)[];
  readonly api: unknown;
}

/** Alarms that behave like Chrome's: a new alarm first fires one period from now. */
function createChrome(seed: readonly Partial<FakeAlarm>[] = []): FakeChrome {
  const alarms = new Map<string, FakeAlarm>();
  const alarmListeners: ((alarm: { readonly name: string }) => void)[] = [];

  seed.forEach((alarm) => {
    alarms.set(alarm.name ?? SYNC_ALARM, {
      name: alarm.name ?? SYNC_ALARM,
      periodInMinutes: alarm.periodInMinutes ?? null,
      delayInMinutes: alarm.delayInMinutes ?? null,
      scheduledTime: alarm.scheduledTime ?? Date.now(),
    });
  });

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
        addListener: (listener: (alarm: { readonly name: string }) => void) => {
          alarmListeners.push(listener);
        },
      },
    },
    tabs: { create: () => Promise.resolve({ id: 1 }) },
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
      onMessage: { addListener: () => undefined },
    },
  };

  return { alarms, alarmListeners, api };
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

/** Frequency the fake server reports as stored, and every job start the worker requested. */
interface ServerState {
  intervalMinutes: number;
}

interface Recorded {
  readonly starts: { idempotencyKey: string; mode: string; trigger: string }[];
  readonly heartbeats: { appliedIntervalMinutes: number; nextSyncDueAt: string | null }[];
}

function installFetch(state: ServerState): Recorded {
  const server: Recorded = { starts: [], heartbeats: [] };
  const amazonHosts = new Set<string>([us.notebookHost, ...us.signInHostPatterns]);

  vi.stubGlobal("fetch", (input: string | URL, init?: RequestInit): Promise<FakeResponse> => {
    const url = new URL(typeof input === "string" ? input : input.toString());

    if (amazonHosts.has(url.hostname)) {
      const key = url.searchParams.get("bookKey");

      return Promise.resolve(
        key === null
          ? pageResponse(fixture("library.html"), url.toString())
          : pageResponse(fixture("book.html").replace("B00EXAMPLE01", key), url.toString()),
      );
    }

    const body = init?.body === undefined ? {} : JSON.parse(String(init.body));
    const path = url.pathname;

    if (path === `/sync/${PROVIDER_ID}/heartbeat`) {
      server.heartbeats.push(body);

      return Promise.resolve(
        jsonResponse({
          command: "none",
          region: { id: "us", displayName: "United States", host: us.notebookHost },
          intervalMinutes: state.intervalMinutes,
          heartbeatSeconds: 60,
        }),
      );
    }

    if (path === `/sync/${PROVIDER_ID}`) {
      return Promise.resolve(
        jsonResponse({
          providerId: PROVIDER_ID,
          status: "connected",
          region: { id: "us", displayName: "United States", host: us.notebookHost },
        }),
      );
    }

    if (path === `/sync/${PROVIDER_ID}/jobs`) {
      server.starts.push(body);
      return Promise.resolve(jsonResponse({ jobId: 42, knownBooks: [] }));
    }

    if (path.endsWith("/batches")) {
      return Promise.resolve(
        jsonResponse({ batchIndex: body.batchIndex, replayed: false, newHighlights: 3, duplicateHighlights: 0, newBooks: 1, newAuthors: 1 }),
      );
    }

    return Promise.resolve(jsonResponse(null, 204));
  });

  return server;
}

/** Lets the worker's own module-level start-up and any run it kicked off settle. */
async function settle(times = 6): Promise<void> {
  for (let index = 0; index < times; index++) await new Promise((resolve) => setTimeout(resolve, 0));
}

/** Loads a fresh worker bound to a fresh fake browser, with the given alarms already present. */
async function loadWorker(overrides: Partial<ServerState> = {}, seed: readonly Partial<FakeAlarm>[] = []) {
  const state: ServerState = { intervalMinutes: DEFAULT_INTERVAL_MINUTES, ...overrides };
  const chrome = createChrome(seed);
  vi.stubGlobal("chrome", chrome.api);
  const server = installFetch(state);

  vi.resetModules();
  const worker = await import("../src/background");
  await settle();

  return { worker, server, state, chrome };
}

/** The period currently on the routine alarm, or `null` when there is no alarm. */
function syncPeriod(chrome: FakeChrome): number | null | undefined {
  return chrome.alarms.get(SYNC_ALARM)?.periodInMinutes;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("extension alarms", () => {
  it("creates the routine alarm at the default frequency and the heartbeat at sixty seconds", async () => {
    const { chrome } = await loadWorker();

    expect(syncPeriod(chrome)).toBe(DEFAULT_INTERVAL_MINUTES);
    expect(chrome.alarms.get(HEARTBEAT_ALARM)?.periodInMinutes).toBe(HEARTBEAT_MINUTES);
    expect(HEARTBEAT_MINUTES).toBe(1);
  });

  it("applies each of the ten frequencies to the routine alarm", async () => {
    const { worker, chrome } = await loadWorker();

    for (const minutes of ALLOWED_INTERVAL_MINUTES) {
      await worker.applyInterval(minutes);

      expect(syncPeriod(chrome)).toBe(minutes);
    }
  });

  it("keeps the heartbeat at sixty seconds for all ten frequencies", async () => {
    const { worker, chrome } = await loadWorker();

    for (const minutes of ALLOWED_INTERVAL_MINUTES) {
      await worker.applyInterval(minutes);

      expect(chrome.alarms.get(HEARTBEAT_ALARM)?.periodInMinutes).toBe(HEARTBEAT_MINUTES);
    }
  });

  it("never varies the heartbeat with the frequency the server reports", async () => {
    const { worker, chrome } = await loadWorker({ intervalMinutes: 1440 });

    await worker.heartbeat();

    expect(syncPeriod(chrome)).toBe(1440);
    expect(chrome.alarms.get(HEARTBEAT_ALARM)?.periodInMinutes).toBe(HEARTBEAT_MINUTES);
  });

  it("keeps the alarm period an earlier session stored instead of resetting it to the default", async () => {
    const { chrome } = await loadWorker({}, [{ periodInMinutes: 45, scheduledTime: Date.now() + 45 * 60_000 }]);

    expect(syncPeriod(chrome)).toBe(45);
  });

  it("restores the server-stored frequency on the first heartbeat after a restart lost the alarm", async () => {
    const { worker, chrome } = await loadWorker({ intervalMinutes: 45 });

    expect(syncPeriod(chrome)).toBe(DEFAULT_INTERVAL_MINUTES);

    await worker.heartbeat();

    expect(syncPeriod(chrome)).toBe(45);
  });

  it("leaves the alarm untouched when the heartbeat reports the frequency already applied", async () => {
    const { worker, chrome } = await loadWorker({ intervalMinutes: 60 });
    await worker.heartbeat();

    const before = chrome.alarms.get(SYNC_ALARM)?.scheduledTime;

    await worker.heartbeat();

    expect(chrome.alarms.get(SYNC_ALARM)?.scheduledTime).toBe(before);
    expect(syncPeriod(chrome)).toBe(60);
  });

  it("never starts a routine sync from a heartbeat", async () => {
    const { worker, server } = await loadWorker({ intervalMinutes: 120 });

    await worker.heartbeat();

    expect(server.heartbeats).toHaveLength(1);
    expect(server.starts).toEqual([]);
  });

  it("reports the applied frequency and the alarm's due time on every heartbeat", async () => {
    const { worker, chrome, server } = await loadWorker({ intervalMinutes: DEFAULT_INTERVAL_MINUTES });

    await worker.heartbeat();
    const dueAt = chrome.alarms.get(SYNC_ALARM)?.scheduledTime;

    expect(server.heartbeats[0]?.appliedIntervalMinutes).toBe(DEFAULT_INTERVAL_MINUTES);
    expect(server.heartbeats[0]?.nextSyncDueAt).toBe(new Date(dueAt ?? 0).toISOString());
  });

  it("starts no catch-up run while the alarm is still in the future", async () => {
    const { server } = await loadWorker();

    await expect(catchUpIfDue()).resolves.toBe(false);
    expect(server.starts).toEqual([]);
  });

  it("costs one catch-up run no matter how many periods the browser missed", async () => {
    const { server, chrome } = await loadWorker();
    const alarm = chrome.alarms.get(SYNC_ALARM);
    if (alarm === undefined) throw new Error("The routine alarm must exist after start-up.");

    alarm.scheduledTime = Date.now() - 10 * DEFAULT_INTERVAL_MINUTES * 60_000;

    await expect(catchUpIfDue()).resolves.toBe(true);
    await settle();

    expect(server.starts).toHaveLength(1);
    expect(server.starts[0]?.trigger).toBe("scheduled");
    expect(server.starts[0]?.mode).toBe("routine");
  });

  it("re-arms the routine alarm a full period ahead after a catch-up run", async () => {
    const { server, chrome } = await loadWorker();
    const alarm = chrome.alarms.get(SYNC_ALARM);
    if (alarm === undefined) throw new Error("The routine alarm must exist after start-up.");

    alarm.scheduledTime = Date.now() - 5 * DEFAULT_INTERVAL_MINUTES * 60_000;

    await catchUpIfDue();

    const rearmed = chrome.alarms.get(SYNC_ALARM)?.scheduledTime ?? 0;

    expect(rearmed).toBeGreaterThan(Date.now() + (DEFAULT_INTERVAL_MINUTES - 1) * 60_000);
    expect(syncPeriod(chrome)).toBe(DEFAULT_INTERVAL_MINUTES);
    expect(server.starts).toHaveLength(1);
  });

  it("does not repeat a catch-up run when the worker starts again", async () => {
    const { server, chrome } = await loadWorker();
    const alarm = chrome.alarms.get(SYNC_ALARM);
    if (alarm === undefined) throw new Error("The routine alarm must exist after start-up.");

    alarm.scheduledTime = Date.now() - 3 * DEFAULT_INTERVAL_MINUTES * 60_000;

    await catchUpIfDue();
    await settle();
    await start();
    await settle();

    expect(server.starts).toHaveLength(1);
  });

  it("coalesces overlapping firings of the routine alarm into one run", async () => {
    const { chrome, server } = await loadWorker();
    const listener = chrome.alarmListeners[0];
    if (listener === undefined) throw new Error("The alarm listener must be registered on start-up.");

    listener({ name: SYNC_ALARM });
    listener({ name: SYNC_ALARM });
    await settle();

    expect(server.starts).toHaveLength(1);
    expect(server.starts[0]?.trigger).toBe("scheduled");
  });

  it("does not start a routine sync when the heartbeat alarm fires", async () => {
    const { chrome, server } = await loadWorker();
    const listener = chrome.alarmListeners[0];
    if (listener === undefined) throw new Error("The alarm listener must be registered on start-up.");

    listener({ name: HEARTBEAT_ALARM });
    await settle();

    expect(server.heartbeats).toHaveLength(1);
    expect(server.starts).toEqual([]);
  });

  it("arms no sign-in retry alarm until a run actually hits the Amazon sign-in page", async () => {
    const { chrome } = await loadWorker();

    expect(chrome.alarms.has(AUTH_RETRY_ALARM)).toBe(false);
    expect(chrome.alarms.has(SYNC_ALARM)).toBe(true);
    expect(chrome.alarms.has(HEARTBEAT_ALARM)).toBe(true);
  });
});
