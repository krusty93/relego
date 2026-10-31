/**
 * The popup: which Amazon notebook Relego may read, and nothing else.
 *
 * The popup is a view controller. It renders the allowlist, reads the two fields, and hands the
 * choice to `region-change.ts`, which owns the order of operations — ask for host access, tell the
 * server, release the host that is no longer read — so a refusal changes nothing. The region the
 * connection reads is always read back from the server, which stays the single source of truth.
 *
 * It has two views and shows exactly one of them. A connection that answers shows the confirmation
 * and a way into the form; the form appears only while something has to be connected, or when the
 * user asks to edit it. Saving successfully returns to the confirmation. Both views also offer a
 * button that opens the Kindle Notebook Sync page of the Relego server in a new tab: the form's
 * button follows the address being typed, the confirmation's uses the paired server.
 */

import { PROVIDER_ID, RelegoClient, statusRegion } from "./client";
import type { RelegoClientConfig } from "./client";
import { CONFIGURED_MESSAGE } from "./messages";
import { browserHostPermissions } from "./permissions";
import { isWindowed, windowedUrl, WINDOW_HEIGHT, WINDOW_WIDTH } from "./popout";
import { REGION_DISCLOSURE, resolveRegion } from "./profile";
import { chooseRegion, regionOptions } from "./region-change";
import type { RegionChoice } from "./region-change";
import { kindleSyncUrl } from "./server-link";
import {
  readConfig,
  readDraftRegion,
  readDraftServerUrl,
  writeConfig,
  writeDraftRegion,
  writeDraftServerUrl,
} from "./storage";

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
  readonly done: HTMLElement;
  readonly edit: HTMLButtonElement;
  readonly openDone: HTMLButtonElement;
  readonly openForm: HTMLButtonElement;
  readonly state: HTMLParagraphElement;
  readonly popout: HTMLButtonElement;
  readonly serverUrl: HTMLInputElement;
  readonly code: HTMLInputElement;
  readonly region: HTMLSelectElement;
  readonly disclosure: HTMLParagraphElement;
  readonly notice: HTMLParagraphElement;
  readonly save: HTMLButtonElement;
  readonly cancel: HTMLButtonElement;
}

/** Which of the two views the popup is showing. */
type PopupView = "form" | "done";

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

/** Shows a sentence in the notice block, or clears it when there is nothing to say. */
function say(elements: Elements, text: string, tone: "info" | "warn"): void {
  if (text.length === 0) {
    clearNotice(elements);

    return;
  }

  elements.notice.textContent = text;
  elements.notice.dataset["tone"] = tone;
}

/**
 * Empties the notice block.
 *
 * The tone attribute is what makes the block visible, so clearing only the text would leave an
 * empty box on screen.
 */
function clearNotice(elements: Elements): void {
  elements.notice.textContent = "";
  delete elements.notice.dataset["tone"];
}

/**
 * Shows one view and hides the other.
 *
 * Cancel is offered only from a configuration that already works, because there is nothing to go
 * back to before the first successful save.
 */
function show(elements: Elements, view: PopupView, paired: boolean): void {
  elements.done.hidden = view !== "done";
  elements.form.hidden = view !== "form";
  elements.cancel.hidden = !paired || view !== "form";

  if (view === "done") clearNotice(elements);
  updateOpenForm(elements);
}

/**
 * Enables the form's link to Relego only while the address typed in the form can be opened.
 *
 * The address field is also filled in by code, not just by typing, so this runs on every input and
 * whenever a view is shown.
 */
function updateOpenForm(elements: Elements): void {
  elements.openForm.disabled = kindleSyncUrl(elements.serverUrl.value) === null;
}

/**
 * Opens the Kindle Notebook Sync page of a Relego server in a new tab.
 *
 * Opening a tab needs no permission. The toolbar popup closes when the new tab takes focus; the
 * detached window stays open, which is what lets the one-time code be carried back into it.
 *
 * @param elements The popup's elements, used to report a tab the browser refused to open.
 * @param serverUrl Server address to open; nothing happens when it is not an http(s) URL.
 */
