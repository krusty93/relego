/**
 * Extension service worker (Manifest V3).
 *
 * Owns the two alarms the profile contract defines and drives the sync runner from them:
 *
 * - `kindle-sync` fires at the user's chosen frequency and runs a routine sync. Its period is
 *   re-applied on every service-worker and browser start and whenever the server reports a
 *   different stored frequency.
 * - `kindle-heartbeat` fires every sixty seconds, reports liveness and the applied frequency, and
 *   executes at most one user-requested command. A heartbeat never starts a routine sync.
 *
 * Amazon authentication, CAPTCHA and MFA are always completed by the user on Amazon's own page;
 * this worker only reads the notebook in the session the user already has.
 */

import { PROVIDER_ID, RelegoClient } from "./client";
import {
  DEFAULT_INTERVAL_MINUTES,
  HEARTBEAT_SECONDS,
  PROFILE_VERSION,
  clampIntervalMinutes,
} from "./profile";
import { readConfig } from "./storage";
import { runSync } from "./sync-runner";
import type { PageResponse, SyncMode, SyncRunOutcome, SyncTrigger } from "./sync-runner";

/** Name of the routine sync alarm. */
export const SYNC_ALARM = "kindle-sync";

/** Name of the liveness alarm. */
export const HEARTBEAT_ALARM = "kindle-heartbeat";

/** Period of the liveness alarm, in minutes. */
export const HEARTBEAT_MINUTES = HEARTBEAT_SECONDS / 60;

/** Reports whether the extension has been paired with a Relego server. */
export async function isConfigured(): Promise<boolean> {
  return (await readConfig()) !== null;
}

/** Creates a client for the stored configuration, or `null` when the extension is not paired. */
export async function createClient(): Promise<RelegoClient | null> {
  const config = await readConfig();
  return config === null ? null : new RelegoClient(config);
}

/**
 * Creates the two alarms when they are missing and restores the sync alarm to the stored
 * frequency.
 *
 * The sync alarm is only recreated when its period differs, so waking the worker does not keep
 * pushing the next run further out; Chrome persists alarms across browser restarts, and creating
 * them here covers a fresh profile.
 *
 * @returns A task that completes once both alarms exist.
 */
export async function ensureAlarms(): Promise<void> {
  const sync = await chrome.alarms.get(SYNC_ALARM);
  const interval = sync?.periodInMinutes ?? DEFAULT_INTERVAL_MINUTES;

  if (sync === undefined || sync.periodInMinutes !== interval) {
    await chrome.alarms.create(SYNC_ALARM, { periodInMinutes: interval });
  }

  const heartbeat = await chrome.alarms.get(HEARTBEAT_ALARM);

  if (heartbeat === undefined || heartbeat.periodInMinutes !== HEARTBEAT_MINUTES) {
    await chrome.alarms.create(HEARTBEAT_ALARM, { periodInMinutes: HEARTBEAT_MINUTES });
  }
}

/**
 * Applies a sync frequency to the routine alarm.
 *
 * @param minutes Frequency requested by the server or the user; clamped to the allowlist.
 * @returns A task that completes once the alarm has been recreated.
 */
export async function applyInterval(minutes: number): Promise<void> {
  await chrome.alarms.create(SYNC_ALARM, { periodInMinutes: clampIntervalMinutes(minutes) });
}

/** Reads the frequency currently applied to the routine alarm, in minutes. */
export async function appliedIntervalMinutes(): Promise<number> {
  const alarm = await chrome.alarms.get(SYNC_ALARM);
  return alarm?.periodInMinutes ?? DEFAULT_INTERVAL_MINUTES;
}

/**
 * Runs one sync pass from an alarm or a user action.
 *
 * @param trigger Why the run started.
 * @param mode Sync mode; defaults to a routine incremental run.
 * @returns The run outcome, or `null` when the extension is not paired.
 */
export async function syncNow(
  trigger: SyncTrigger,
  mode: SyncMode = "routine",
): Promise<SyncRunOutcome | null> {
  const client = await createClient();
  if (client === null) return null;

  const interval = await appliedIntervalMinutes();

  return runSync(
    { client, fetchPage: fetchNotebookPage },
    { mode, trigger, idempotencyKey: idempotencyKey(trigger, interval) },
  );
}

/**
 * Reports liveness, applies the stored frequency and executes a pending command.
 *
 * @returns A task that completes when the heartbeat has been processed.
 */
export async function heartbeat(): Promise<void> {
  const client = await createClient();
  if (client === null) return;

  const applied = await appliedIntervalMinutes();
  const alarm = await chrome.alarms.get(SYNC_ALARM);
  const nextSyncDueAt = alarm === undefined ? null : new Date(alarm.scheduledTime).toISOString();

  const response = await client.heartbeat({
    profileVersion: PROFILE_VERSION,
    appliedIntervalMinutes: applied,
    nextSyncDueAt,
  });

  if (response.intervalMinutes !== applied) await applyInterval(response.intervalMinutes);

  if (response.command === "sync") await syncNow("manual");
  else if (response.command === "full_resync") await syncNow("manual", "full_resync");
}

/**
 * Registers the alarm listener and creates the alarms.
 *
 * @returns A task that completes once the worker is listening.
 */
export async function start(): Promise<void> {
  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === SYNC_ALARM) void syncNow("scheduled");
    else if (alarm.name === HEARTBEAT_ALARM) void heartbeat();
  });

  await ensureAlarms();
}

/**
 * Builds the replay key for a run.
 *
 * A scheduled run is keyed on its alarm period, so a firing retried after a worker restart replays
 * the same server-side job instead of importing twice; manual runs are always distinct.
 */
function idempotencyKey(trigger: SyncTrigger, intervalMinutes: number): string {
  if (trigger !== "scheduled") return `${trigger}-${crypto.randomUUID()}`;

  const bucket = Math.floor(Date.now() / (intervalMinutes * 60_000));
  return `${PROVIDER_ID}-scheduled-${bucket}`;
}

/** Fetches a notebook page inside the user's own Amazon session. */
async function fetchNotebookPage(url: string): Promise<PageResponse> {
  const response = await fetch(url, { credentials: "include", redirect: "follow" });

  if (!response.ok) {
    throw new Error(`Notebook page responded with ${response.status}.`);
  }

  return { url: response.url, html: await response.text() };
}

if (typeof chrome !== "undefined" && chrome.alarms !== undefined) {
  chrome.runtime.onInstalled.addListener(() => void start());
  chrome.runtime.onStartup.addListener(() => void start());
  void start();
}
