/**
 * Configuration helpers backed by `chrome.storage.local`.
 *
 * The server URL, pairing token, pending auth-retry mode and the next routine sync's due time are
 * stored; no Amazon data (cookies, session, page content) is ever written to storage. The due time
 * is a timestamp only: it exists so a lost alarm is still caught up instead of postponing the next
 * run by a whole period.
 *
 * A second, separate pair of keys holds what the user typed before pairing — the server address and
 * the region — so reopening the popup does not clear the form. A draft is never a configuration:
 * `readConfig` still reports "not paired" until a token exists.
 */

import type { RelegoClientConfig } from "./client";
import type { SyncMode } from "./sync-runner";

const SERVER_URL_KEY = "serverUrl";
const TOKEN_KEY = "token";
const AUTH_RETRY_MODE_KEY = "authRetryMode";
const NEXT_SYNC_DUE_AT_KEY = "nextSyncDueAt";
const DRAFT_SERVER_URL_KEY = "draftServerUrl";
const DRAFT_REGION_KEY = "draftRegion";

/** Reads the recorded due time of the next routine sync, or `null` when none is recorded. */
export async function readNextSyncDueAt(): Promise<number | null> {
  const stored = await chrome.storage.local.get(NEXT_SYNC_DUE_AT_KEY);
  const dueAt = stored[NEXT_SYNC_DUE_AT_KEY];
  if (dueAt === undefined) return null;
  if (typeof dueAt !== "number" || !Number.isFinite(dueAt)) {
    throw new Error("Stored sync due time is invalid.");
  }

  return dueAt;
}

/**
 * Records when the routine sync alarm is due.
 *
 * Chrome persists alarms but can still drop one, and a worker start recreates a missing alarm a
 * full period out. Keeping the due time lets the worker tell a dropped alarm apart from a fresh
 * profile, so a period missed while the browser was away costs one catch-up run.
 */
export async function writeNextSyncDueAt(dueAt: number): Promise<void> {
  await chrome.storage.local.set({ [NEXT_SYNC_DUE_AT_KEY]: dueAt });
}

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
  await chrome.storage.local.remove([
    SERVER_URL_KEY,
    TOKEN_KEY,
    AUTH_RETRY_MODE_KEY,
    NEXT_SYNC_DUE_AT_KEY,
  ]);
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