async function openRelego(elements: Elements, serverUrl: string): Promise<void> {
  const url = kindleSyncUrl(serverUrl);
  if (url === null) return;

  try {
    await chrome.tabs.create({ url });
  } catch {
    say(elements, "The browser did not open a new tab.", "warn");
  }
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
 * @returns Whether the server reported a region, which is what makes the configuration usable.
 */
async function refresh(elements: Elements): Promise<boolean> {
  const config = await readConfig();
  if (config === null) {
    elements.state.textContent = "Not connected yet.";
    say(elements, "Paste the one-time code Relego shows to connect this browser.", "warn");
    await restoreDraft(elements);

    return false;
  }

  elements.serverUrl.value = config.baseUrl;

  try {
    const status = await new RelegoClient(config).getStatus(PROVIDER_ID);
    const reported = statusRegion(status);
    const region = reported === null ? null : resolveRegion(reported.id);
    if (region !== null) elements.region.value = region.id;

    elements.state.textContent =
      region === null
        ? `Connected to ${config.baseUrl}. No region selected yet.`
        : `Reading ${region.displayName} · ${region.notebookHost}`;

    const attention = ATTENTION[status.status];
    if (attention === undefined) clearNotice(elements);
    else say(elements, attention, "warn");

    return region !== null;
  } catch {
    elements.state.textContent = `Relego at ${config.baseUrl} did not answer.`;
    say(elements, "Check that the server is running and that the address is right.", "warn");

    return false;
  }
}

/**
 * Restores the fields the user filled in before pairing.
 *
 * The popup is torn down every time it loses focus, so anything typed before a pairing exists has to
 * come back from storage — otherwise carrying the one-time code over from Relego would cost the
 * address that was already entered.
 *
 * @param elements The popup's elements.
 */
async function restoreDraft(elements: Elements): Promise<void> {
  const [serverUrl, regionId] = await Promise.all([readDraftServerUrl(), readDraftRegion()]);

  if (serverUrl.length > 0) elements.serverUrl.value = serverUrl;
  if (regionId !== null && resolveRegion(regionId) !== null) elements.region.value = regionId;
}

/** Keeps what the user types, so a popup that closes and reopens comes back filled in. */
function keepDraft(elements: Elements): void {
  elements.serverUrl.addEventListener("input", () => {
    void writeDraftServerUrl(elements.serverUrl.value);
    updateOpenForm(elements);
  });
  elements.region.addEventListener("change", () => {
    void writeDraftRegion(elements.region.value);
  });
}

/**
 * Reopens the popup in a window of its own, then closes the toolbar popup.
 *
 * @param elements The popup's elements, used to report a window the browser refused to open.
 */
function popOut(elements: Elements): void {
  chrome.windows.create(
    {
      url: windowedUrl(chrome.runtime.getURL("popup.html")),
      type: "popup",
      width: WINDOW_WIDTH,
      height: WINDOW_HEIGHT,
    },
    () => {
      if (chrome.runtime.lastError === undefined) window.close();
      else say(elements, "The browser did not open a window. Check that popups are allowed.", "warn");
    },
  );
}

/**
 * Applies what the user saved, keeping the button disabled while it runs.
 *
 * The notice is written after the server state has been read back, so a message about this save is
 * never replaced by the state the page already knew.
 *
 * @param elements The popup's elements.
 * @returns A task that completes once the outcome is on screen.
 */
async function save(elements: Elements): Promise<void> {
  elements.save.disabled = true;
  try {
    const outcome = await applySave(elements);
    if (outcome === null) return;

    const paired = await refresh(elements);
    const applied = outcome.kind === "region_selected" || outcome.kind === "already_selected";

    if (applied) elements.code.value = "";

    // A working configuration moves to the confirmation, which says everything that is left to say;
    // anything else keeps the form on screen with the reason next to it.
    if (applied && paired) show(elements, "done", true);
    else {
      show(elements, "form", paired);
      announceOutcome(elements, outcome);
    }
  } catch (error) {
    say(elements, describeFailure(statusOf(error)), "warn");
  } finally {
    elements.save.disabled = false;
  }
}

/**
 * Resolves the target server, then hands the choice to the region decision.
 *
 * @param elements The popup's elements.
 * @returns What the region decision made of the choice, or `null` when the form itself was
 * incomplete and the server was never called.
 */
async function applySave(elements: Elements): Promise<RegionChoice | null> {
  const config = await readConfig();
  const serverUrl = elements.serverUrl.value.trim();
  const code = elements.code.value.trim();
  const needsCode = config === null || config.baseUrl !== serverUrl;

  if (needsCode && code.length === 0) {
    say(elements, "Paste the one-time code from Relego to connect to this server.", "warn");

    return null;
  }

  const target: RelegoClientConfig = needsCode ? { baseUrl: serverUrl, token: code } : config;
  const client = new RelegoClient(target);
  const currentRegionId = needsCode ? null : await readCurrentRegionId(client);

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

  await applyOutcome(outcome, target, needsCode);

  return outcome;
}

/** Reads the region the server has for this connection; `null` when it has none yet. */
async function readCurrentRegionId(client: RelegoClient): Promise<string | null> {
  const reported = statusRegion(await client.getStatus(PROVIDER_ID));

  return reported === null ? null : reported.id;
}

/** Tells the worker to recreate its alarms and persists the configuration the choice needs. */
async function applyOutcome(
  outcome: RegionChoice,
  target: RelegoClientConfig,
  needsCode: boolean,
): Promise<void> {
  if (outcome.kind === "region_selected") await notifyWorker(outcome.intervalMinutes);
  if (needsCode) await writeConfig(target);
}

/** Shows what happened to the user's choice. */
function announceOutcome(elements: Elements, outcome: RegionChoice): void {
  switch (outcome.kind) {
    case "region_selected":
      say(
        elements,
        outcome.intervalMinutes === null
          ? `Now reading ${outcome.region.displayName} · ${outcome.region.notebookHost}. A full re-read starts now.`
          : `Connected. Relego reads your ${outcome.region.displayName} notebook from now on.`,
        "info",
      );
      break;
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
 */
async function notifyWorker(intervalMinutes: number | null): Promise<void> {
  try {
    await chrome.runtime.sendMessage({
      type: CONFIGURED_MESSAGE,
      intervalMinutes: intervalMinutes ?? undefined,
    });
  } catch {
    // The worker recreates its alarms when it next starts, so a missed message only delays a run.
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
    done: required<HTMLElement>("done"),
    edit: required<HTMLButtonElement>("edit"),
    openDone: required<HTMLButtonElement>("openDone"),
    openForm: required<HTMLButtonElement>("openForm"),
    state: required<HTMLParagraphElement>("state"),
    popout: required<HTMLButtonElement>("popout"),
    serverUrl: required<HTMLInputElement>("serverUrl"),
    code: required<HTMLInputElement>("code"),
    region: required<HTMLSelectElement>("region"),
    disclosure: required<HTMLParagraphElement>("disclosure"),
    notice: required<HTMLParagraphElement>("notice"),
    save: required<HTMLButtonElement>("save"),
    cancel: required<HTMLButtonElement>("cancel"),
  };

  renderPicker(elements);
  keepDraft(elements);
  elements.form.addEventListener("submit", (event) => {
    event.preventDefault();
    void save(elements);
  });

  elements.edit.addEventListener("click", () => {
    clearNotice(elements);
    show(elements, "form", true);
  });
  elements.cancel.addEventListener("click", () => {
    void openConfigured(elements);
  });
  elements.openForm.addEventListener("click", () => {
    void openRelego(elements, elements.serverUrl.value);
  });
  elements.openDone.addEventListener("click", () => {
    void openStoredRelego(elements);
  });

  if (isWindowed(location.search)) {
    document.body.dataset["window"] = "";
    elements.popout.hidden = true;
  } else {
    elements.popout.addEventListener("click", () => popOut(elements));
  }

  await openConfigured(elements);
}

/** Opens the page of the server this browser is paired with, the one the confirmation speaks of. */
async function openStoredRelego(elements: Elements): Promise<void> {
  const config = await readConfig();
  if (config !== null) await openRelego(elements, config.baseUrl);
}

/**
 * Reads the connection back and shows the view that matches it.
 *
 * @param elements The popup's elements.
 * @returns A task that completes once the view is on screen.
 */
async function openConfigured(elements: Elements): Promise<void> {
  const paired = await refresh(elements);
  show(elements, paired ? "done" : "form", paired);
}

if (typeof document !== "undefined") void main();
