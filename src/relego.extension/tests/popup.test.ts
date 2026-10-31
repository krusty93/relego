import { afterEach, describe, expect, it, vi } from "vitest";

import { CONFIGURED_MESSAGE } from "../src/messages";

const SERVER = "http://relego.test";
const NOTEBOOK = "https://read.amazon.com/*";

function createElement(value = "") {
  return {
    value,
    disabled: false,
    textContent: "",
    dataset: {} as Record<string, string>,
    replaceChildren: vi.fn(),
    addEventListener: vi.fn(),
  };
}

async function loadPopup(paired = true) {
  const elements = {
    settings: createElement(),
    state: createElement(),
    serverUrl: createElement(),
    code: createElement(),
    region: createElement("us"),
    disclosure: createElement(),
    notice: createElement(),
    save: createElement(),
  };
  const stored: Record<string, unknown> = paired ? { serverUrl: SERVER, token: "old-code" } : {};
  const writes = vi.fn(async (values: Record<string, unknown>) => { Object.assign(stored, values); });
  const request = vi.fn(async () => true);
  const sendMessage = vi.fn(async () => undefined);
  const get = vi.fn(async () => ({ ...stored }));
  vi.stubGlobal("document", {
    getElementById: (id: string) => elements[id as keyof typeof elements],
    createElement: () => createElement(),
  });
  vi.stubGlobal("chrome", {
    storage: { local: { get, set: writes } },
    permissions: {
      contains: async () => paired,
      request,
      getAll: async () => ({ origins: paired ? [NOTEBOOK] : [] }),
      remove: async () => true,
    },
    runtime: { sendMessage },
  });

  let regionId = "us";
  let pairStatus = 200;
  const calls: { path: string; token: string | null; body: unknown }[] = [];
  const fetch = vi.fn(async (input: string, init?: RequestInit) => {
    const path = new URL(input).pathname;
    const body = init?.body === undefined ? null : JSON.parse(String(init.body));
    calls.push({ path, token: new Headers(init?.headers).get("Authorization"), body });
    if (path.endsWith("/pair")) {
      return new Response(JSON.stringify({ intervalMinutes: 360 }), { status: pairStatus });
    }
    if (path.endsWith("/region")) {
      regionId = body.regionId;
      return Response.json({ pendingCommand: "full_resync" });
    }
    return Response.json({
      providerId: "kindle-cloud",
      status: "connected",
      connection: { region: { id: regionId, displayName: "United States", host: "read.amazon.com" } },
    });
  });
  vi.stubGlobal("fetch", fetch);
  vi.resetModules();
  await import("../src/popup");
  await vi.waitFor(() => expect(elements.settings.addEventListener).toHaveBeenCalledWith("submit", expect.any(Function)));
  const submit = elements.settings.addEventListener.mock.calls[0]?.[1] as (event: { preventDefault(): void }) => void;

  return {
    elements, stored, writes, request, fetch, calls, sendMessage, get,
    setPairStatus: (status: number) => { pairStatus = status; },
    submit: () => submit({ preventDefault: () => undefined }),
    settled: async () => { await vi.waitFor(() => expect(elements.save.disabled).toBe(false)); },
  };
}

afterEach(() => vi.unstubAllGlobals());

describe("popup configuration", () => {
  it("renders the nested status region and uses it to avoid a redundant region change", async () => {
    const popup = await loadPopup();
    expect(popup.elements.state.textContent).toContain("Reading United States");
    popup.submit();
    await popup.settled();
    expect(popup.calls.map((call) => call.path)).toEqual(["/sync/kindle-cloud"]);
    expect(popup.elements.notice.textContent).toContain("Nothing changed");
  });

  it("requests permission synchronously without rereading storage or waiting for status", async () => {
    const popup = await loadPopup();
    popup.elements.region.value = "de";
    popup.fetch.mockClear();
    popup.get.mockClear();

    popup.submit();

    expect(popup.request).toHaveBeenCalledWith({ origins: ["https://lesen.amazon.de/*"] });
    expect(popup.fetch).not.toHaveBeenCalled();
    expect(popup.get).not.toHaveBeenCalled();
    await popup.settled();
    expect(popup.calls.filter((call) => call.path.endsWith("/region"))[0]?.body).toEqual({ regionId: "de" });
    expect(popup.sendMessage).toHaveBeenCalledWith({
      type: CONFIGURED_MESSAGE, reason: "region_changed", intervalMinutes: undefined,
    });
  });

  it("uses a replacement code to re-pair the same server and starts an initial sync", async () => {
    const popup = await loadPopup();
    popup.elements.code.value = "replacement";
    popup.submit();
    await popup.settled();

    expect(popup.calls.find((call) => call.path.endsWith("/pair"))?.token).toBe("Bearer replacement");
    expect(popup.stored).toEqual({ serverUrl: SERVER, token: "replacement" });
    expect(popup.writes).toHaveBeenCalledTimes(1);
    expect(popup.elements.code.value).toBe("");
    expect(popup.sendMessage).toHaveBeenCalledWith({
      type: CONFIGURED_MESSAGE, reason: "paired", intervalMinutes: 360,
    });
  });

  it.each(["denied", "invalid address", "rejected code"])("preserves the working config after %s", async (failure) => {
    const popup = await loadPopup();
    popup.elements.serverUrl.value = failure === "invalid address" ? "localhost:8080" : "http://new-relego.test";
    popup.elements.code.value = "new-code";
    if (failure === "denied") popup.request.mockResolvedValue(false);
    if (failure === "rejected code") popup.setPairStatus(401);
    popup.submit();
    await popup.settled();

    expect(popup.writes).not.toHaveBeenCalled();
    expect(popup.stored).toEqual({ serverUrl: SERVER, token: "old-code" });
    expect(popup.sendMessage).not.toHaveBeenCalled();
    expect(popup.elements.notice.dataset["tone"]).toBe("warn");
  });

  it("retries pairing after a failed first setup instead of treating it as connected", async () => {
    const popup = await loadPopup(false);
    popup.elements.serverUrl.value = SERVER;
    popup.elements.code.value = "new-code";
    popup.setPairStatus(401);
    popup.submit();
    await popup.settled();
    expect(popup.stored).toEqual({});
    expect(popup.elements.notice.textContent).toContain("did not accept");

    popup.setPairStatus(200);
    popup.submit();
    await popup.settled();
    expect(popup.calls.filter((call) => call.path.endsWith("/pair"))).toHaveLength(2);
    expect(popup.writes).toHaveBeenCalledTimes(1);
    expect(popup.stored).toEqual({ serverUrl: SERVER, token: "new-code" });
  });

  it("reports a missed worker notification without claiming that pairing failed", async () => {
    const popup = await loadPopup(false);
    popup.elements.serverUrl.value = SERVER;
    popup.elements.code.value = "new-code";
    popup.sendMessage.mockRejectedValue(new Error("worker unavailable"));
    popup.submit();
    await popup.settled();

    expect(popup.stored).toEqual({ serverUrl: SERVER, token: "new-code" });
    expect(popup.elements.notice.textContent).toContain("Saved, but the background sync could not be started");
  });
});
