/**
 * The popup: which Amazon notebook Relego may read, and nothing else.
 *
 * The popup is a view controller. It renders the allowlist, reads the two fields, and hands the
 * choice to `region-change.ts`, which owns the order of operations — ask for host access, tell the
 * server, release the host that is no longer read — so a refusal changes nothing. The region the
 * connection reads is always read back from the server, which stays the single source of truth.
 */

import { PROVIDER_ID, RelegoClient } from "./client";
import type { RelegoClientConfig } from "./client";
import { CONFIGURED_MESSAGE } from "./messages";
import { browserHostPermissions } from "./permissions";
import { REGION_DISCLOSURE, resolveRegion } from "./profile";
import { chooseRegion, regionOptions } from "./region-change";
import type { RegionChoice } from "./region-change";
import { readConfig, writeConfig } from "./storage";

/** Wording for the statuses that ask the user to do something, mirrored from the web app. */
const ATTENTION: Record<string, string> = {
  awaiting_pairing: "Waiting for Relego to confirm the connection.",
  auth_expired: "Amazon sign-in needed. Sign in to Kindle in this browser, then save again.",
  failed: "The last sync did not finish.",
  browser_not_reporting: "Relego has not heard from this browser recently, so syncs are paused.",
  reminder_due: "It has been a while since your highlights were read.",
};

/** The elements the popup drives. */
interface Elements {
  readonly form: HTMLFormElement;
  readonly state: HTMLParagraphElement;
  readonly serverUrl: HTMLInputElement;
  readonly code: HTMLInputElement;
  readonly region: HTMLSelectElement;
  readonly disclosure: HTMLParagraphElement;
  readonly notice: HTMLParagraphElement;
  readonly save: HTMLButtonElement;
}

interface PopupState {
  config: RelegoClientConfig | null;
  currentRegionId: string | null;
}

/** Returns a required element, failing loudly when the markup and this file disagree. */
function required<T extends HTMLElement>(id: string): T {
  const found = document.getElementById(id);
  if (found === null) throw new Error(`popup.html is missing #${id}.`);

  return found as T;
}

/** Reads the HTTP status out of a rejected server call, when it has one. */
function statusOf(error: unknown): number | null {
  const status = (error as { readonly status?: unknown } | null)?.status;

  return typeof status === "number" ? status : null;
}

/** Shows a sentence in the notice block. */
function say(elements: Elements, text: string, tone: "info" | "warn"): void {
  elements.notice.textContent = text;
  elements.notice.dataset["tone"] = tone;
}

/** Fills the picker from the allowlist; there is no free-text host field anywhere. */
function renderPicker(elements: Elements): void {
  elements.region.replaceChildren(
    ...regionOptions().map((option) => {
      const row = document.createElement("option");
      row.value = option.value;
      row.textContent = option.label;

      return row;
    }),
  );
  elements.disclosure.textContent = REGION_DISCLOSURE;
}

/**
 * Reads the connection state from the server and shows it.
 *
 * @param elements The popup's elements.
 * @param state Cached configuration and region used during the next Save gesture.
 */
async function refresh(elements: Elements, state: PopupState): Promise<void> {
  const config = await readConfig();
  state.config = config;
  state.currentRegionId = null;
  if (config === null) {
    elements.state.textContent = "Not connected yet.";
    say(elements, "Paste the one-time code Relego shows to connect this browser.", "warn");

    return;
  }

  elements.serverUrl.value = config.baseUrl;

  try {
    const status = await new RelegoClient(config).getStatus(PROVIDER_ID);
    const region = resolveRegion(status.connection.region?.id ?? null);
    state.currentRegionId = region?.id ?? null;
    if (region !== null) elements.region.value = region.id;

    elements.state.textContent =
      region === null
        ? `Connected to ${config.baseUrl}. No region selected yet.`
        : `Reading ${region.displayName} · ${region.notebookHost}`;

    const attention = ATTENTION[status.status];
    if (attention === undefined) elements.notice.textContent = "";
    else say(elements, attention, "warn");
  } catch {
    elements.state.textContent = `Relego at ${config.baseUrl} did not answer.`;
    say(elements, "Check that the server is running and that the address is right.", "warn");
  }
}

/**
 * Applies what the user saved, keeping the button disabled while it runs.
 *
 * @param elements The popup's elements.
 * @param state Configuration loaded before the Save gesture.
 * @returns A task that completes once the outcome is on screen.
 */
async function save(elements: Elements, state: PopupState): Promise<void> {
  if (elements.save.disabled) return;
  elements.save.disabled = true;
  try {
    await applySave(elements, state);
  } catch (error) {
    say(elements, describeFailure(statusOf(error)), "warn");
  } finally {
    elements.save.disabled = false;
  }
}

