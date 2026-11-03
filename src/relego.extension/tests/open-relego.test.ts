/**
 * "Open Relego" button tests.
 *
 * The popup carries a button that opens the Kindle Notebook Sync page of the Relego server in a new
 * tab, in both of its views. The address is built from one route constant and the normalised server
 * address; the form's button follows the address being typed, and the confirmation's button uses the
 * paired server. The popup runs here against a minimal fake document, a fake `chrome` and a fake
 * `fetch`, so the wiring is exercised without a browser.
 */

import { afterEach, describe, expect, it, vi } from "vitest";

import { PROVIDER_ID } from "../src/client";
import { KINDLE_SYNC_ROUTE, kindleSyncUrl } from "../src/server-link";

const SERVER = "http://relego.test";
const SYNC_URL = `${SERVER}/app/kindle-sync`;

describe("kindleSyncUrl", () => {
  it("keeps the route in one place, under the web UI's /app", () => {
    expect(KINDLE_SYNC_ROUTE).toBe("/app/kindle-sync");
  });

  it("appends the route to the server address", () => {
    expect(kindleSyncUrl(SERVER)).toBe(SYNC_URL);
    expect(kindleSyncUrl("https://relego.example:8443")).toBe("https://relego.example:8443/app/kindle-sync");
  });

  it("never doubles the slash when the address ends with one", () => {
    expect(kindleSyncUrl(`${SERVER}/`)).toBe(SYNC_URL);
    expect(kindleSyncUrl(`${SERVER}///`)).toBe(SYNC_URL);
  });

  it("ignores whitespace around the address", () => {
    expect(kindleSyncUrl(`  ${SERVER}/ `)).toBe(SYNC_URL);
  });

  it("keeps a path prefix, like the client does for its own calls", () => {
    expect(kindleSyncUrl("https://relego.example/relego/")).toBe("https://relego.example/relego/app/kindle-sync");
  });

  it("refuses an address that is not an absolute http(s) URL", () => {
    expect(kindleSyncUrl("")).toBeNull();
    expect(kindleSyncUrl("   ")).toBeNull();
    expect(kindleSyncUrl("relego.example")).toBeNull();
    expect(kindleSyncUrl("localhost:8080")).toBeNull();
    expect(kindleSyncUrl("ftp://relego.example")).toBeNull();
    expect(kindleSyncUrl("javascript:alert(1)")).toBeNull();
  });
});

type Listener = (event: { preventDefault: () => void }) => void;

/** The few members of an element the popup touches. */
class FakeElement {
  hidden = false;
  disabled = false;
  value = "";
  textContent = "";
  readonly dataset: Record<string, string> = {};
  private readonly listeners = new Map<string, Listener[]>();

  addEventListener(type: string, listener: Listener): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  replaceChildren(): void {
    // The region picker's options are not under test.
  }

  /** Fires an event the way the browser does; a disabled button never receives a click. */
  fire(type: string): void {
    if (this.disabled && type === "click") return;

    for (const listener of this.listeners.get(type) ?? []) listener({ preventDefault: () => undefined });
  }

  /** Types into the field, the way the user does. */
  type(value: string): void {
    this.value = value;
    this.fire("input");
  }
}

interface FakePopup {
  readonly elements: ReadonlyMap<string, FakeElement>;
  readonly tabs: { url: string }[];
  /** Waits until the popup has chosen a view and returns the elements by id. */
  opened: () => Promise<(id: string) => FakeElement>;
}

interface PopupSetup {
  /** What `chrome.storage.local` holds when the popup opens. */
  readonly stored?: Record<string, unknown>;
  /** Makes `chrome.tabs.create` reject, like a browser that refuses to open a tab. */
  readonly tabsRefuse?: boolean;
}

