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
  notebookUrl,
  resolveRegion,
} from "./profile";
import type { KindleRegion } from "./profile";
import { readConfig } from "./storage";
import { runSync } from "./sync-runner";
import type { PageResponse, SyncMode, SyncRunOutcome, SyncTrigger } from "./sync-runner";

/** Name of the routine sync alarm. */
export const SYNC_ALARM = "kindle-sync";

/** Name of the liveness alarm. */
export const HEARTBEAT_ALARM = "kindle-heartbeat";

/** Period of the liveness alarm, in minutes. */
export const HEARTBEAT_MINUTES = HEARTBEAT_SECONDS / 60;

/** Name of the alarm that keeps retrying while the user completes Amazon sign-in. */
export const AUTH_RETRY_ALARM = "kindle-auth-retry";

/**
 * Minutes between retries while Amazon sign-in is pending.
 *
 * A run that hits the sign-in page is not a dead end: the user signs in on Amazon's own page and
 * the next retry picks the sync up, so the wait is short instead of the whole sync frequency.
 */
export const AUTH_RETRY_MINUTES = 5;

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

  const outcome = await runSync(
    { client, fetchPage: fetchNotebookPage },
    { mode, trigger, idempotencyKey: idempotencyKey(trigger, interval) },
  );

  await reactToOutcome(client, outcome);
  return outcome;
}

/**
 * Reacts to a run outcome that needs the user.
 *
 * Only `auth_expired` needs the user: the extension opens the region's Kindle notebook so they can
 * sign in on Amazon's own page — it never fills credentials — and arms the short retry alarm so
 * the sync resumes by itself once they are signed in. Any other outcome ends the waiting period.
 *
 * @param client Client for the run, used to resolve the region that needs signing in.
 * @param outcome Outcome of the run that just finished.
 * @returns A task that completes once the reaction has been applied.
 */
async function reactToOutcome(client: RelegoClient, outcome: SyncRunOutcome): Promise<void> {
  if (outcome.kind === "completed") {
    await clearAuthRetry();
    return;
  }

  if (outcome.kind !== "failed") return;

  if (outcome.code !== "auth_expired") {
    await clearAuthRetry();
    return;
  }

  const alreadyWaiting = (await chrome.alarms.get(AUTH_RETRY_ALARM)) !== undefined;

  if (!alreadyWaiting) {
    const status = await client.getStatus(PROVIDER_ID);
    const region = resolveRegion(status.region?.id ?? null);

    if (region !== null) await openNotebookForSignIn(region);
  }

  await scheduleAuthRetry();
}

/**
 * Opens the region's Kindle notebook in a tab so the user can sign in on Amazon.
 *
 * Creating a tab needs no extra permission — the extension never reads tab URLs or titles — and the
 * page stays on Amazon's own origin, where only the user types their credentials.
 *
 * @param region Region whose notebook host needs a signed-in session.
 * @returns A task that completes once the tab has been opened.
 */
export async function openNotebookForSignIn(region: KindleRegion): Promise<void> {
  await chrome.tabs.create({ url: notebookUrl(region), active: true });
}

/** Arms the short retry alarm that keeps a run moving while sign-in is pending. */
export async function scheduleAuthRetry(): Promise<void> {
  const existing = await chrome.alarms.get(AUTH_RETRY_ALARM);

  if (existing === undefined) {
    await chrome.alarms.create(AUTH_RETRY_ALARM, {
      delayInMinutes: AUTH_RETRY_MINUTES,
      periodInMinutes: AUTH_RETRY_MINUTES,
    });
  }
}

/** Disarms the retry alarm once a run succeeds or fails for a reason the user cannot fix by signing in. */
export async function clearAuthRetry(): Promise<void> {
  await chrome.alarms.clear(AUTH_RETRY_ALARM);
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
    else if (alarm.name === AUTH_RETRY_ALARM) void syncNow("retry");
  });

  await ensureAlarms();
}

/**
 * Builds the replay key for a run.
 *
 * A scheduled run is keyed on its alarm period and a sign-in retry on the retry period, so a firing
 * replayed after a worker restart replays the same server-side job instead of importing twice;
 * user-requested runs are always distinct.
 */
function idempotencyKey(trigger: SyncTrigger, intervalMinutes: number): string {
  if (trigger === "retry") return `${PROVIDER_ID}-retry-${bucket(AUTH_RETRY_MINUTES)}`;
  if (trigger !== "scheduled") return `${trigger}-${crypto.randomUUID()}`;

  return `${PROVIDER_ID}-scheduled-${bucket(intervalMinutes)}`;
}

/** Buckets the current time by an alarm period, so one firing maps to one replay key. */
function bucket(minutes: number): number {
  return Math.floor(Date.now() / (minutes * 60_000));
}

/** Fetches a notebook page inside the user's own Amazon session. */
export async function fetchNotebookPage(url: string, signal?: AbortSignal): Promise<PageResponse> {
  const controller = new AbortController();
  const abort = (): void => controller.abort(signal?.reason);
  if (signal?.aborted === true) abort();
  else signal?.addEventListener("abort", abort, { once: true });
  const timeout = setTimeout(() => controller.abort(new DOMException("Notebook request timed out.", "TimeoutError")), 60_000);

  try {
    const response = await fetch(url, { credentials: "include", redirect: "follow", signal: controller.signal });
    if (!response.ok) {
      throw new Error(`Notebook page responded with ${response.status}.`);
    }

    return { url: response.url, html: await response.text() };
  } finally {
    clearTimeout(timeout);
    signal?.removeEventListener("abort", abort);
  }
}

if (typeof chrome !== "undefined" && chrome.alarms !== undefined) {
  chrome.runtime.onInstalled.addListener(() => void start());
  chrome.runtime.onStartup.addListener(() => void start());
  void start();
}
