/**
 * Configuration helpers backed by `chrome.storage.local`.
 *
 * The server URL, pairing token and pending auth-retry mode are stored; no Amazon data (cookies,
 * session, page content) is ever written to storage.
 */

import type { RelegoClientConfig } from "./client";
import type { SyncMode } from "./sync-runner";

const SERVER_URL_KEY = "serverUrl";
const TOKEN_KEY = "token";
const AUTH_RETRY_MODE_KEY = "authRetryMode";

/** Reads the interrupted sync mode, or `null` when no authentication retry is pending. */
export async function readAuthRetryMode(): Promise<SyncMode | null> {
  const stored = await chrome.storage.local.get(AUTH_RETRY_MODE_KEY);
  const mode = stored[AUTH_RETRY_MODE_KEY];
  if (mode === undefined) return null;
  if (mode === "routine" || mode === "full_resync") return mode;
  throw new Error("Stored authentication retry mode is invalid.");
}

/** Persists the interrupted sync mode before arming the authentication retry. */
export async function writeAuthRetryMode(mode: SyncMode): Promise<void> {
  await chrome.storage.local.set({ [AUTH_RETRY_MODE_KEY]: mode });
}

/** Removes the interrupted mode once authentication recovery is no longer needed. */
export async function clearAuthRetryMode(): Promise<void> {
  await chrome.storage.local.remove(AUTH_RETRY_MODE_KEY);
}

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
  await chrome.storage.local.remove([SERVER_URL_KEY, TOKEN_KEY, AUTH_RETRY_MODE_KEY]);
}