/** Resolves the target server, then hands the choice to the region decision. */
async function applySave(elements: Elements, state: PopupState): Promise<void> {
  const config = state.config;
  const serverUrl = elements.serverUrl.value.trim();
  const code = elements.code.value.trim();
  const needsCode =
    config === null || config.baseUrl !== serverUrl || (code.length > 0 && code !== config.token);

  if (needsCode && code.length === 0) {
    say(elements, "Paste the one-time code from Relego to connect to this server.", "warn");

    return;
  }

  const target: RelegoClientConfig = needsCode ? { baseUrl: serverUrl, token: code } : config;
  const client = new RelegoClient(target);
  const currentRegionId = needsCode ? null : state.currentRegionId;

  const outcome = await chooseRegion(
    {
      regionId: elements.region.value,
      currentRegionId,
      serverBaseUrl: needsCode ? target.baseUrl : null,
    },
    {
      permissions: browserHostPermissions,
      pair: async (regionId) => {
        const pairing = await client.pair(regionId);
        await writeConfig(target);

        return pairing.intervalMinutes;
      },
      setRegion: async (regionId) => (await client.setRegion(regionId)).pendingCommand,
    },
  );

  await applyOutcome(elements, state, outcome, needsCode);
}

/** Tells the worker to recreate its alarms, then shows what happened. */
async function applyOutcome(
  elements: Elements,
  state: PopupState,
  outcome: RegionChoice,
  needsCode: boolean,
): Promise<void> {
  switch (outcome.kind) {
    case "region_selected": {
      elements.code.value = "";
      const notified = await notifyWorker(outcome.intervalMinutes, needsCode);
      await refresh(elements, state);
      if (!notified) {
        say(elements, "Saved, but the background sync could not be started. Reload the extension, then sync from Relego.", "warn");
        break;
      }
      say(
        elements,
        outcome.intervalMinutes === null
          ? `Now reading ${outcome.region.displayName} · ${outcome.region.notebookHost}. A full re-read starts now.`
          : `Connected. Relego reads your ${outcome.region.displayName} notebook from now on.`,
        "info",
      );
      break;
    }
    case "already_selected":
      say(elements, `Already reading ${outcome.region.displayName}. Nothing changed.`, "info");
      break;
    case "access_denied":
      say(
        elements,
        `Relego needs access to ${outcome.region.notebookHost} to read your notebook. Nothing changed.`,
        "warn",
      );
      break;
    case "unsupported_region":
      say(elements, "Only the regions listed here can be read. Nothing changed.", "warn");
      break;
    case "invalid_server_url":
      say(elements, "Enter the full address, for example http://localhost:8080.", "warn");
      break;
    case "failed":
      say(elements, describeFailure(outcome.status), "warn");
      break;
  }
}

/**
 * Asks the worker to recreate its alarms and pick up any command the change queued.
 *
 * @param intervalMinutes Frequency the server reported, or `null` when none was learned.
 * @param paired Whether a new pairing needs its first sync.
 */
async function notifyWorker(intervalMinutes: number | null, paired: boolean): Promise<boolean> {
  try {
    await chrome.runtime.sendMessage({
      type: CONFIGURED_MESSAGE,
      reason: paired ? "paired" : "region_changed",
      intervalMinutes: intervalMinutes ?? undefined,
    });
    return true;
  } catch {
    return false;
  }
}

/** Turns a failed server call into something the user can act on. */
function describeFailure(status: number | null): string {
  if (status === 401) {
    return "Relego did not accept that one-time code. Generate a new one, then save again.";
  }

  if (status === 422) {
    return "Relego does not accept that region. Update the extension, then try again.";
  }

  if (status === null) return "Relego could not be reached. Check that the server is running.";

  return `Relego answered with ${status}. Nothing changed.`;
}

/** Wires the popup to the document. */
async function main(): Promise<void> {
  const elements: Elements = {
    form: required<HTMLFormElement>("settings"),
    state: required<HTMLParagraphElement>("state"),
    serverUrl: required<HTMLInputElement>("serverUrl"),
    code: required<HTMLInputElement>("code"),
    region: required<HTMLSelectElement>("region"),
    disclosure: required<HTMLParagraphElement>("disclosure"),
    notice: required<HTMLParagraphElement>("notice"),
    save: required<HTMLButtonElement>("save"),
  };

  const state: PopupState = { config: null, currentRegionId: null };
  elements.save.disabled = true;
  renderPicker(elements);
  await refresh(elements, state);
  elements.save.disabled = false;
  elements.form.addEventListener("submit", (event) => {
    event.preventDefault();
    void save(elements, state);
  });
}

if (typeof document !== "undefined") void main();