/** Loads popup.ts against a fake document, `chrome` and `fetch`. */
async function openPopup(setup: PopupSetup = {}): Promise<FakePopup> {
  const elements = new Map<string, FakeElement>();
  const tabs: { url: string }[] = [];
  const storage = new Map<string, unknown>(Object.entries(setup.stored ?? {}));

  const element = (id: string): FakeElement => {
    const existing = elements.get(id);
    if (existing !== undefined) return existing;

    const created = new FakeElement();
    if (id === "settings" || id === "done" || id === "cancel") created.hidden = true;
    elements.set(id, created);

    return created;
  };

  vi.stubGlobal("document", {
    getElementById: element,
    createElement: () => new FakeElement(),
    body: new FakeElement(),
  });
  vi.stubGlobal("location", { search: "" });
  vi.stubGlobal("window", { close: () => undefined });
  vi.stubGlobal("chrome", {
    storage: {
      local: {
        get: (keys: string[]) =>
          Promise.resolve(Object.fromEntries(keys.filter((key) => storage.has(key)).map((key) => [key, storage.get(key)]))),
        set: (values: Record<string, unknown>) => {
          for (const [key, value] of Object.entries(values)) storage.set(key, value);

          return Promise.resolve();
        },
      },
    },
    tabs: {
      create: (info: { url: string }) => {
        if (setup.tabsRefuse === true) return Promise.reject(new Error("No tab."));

        tabs.push({ url: info.url });

        return Promise.resolve({ id: tabs.length });
      },
    },
    runtime: { getURL: (path: string) => `chrome-extension://relego/${path}` },
    windows: { create: () => undefined },
  });
  vi.stubGlobal("fetch", (input: string | URL) => {
    const url = new URL(typeof input === "string" ? input : input.toString());
    const body = {
      providerId: PROVIDER_ID,
      status: "connected",
      connection: { state: "active", region: { id: "us", displayName: "United States", host: "read.amazon.com" } },
    };

    return Promise.resolve({
      ok: url.pathname === `/sync/${PROVIDER_ID}`,
      status: 200,
      json: () => Promise.resolve(body),
      text: () => Promise.resolve(JSON.stringify(body)),
    });
  });

  vi.resetModules();
  await import("../src/popup");

  return {
    elements,
    tabs,
    opened: async () => {
      await vi.waitFor(() => {
        expect(element("settings").hidden && element("done").hidden).toBe(false);
      });

      return element;
    },
  };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

/** What a paired browser keeps in storage. */
function paired(serverUrl: string): Record<string, unknown> {
  return { serverUrl, token: "pairing-token" };
}

describe("Open Relego in the form view", () => {
  it("is disabled until the address can be opened", async () => {
    const popup = await openPopup();
    const get = await popup.opened();

    expect(get("settings").hidden).toBe(false);
    expect(get("openForm").disabled).toBe(true);

    get("serverUrl").type("relego.test");
    expect(get("openForm").disabled).toBe(true);

    get("serverUrl").type("ftp://relego.test");
    expect(get("openForm").disabled).toBe(true);

    get("serverUrl").type(SERVER);
    expect(get("openForm").disabled).toBe(false);

    get("serverUrl").type("");
    expect(get("openForm").disabled).toBe(true);
  });

  it("is enabled when the address kept from an earlier visit is valid", async () => {
    const popup = await openPopup({ stored: { draftServerUrl: `${SERVER}/` } });
    const get = await popup.opened();

    expect(get("serverUrl").value).toBe(`${SERVER}/`);
    expect(get("openForm").disabled).toBe(false);
  });

  it("opens the sync page of the typed address in a new tab", async () => {
    const popup = await openPopup();
    const get = await popup.opened();

    get("serverUrl").type(`${SERVER}/`);
    get("openForm").fire("click");

    await vi.waitFor(() => {
      expect(popup.tabs).toEqual([{ url: SYNC_URL }]);
    });
  });

  it("opens the page for the address as it stands when clicked", async () => {
    const popup = await openPopup();
    const get = await popup.opened();

    get("serverUrl").type("http://one.test");
    get("serverUrl").type("https://two.test:9000");
    get("openForm").fire("click");

    await vi.waitFor(() => {
      expect(popup.tabs).toEqual([{ url: "https://two.test:9000/app/kindle-sync" }]);
    });
  });

  it("does nothing for an address that cannot be opened", async () => {
    const popup = await openPopup();
    const get = await popup.opened();

    get("serverUrl").type("relego.test");
    // A click that slips past a disabled button must still not open anything.
    get("openForm").disabled = false;
    get("openForm").fire("click");
    await Promise.resolve();

    expect(popup.tabs).toEqual([]);
  });

  it("explains a refused tab in the notice", async () => {
    const popup = await openPopup({ tabsRefuse: true });
    const get = await popup.opened();

    get("serverUrl").type(SERVER);
    get("openForm").fire("click");

    await vi.waitFor(() => {
      expect(get("notice").textContent).toBe("The browser did not open a new tab.");
    });
    expect(get("notice").dataset["tone"]).toBe("warn");
  });
});

describe("Open Relego in the configuration complete view", () => {
  it("opens the sync page of the paired server in a new tab", async () => {
    const popup = await openPopup({ stored: paired(`${SERVER}/`) });
    const get = await popup.opened();

    expect(get("done").hidden).toBe(false);
    expect(get("settings").hidden).toBe(true);

    get("openDone").fire("click");

    await vi.waitFor(() => {
      expect(popup.tabs).toEqual([{ url: SYNC_URL }]);
    });
  });

  it("uses the paired server, not an address left in the form", async () => {
    const popup = await openPopup({ stored: { ...paired(SERVER), draftServerUrl: "http://draft.test" } });
    const get = await popup.opened();

    get("openDone").fire("click");

    await vi.waitFor(() => {
      expect(popup.tabs).toEqual([{ url: SYNC_URL }]);
    });
  });

  it("is offered again in the form once the user edits the configuration", async () => {
    const popup = await openPopup({ stored: paired(SERVER) });
    const get = await popup.opened();

    get("edit").fire("click");

    expect(get("settings").hidden).toBe(false);
    expect(get("openForm").disabled).toBe(false);

    get("openForm").fire("click");

    await vi.waitFor(() => {
      expect(popup.tabs).toEqual([{ url: SYNC_URL }]);
    });
  });
});
