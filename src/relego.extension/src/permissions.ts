/**
 * Runtime host permissions for the region picker.
 *
 * The extension installs with no host access. Every host it reads is requested at the moment the
 * user acts, and only from the bundled region allowlist: the selected region's official notebook
 * host, plus the user's own Relego server. Selecting another region releases the previous region's
 * host, so the extension never keeps access it has stopped using.
 *
 * Origin patterns are derived here from the allowlist and never from user input, so no free-text
 * host can reach the permissions API.
 */

import { REGIONS } from "./profile";
import type { KindleRegion } from "./profile";

/** Origin pattern that grants read access to a region's Kindle notebook host. */
export function notebookOrigin(region: KindleRegion): string {
  return `https://${region.notebookHost}/*`;
}

/**
 * Origin pattern that grants access to the user's Relego server.
 *
 * @param baseUrl Server address the user typed.
 * @returns The pattern, or `null` when the address is not an absolute http(s) URL.
 */
export function serverOriginPattern(baseUrl: string): string | null {
  let url: URL;

  try {
    url = new URL(baseUrl);
  } catch {
    return null;
  }

  if (url.protocol !== "http:" && url.protocol !== "https:") return null;

  return `${url.origin}/*`;
}

/** Notebook origins of the bundled allowlist, in allowlist order. */
export function allowlistedNotebookOrigins(): readonly string[] {
  return REGIONS.map(notebookOrigin);
}

/**
 * Granted notebook origins the selected region does not use.
 *
 * @param granted Origins the browser currently grants.
 * @param region Region the user selected.
 * @returns The origins to release, in allowlist order; empty when nothing is stale.
 */
export function staleNotebookOrigins(granted: readonly string[], region: KindleRegion): readonly string[] {
  const keep = notebookOrigin(region);
  const grantedSet = new Set(granted);

  return allowlistedNotebookOrigins().filter((origin) => origin !== keep && grantedSet.has(origin));
}

/** The host-permission surface the picker needs, as a port so tests run without a browser. */
export interface HostPermissions {
  /** Reports whether every given origin is already granted. */
  readonly contains: (origins: readonly string[]) => Promise<boolean>;
  /** Asks the user for the given origins; must be called from a user gesture. */
  readonly request: (origins: readonly string[]) => Promise<boolean>;
  /** Drops the given origins. */
  readonly remove: (origins: readonly string[]) => Promise<void>;
  /** Lists the origins currently granted. */
  readonly granted: () => Promise<readonly string[]>;
}

/** Host-permission port backed by the extension permissions API. */
export const browserHostPermissions: HostPermissions = {
  contains: (origins) => chrome.permissions.contains({ origins: [...origins] }),
  request: (origins) => chrome.permissions.request({ origins: [...origins] }),
  remove: async (origins) => {
    await chrome.permissions.remove({ origins: [...origins] });
  },
  granted: async () => (await chrome.permissions.getAll()).origins ?? [],
};
