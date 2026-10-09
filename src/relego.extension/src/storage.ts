/**
 * Configuration helpers backed by `chrome.storage.local`.
 *
 * Only the Relego server URL and the pairing token are stored; no Amazon data (cookies, session,
 * page content) is ever written to storage.
 */

import type { RelegoClientConfig } from "./client";

const SERVER_URL_KEY = "serverUrl";
const TOKEN_KEY = "token";

/** Reads the stored server configuration, or `null` when the extension is not paired. */
export async function readConfig(): Promise<RelegoClientConfig | null> {
  const stored = await chrome.storage.local.get([SERVER_URL_KEY, TOKEN_KEY]);
  const baseUrl = stored[SERVER_URL_KEY];
  const token = stored[TOKEN_KEY];

  if (typeof baseUrl !== "string" || typeof token !== "string") return null;
  if (baseUrl.length === 0 || token.length === 0) return null;

  return { baseUrl, token };
}

/** Persists the server configuration. */
export async function writeConfig(config: RelegoClientConfig): Promise<void> {
  await chrome.storage.local.set({
    [SERVER_URL_KEY]: config.baseUrl,
    [TOKEN_KEY]: config.token,
  });
}

/** Clears the stored server configuration. */
export async function clearConfig(): Promise<void> {
  await chrome.storage.local.remove([SERVER_URL_KEY, TOKEN_KEY]);
}
