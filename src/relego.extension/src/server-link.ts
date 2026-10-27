/**
 * Links from the popup into the Relego web UI.
 *
 * The one-time code that pairs this browser is generated on the Kindle Notebook Sync page, so the
 * popup offers a way to open it: the user sets the address, opens the page, copies the code and
 * comes back, without hunting for the server in another tab.
 */

import { trimBaseUrl } from "./client";
import { serverOriginPattern } from "./permissions";

/** Path of the Kindle Notebook Sync page in the Relego web UI, which is served under `/app`. */
export const KINDLE_SYNC_ROUTE = "/app/kindle-sync";

/**
 * Builds the address of the Kindle Notebook Sync page on a Relego server.
 *
 * @param serverUrl Server address as typed or stored; surrounding whitespace and trailing slashes
 * are ignored.
 * @returns The absolute page address, or `null` when the server address is not an absolute http(s)
 * URL.
 */
export function kindleSyncUrl(serverUrl: string): string | null {
  const address = serverUrl.trim();
  if (serverOriginPattern(address) === null) return null;

  return `${trimBaseUrl(address)}${KINDLE_SYNC_ROUTE}`;
}
