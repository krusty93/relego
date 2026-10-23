/**
 * Configuration helpers backed by `chrome.storage.local`.
 *
 * Only the Relego server URL and the pairing token are stored; no Amazon data (cookies, session,
 * page content) is ever written to storage.
 *
 * A second, separate pair of keys holds what the user typed before pairing — the server address and
 * the region — so reopening the popup does not clear the form. A draft is never a configuration:
 * `readConfig` still reports "not paired" until a token exists.
 */

import type { RelegoClientConfig } from "./client";

const SERVER_URL_KEY = "serverUrl";
const TOKEN_KEY = "token";
const DRAFT_SERVER_URL_KEY = "draftServerUrl";
const DRAFT_REGION_KEY = "draftRegion";

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

/**
 * Reads the server address typed before pairing.
 *
 * @returns The kept address, or `""` when the user has not typed one yet.
 */
export async function readDraftServerUrl(): Promise<string> {
  const stored = await chrome.storage.local.get([DRAFT_SERVER_URL_KEY]);
  const value = stored[DRAFT_SERVER_URL_KEY];

  return typeof value === "string" ? value : "";
}

/**
 * Keeps the server address as the user types it, so reopening the popup restores it.
 *
 * @param baseUrl Address currently in the field.
 */
export async function writeDraftServerUrl(baseUrl: string): Promise<void> {
  await chrome.storage.local.set({ [DRAFT_SERVER_URL_KEY]: baseUrl });
}

/**
 * Reads the region picked before pairing.
 *
 * @returns The kept region id, or `null` when the user has not picked one yet.
 */
export async function readDraftRegion(): Promise<string | null> {
  const stored = await chrome.storage.local.get([DRAFT_REGION_KEY]);
  const value = stored[DRAFT_REGION_KEY];

  return typeof value === "string" && value.length > 0 ? value : null;
}

/**
 * Keeps the region as the user picks it, so reopening the popup restores it.
 *
 * @param regionId Allowlisted region id selected in the picker.
 */
export async function writeDraftRegion(regionId: string): Promise<void> {
  await chrome.storage.local.set({ [DRAFT_REGION_KEY]: regionId });
}
