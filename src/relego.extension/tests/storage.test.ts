/**
 * Draft-storage tests.
 *
 * The popup document is destroyed whenever it closes, so anything typed before a pairing exists has
 * to survive in storage. These tests pin the separation that makes that safe: a draft is kept so the
 * form comes back filled in, but it never counts as a pairing on its own.
 */

import { afterEach, describe, expect, it, vi } from "vitest";

import {
  readConfig,
  readDraftRegion,
  readDraftServerUrl,
  writeConfig,
  writeDraftRegion,
  writeDraftServerUrl,
} from "../src/storage";

/** Installs a `chrome.storage.local` stand-in backed by a plain object. */
function stubChrome(initial: Record<string, unknown> = {}): Map<string, unknown> {
  const state = new Map<string, unknown>(Object.entries(initial));

  vi.stubGlobal("chrome", {
    storage: {
      local: {
        get: (keys: string[]) =>
          Promise.resolve(
            Object.fromEntries(keys.filter((key) => state.has(key)).map((key) => [key, state.get(key)])),
          ),
        set: (values: Record<string, unknown>) => {
          for (const [key, value] of Object.entries(values)) state.set(key, value);

          return Promise.resolve();
        },
        remove: (keys: string[]) => {
          for (const key of keys) state.delete(key);

          return Promise.resolve();
        },
      },
    },
  });

  return state;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("draft server address", () => {
  it("starts empty when nothing was typed", async () => {
    stubChrome();

    expect(await readDraftServerUrl()).toBe("");
  });

  it("comes back after the popup closed", async () => {
    stubChrome();
    await writeDraftServerUrl("http://localhost:8080");

    expect(await readDraftServerUrl()).toBe("http://localhost:8080");
  });

  it("is not a pairing on its own", async () => {
    stubChrome();
    await writeDraftServerUrl("http://localhost:8080");

    expect(await readConfig()).toBeNull();
  });

  it("does not disturb a pairing that already exists", async () => {
    stubChrome();
    await writeConfig({ baseUrl: "http://relego.test", token: "token" });
    await writeDraftServerUrl("http://localhost:8080");

    expect(await readConfig()).toEqual({ baseUrl: "http://relego.test", token: "token" });
  });
});

describe("draft region", () => {
  it("starts unset when nothing was picked", async () => {
    stubChrome();

    expect(await readDraftRegion()).toBeNull();
  });

  it("comes back after the popup closed", async () => {
    stubChrome();
    await writeDraftRegion("uk");

    expect(await readDraftRegion()).toBe("uk");
  });

  it("treats an empty value as unset", async () => {
    stubChrome({ draftRegion: "" });

    expect(await readDraftRegion()).toBeNull();
  });
});
