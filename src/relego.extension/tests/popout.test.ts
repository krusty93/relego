/**
 * Detached-window helper tests.
 *
 * The toolbar popup and the window it can pop out into load the same document, so the only thing
 * that tells them apart is the marker parameter these helpers read and write. Everything here is
 * pure string handling — no browser is involved.
 */

import { describe, expect, it } from "vitest";

import { isWindowed, windowedUrl, WINDOW_HEIGHT, WINDOW_PARAM, WINDOW_WIDTH } from "../src/popout";

const POPUP_URL = "chrome-extension://abcdefghijklmnop/popup.html";

describe("isWindowed", () => {
  it("reports a popup loaded in the toolbar", () => {
    expect(isWindowed("")).toBe(false);
    expect(isWindowed("?")).toBe(false);
    expect(isWindowed("?region=us")).toBe(false);
  });

  it("reports a document opened through windowedUrl", () => {
    const search = windowedUrl(POPUP_URL).slice(POPUP_URL.length);

    expect(isWindowed(search)).toBe(true);
  });

  it("ignores an empty marker, which the browser may leave behind", () => {
    expect(isWindowed(`?${WINDOW_PARAM}=`)).toBe(false);
  });

  it("ignores a marker spelled with a different case", () => {
    expect(isWindowed("?Window=1")).toBe(false);
  });
});

describe("windowedUrl", () => {
  it("marks the popup document", () => {
    const url = windowedUrl(POPUP_URL);

    expect(url.startsWith(`${POPUP_URL}?`)).toBe(true);
    expect(new URL(url).searchParams.get(WINDOW_PARAM)).toBe("1");
  });

  it("keeps an existing query string", () => {
    const url = new URL(windowedUrl(`${POPUP_URL}?region=uk`));

    expect(url.searchParams.get("region")).toBe("uk");
    expect(url.searchParams.get(WINDOW_PARAM)).toBe("1");
  });

  it("is stable when applied twice", () => {
    const once = windowedUrl(POPUP_URL);
    const twice = windowedUrl(once);

    expect(twice).toBe(once);
    expect(isWindowed(new URL(twice).search)).toBe(true);
  });

  it("asks for a window tall enough for the whole form", () => {
    expect(WINDOW_WIDTH).toBeGreaterThanOrEqual(320);
    expect(WINDOW_HEIGHT).toBeGreaterThanOrEqual(600);
  });
});
