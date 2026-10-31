import { expect, test, type Page } from "@playwright/test";

const PROVIDER = {
  providerId: "kindle-cloud",
  displayName: "Kindle cloud",
  capabilities: { pushIngest: true, fullResync: true, progressReporting: true },
  inactivityReminderDays: 45,
  disclosureVersion: "1",
};

const PREREQUISITES = [
  "Relego server running",
  "Chrome or Firefox with the Relego extension",
  "An active Amazon sign-in in that browser",
];

const DISCLOSURE_ITEMS = [
  {
    kind: "Coverage",
    title: "Cloud only",
    body: "Only highlights Amazon syncs to your Kindle Notebook are read.",
  },
  {
    kind: "Sideloaded",
    title: "Sideloaded books",
    body: "Books sent to your Kindle over USB or by email are not covered.",
  },
  {
    kind: "ExportLimit",
    title: "Publisher limits",
    body: "Some publishers cap how much of a book Amazon exports.",
  },
  {
    kind: "Prerequisite",
    title: "Kindle Notebook access",
    body: "Your Amazon account needs Kindle Notebook enabled.",
  },
  {
    kind: "Privacy",
    title: "Your data path",
    body: "Your browser reads the page and posts highlight text to your own server.",
  },
  {
    kind: "Availability",
    title: "Validated regions",
    body: "Only validated Amazon regions are supported.",
  },
];

type Payload = Record<string, unknown>;

// Mirrors the server's allowlist and disclosure; the page must state the limits, never offer a host.
const REGIONS = [
  { id: "us", displayName: "United States", host: "read.amazon.com" },
  { id: "ca", displayName: "Canada", host: "read.amazon.ca" },
  { id: "uk", displayName: "United Kingdom", host: "read.amazon.co.uk" },
  { id: "de", displayName: "Germany", host: "lesen.amazon.de" },
  { id: "fr", displayName: "France", host: "lire.amazon.fr" },
  { id: "it", displayName: "Italy", host: "leggi.amazon.it" },
  { id: "es", displayName: "Spain", host: "leer.amazon.es" },
  { id: "br", displayName: "Brazil", host: "ler.amazon.com.br" },
];

const REGION_DISCLOSURE =
  "Only these validated hosts are supported; other Amazon regions are unsupported until validated.";

// The ten choices of FR-025, in the order the page offers them.
const ALLOWED_INTERVALS = [15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440];

/** An open reminder for a quiet period that started 45 days before the stubbed "now". */
const REMINDER = {
  id: 7,
  quietSince: "2024-03-17T09:00:00Z",
  thresholdDays: 45,
  createdAt: "2024-05-01T03:00:00Z",
  dismissed: false,
};

const FREQUENCY_LABELS = [
  "15 minutes",
  "30 minutes",
  "45 minutes",
  "1 hour",
  "2 hours",
  "4 hours",
  "6 hours",
  "12 hours",
  "18 hours",
  "24 hours",
];

const NEXT_RUN_AT = "2024-05-01T16:05:00Z";

function statusResponse(status: string, overrides: Payload = {}): Payload {
  const connected = status !== "not_connected" && status !== "awaiting_pairing";

  return {
    providerId: "kindle-cloud",
    status,
    connection: {
      state: connected ? "active" : "pending_pairing",
      connectedAt: connected ? "2024-05-01T09:00:00Z" : null,
      lastHeartbeatAt: connected ? new Date(Date.now() - 20_000).toISOString() : null,
      browserReporting: connected,
      region: connected
        ? { id: "uk", displayName: "United Kingdom", host: "read.amazon.co.uk" }
        : null,
    },
    schedule: {
      intervalMinutes: 360,
      allowedIntervalMinutes: ALLOWED_INTERVALS,
      nextRunAt: connected ? NEXT_RUN_AT : null,
      applied: connected,
      pendingCommand: "none",
    },
    currentJob: null,
    lastCompletedJob: null,
    failure: null,
    reminder: null,
    disclosure: { version: "1", items: DISCLOSURE_ITEMS },
    prerequisites: PREREQUISITES,
    ...overrides,
  };
}

function job(overrides: Payload = {}): Payload {
  return {
    id: 41,
    mode: "routine",
    status: "running",
    phase: "reading",
    booksTotal: 120,
    booksDone: 37,
    highlightsSeen: 400,
    highlightsNew: 212,
    highlightsDuplicate: 188,
    truncatedBooks: 0,
    startedAt: new Date(Date.now() - 30_000).toISOString(),
    endedAt: null,
    ...overrides,
  };
}

/**
 * Records that the extension has reported applying `minutes`, as its next heartbeat would. The
 * page only learns the new period from a status read, never from the change request itself.
 */
function reportApplied(mock: SyncMock, minutes: number): void {
  mock.appliedInterval = minutes;
  mock.status = {
    ...mock.status,
    schedule: {
      ...(mock.status.schedule as Payload),
      intervalMinutes: minutes,
      applied: true,
      nextRunAt: NEXT_RUN_AT,
    },
  };
}

interface SyncMock {
  status: Payload;
  requests: string[];
  commands: string[];
  statusCalls: number;
  /** Dismissals the page has sent, in order. */
  dismissals: string[];
  /** Frequencies the page has sent, in order. */
  scheduleRequests: number[];
  /** The frequency the extension has reported applying; the server only echoes it back. */
  appliedInterval: number | null;
  /** A value the server refuses even though the page offered it, standing in for a server-side change. */
  rejectInterval: number | null;
}

/** Serves the sync surface from a stub so every state is reachable without the extension. */
async function mockSync(page: Page, initial: Payload): Promise<SyncMock> {
  const initialSchedule = initial.schedule as { intervalMinutes: number; applied: boolean };
  const mock: SyncMock = {
    status: initial,
    requests: [],
    commands: [],
    statusCalls: 0,
    dismissals: [],
    scheduleRequests: [],
    appliedInterval: initialSchedule.applied ? initialSchedule.intervalMinutes : null,
    rejectInterval: null,
  };

  await page.route("**/sync/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    mock.requests.push(`${request.method()} ${path}`);

    if (path === "/sync/providers") {
      await route.fulfill({ json: [PROVIDER] });
      return;
    }

    if (path.endsWith("/connection")) {
      if (request.method() === "DELETE") {
        await route.fulfill({ status: 204 });
        return;
      }

      // The server moves the connection to pending pairing as soon as it mints a token.
      mock.status = statusResponse("awaiting_pairing");
      await route.fulfill({
        status: 201,
        json: { providerId: "kindle-cloud", token: "K7QP-2M4X-9TDH" },
      });
      return;
    }

    // Commands are queued server-side and picked up on the extension's next heartbeat.
    if (path.endsWith("/commands")) {
      const body = request.postDataJSON() as { command?: string };
      mock.commands.push(body.command ?? "");
      // The server keeps the command until the extension's next heartbeat, so the status read that
      // follows the request reports it.
      mock.status = {
        ...mock.status,
        schedule: { ...(mock.status.schedule as Payload), pendingCommand: body.command ?? "none" },
      };
      await route.fulfill({
        status: 202,
        json: { providerId: "kindle-cloud", pendingCommand: body.command ?? "none" },
      });
      return;
    }

    if (path.endsWith("/regions")) {
      await route.fulfill({ json: { regions: REGIONS, disclosure: REGION_DISCLOSURE } });
      return;
    }

    // Dismissing answers the reminder for this quiet period only, so the next status read drops it.
    if (path.endsWith("/dismiss")) {
      mock.dismissals.push(`${request.method()} ${path}`);
      mock.status = { ...mock.status, reminder: null };
      await route.fulfill({ status: 204 });
      return;
    }

    // The server stores a frequency and the extension applies it on its next heartbeat, so the
    // reply reports `applied` only once the extension has reported the same period.
    if (path.endsWith("/schedule")) {
      const body = request.postDataJSON() as { intervalMinutes?: number };
      const requested = body.intervalMinutes ?? 0;
      mock.scheduleRequests.push(requested);

      if (!ALLOWED_INTERVALS.includes(requested) || requested === mock.rejectInterval) {
        await route.fulfill({
          status: 422,
          json: {
            title: "One or more validation errors occurred.",
            status: 422,
            errors: {
              intervalMinutes: [`intervalMinutes must be one of: ${ALLOWED_INTERVALS.join(", ")}.`],
            },
          },
        });
        return;
      }

      const applied = mock.appliedInterval === requested;
      const nextRunAt = applied ? NEXT_RUN_AT : null;

      mock.status = {
        ...mock.status,
        schedule: {
          ...(mock.status.schedule as Payload),
          intervalMinutes: requested,
          applied,
          nextRunAt,
        },
      };

      await route.fulfill({ json: { intervalMinutes: requested, applied, nextRunAt } });
      return;
    }

    mock.statusCalls += 1;
    await route.fulfill({ json: mock.status });
  });

  return mock;
}

function serverUrlField(page: Page) {
  return page.locator(".field", { hasText: "Server URL" }).locator("code");
}

function oneTimeCode(page: Page) {
  return page.locator(".field", { hasText: "One-time code" }).locator("code");
}

test.describe("sync", () => {
  test("the connect flow leads the page and the limits follow it", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("not_connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "How it works" })).toBeVisible();
    await expect(page.getByText("Cloud only")).toBeVisible();
    await expect(page.getByText("Publisher limits")).toBeVisible();
    await expect(page.getByRole("heading", { name: "What this needs" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Chrome Web Store" })).toHaveAttribute(
      "href", "https://chromewebstore.google.com/detail/relego/nlpcdgfkcoiiighmhemlpkjkcnoojdhp",
    );
    await expect(page.getByRole("link", { name: "Firefox Add-ons" })).toHaveAttribute(
      "href", "https://addons.mozilla.org/en-US/firefox/addon/relego/",
    );
    await expect(page.getByRole("link", { name: "GitHub releases" })).toHaveAttribute(
      "href", "https://github.com/krusty93/relego/releases",
    );
    await expect(page.getByRole("heading", { name: "Privacy", exact: true })).toHaveCount(0);
    await expect(page.getByText("Your data path")).toBeVisible();

    const needs = await page.getByRole("heading", { name: "What this needs" }).boundingBox();
    const coverage = await page.getByRole("heading", { name: "How it works" }).boundingBox();
    const button = await page.getByRole("button", { name: "Connect Kindle cloud" }).boundingBox();
    expect(button!.y).toBeLessThan(coverage!.y);
    expect(needs!.y).toBeLessThan(button!.y);

    await page.getByRole("button", { name: "Connect Kindle cloud" }).click();

    await expect(oneTimeCode(page)).toHaveText("K7QP-2M4X-9TDH");
    await expect(page.getByText("Choose your Amazon region in the extension")).toBeVisible();
    expect(mock.requests).toContain("POST /sync/kindle-cloud/connection");
  });

  for (const width of [1440, 393]) {
    test(`the sync information is stacked and spaced at ${width}px`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 950 });
      await mockSync(page, statusResponse("completed"));
      await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

      await expect(page.getByText("Sync is optional.", { exact: false })).toHaveCount(0);
      const manual = page.getByRole("link", { name: "Import a highlights file", exact: true });
      const needs = page.getByRole("region", { name: "What this needs" });
      const outcome = page.getByRole("heading", { name: "Last sync finished", exact: true });
      const manualBox = (await manual.boundingBox())!;
      const needsBox = (await needs.boundingBox())!;
      expect(needsBox.y - (manualBox.y + manualBox.height)).toBeGreaterThanOrEqual(24);
      expect(needsBox.y + needsBox.height).toBeLessThan((await outcome.boundingBox())!.y);

      const details = page.getByRole("region", { name: "How it works" });
      const panelBox = (await outcome.locator("..").locator("..").boundingBox())!;
      for (const text of [
        ...await needs.locator("p").all(),
        ...await details.locator("dd").all(),
      ]) {
        const box = (await text.boundingBox())!;
        expect(box.x).toBe(panelBox.x);
        expect(box.width).toBeCloseTo(panelBox.width, 0);
      }
      await expect(details.locator("dt")).toHaveCount(5);
      await expect(details.getByText("Your data path", { exact: true })).toBeVisible();
      for (const item of await details.locator(".tip").all()) {
        const title = (await item.locator("dt").boundingBox())!;
        const subtitle = (await item.locator("dd").boundingBox())!;
        expect(subtitle.y).toBeGreaterThanOrEqual(title.y + title.height);
        expect(subtitle.x).toBe(title.x);
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: testInfo.outputPath(`sync-${width}.png`), fullPage: true });
    });
  }

  test("the pairing step hands over the server address and one-time code", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("awaiting_pairing"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    // A reload keeps no copy of the code, so the page says so instead of faking one.
    await expect(serverUrlField(page)).toHaveText(new URL(page.url()).origin);
    await expect(page.getByText("The code is shown once")).toBeVisible();
    await expect(page.getByText("Install the Relego extension")).toBeVisible();

    await page.getByRole("button", { name: "Generate a new code" }).click();
    await expect(oneTimeCode(page)).toHaveText("K7QP-2M4X-9TDH");

    await page.getByRole("button", { name: "Cancel setup" }).click();
    await expect(page.getByText("Cancel setup? Nothing has been imported yet.")).toBeVisible();
    await page.getByRole("button", { name: "Yes, cancel" }).click();

    await expect
      .poll(() => mock.requests.includes("DELETE /sync/kindle-cloud/connection"))
      .toBe(true);
  });

  test("picks up a browser that pairs, without anyone refreshing the page", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("awaiting_pairing"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "Waiting for your browser" })).toBeVisible();

    // The extension pairs from its own popup; nothing on this page is clicked.
    mock.status = statusResponse("connected");

    await expect(page.getByRole("heading", { name: "Connected" })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("button", { name: "Check again" })).toHaveCount(0);
    expect(mock.statusCalls).toBeGreaterThan(1);
  });

  test("a running sync shows determinate progress and polls every two seconds", async ({
    page,
  }) => {
    const mock = await mockSync(
      page,
      statusResponse("syncing", { currentJob: job(), lastCompletedJob: job({ id: 40 }) }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    const bar = page.getByRole("progressbar", { name: "Sync progress" });
    await expect(bar).toHaveAttribute("aria-valuenow", "37");
    await expect(bar).toHaveAttribute("aria-valuemax", "120");
    await expect(bar).toHaveAttribute("aria-valuemin", "0");

    await expect(page.getByText("Reading highlights · 37 of 120 books")).toBeVisible();
    await expect(page.getByText(/212 new highlights so far/)).toBeVisible();

    // The extension reports progress as it goes; a stale page would look stalled.
    const before = mock.statusCalls;
    await page.waitForTimeout(2_600);
    expect(mock.statusCalls).toBeGreaterThan(before);
  });

  test("completion names the outcome instead of implying success", async ({ page }) => {
    await mockSync(
      page,
      statusResponse("completed", {
        lastCompletedJob: job({
          status: "completed_with_changes",
          phase: "done",
          booksDone: 120,
          highlightsNew: 12,
          highlightsDuplicate: 388,
          endedAt: "2024-05-01T10:07:00Z",
        }),
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByText("Imported 12 new highlights")).toBeVisible();
    await expect(page.getByText("400 highlights read · 388 duplicates skipped")).toBeVisible();
  });

  test("a sync with nothing new says so", async ({ page }) => {
    await mockSync(
      page,
      statusResponse("completed", {
        lastCompletedJob: job({
          status: "completed_no_changes",
          phase: "done",
          booksDone: 120,
          highlightsNew: 0,
          highlightsSeen: 400,
          endedAt: "2024-05-01T10:07:00Z",
        }),
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByText("Nothing new", { exact: false })).toBeVisible();
  });

  test("publisher truncation is disclosed rather than hidden", async ({ page }) => {
    await mockSync(
      page,
      statusResponse("completed", {
        lastCompletedJob: job({
          status: "completed_with_changes",
          phase: "done",
          booksDone: 120,
          highlightsNew: 4,
          truncatedBooks: 3,
          endedAt: "2024-05-01T10:07:00Z",
        }),
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByText("3 books capped by the publisher")).toBeVisible();
  });

  test("a failed sync reports the code, the plain-language detail and the repair", async ({
    page,
  }) => {
    await mockSync(
      page,
      statusResponse("failed", {
        failure: {
          code: "provider_contract_changed",
          detail: "The Kindle Notebook page no longer matches what the extension reads.",
          recoverySteps: [
            "Update the Relego extension to the latest version.",
            "Run a sync again from the extension.",
          ],
          diagnostics: {
            profileVersion: "3",
            check: "bookRow",
            counts: { bookRows: 0, listElements: 4, keyedRows: 0, titleAnchors: 0 },
          },
          occurredAt: "2024-05-01T10:07:00Z",
        },
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "Last sync did not finish" })).toBeVisible();
    await expect(page.getByText("provider_contract_changed")).toBeVisible();
    await expect(page.getByText("no longer matches what the extension reads")).toBeVisible();
    await expect(page.getByRole("listitem").filter({ hasText: "Update the Relego extension" })).toBeVisible();
    await expect(page.getByRole("listitem").filter({ hasText: "Run a sync again" })).toBeVisible();

    // The recovery step tells the user to report the diagnostics, so the page has to show them.
    const diagnostics = page.getByTestId("sync-failure-diagnostics");
    await expect(diagnostics).toBeVisible();
    await expect(diagnostics.getByText(/"check":"bookRow"/)).toBeVisible();
    await expect(diagnostics.getByText(/listElements 4/)).toBeVisible();
  });

  test("every attention state is named, never left blank", async ({ page }) => {
    const expected: Array<[string, string]> = [
      ["auth_expired", "Amazon sign-in needed"],
      ["browser_not_reporting", "Your browser has not checked in"],
      ["reminder_due", "No recent sync"],
      ["connected", "Connected"],
    ];

    for (const [status, heading] of expected) {
      await mockSync(page, statusResponse(status));
      await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });
      await expect(page.getByRole("heading", { name: heading })).toBeVisible();
    }
  });

  test("a quiet period is named with both ways out and a control that clears it", async ({
    page,
  }) => {
    const mock = await mockSync(page, statusResponse("reminder_due", { reminder: REMINDER }));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    // The date is asserted through the machine-readable attribute so the spec does not depend on
    // the browser's locale.
    const reminder = page.getByTestId("sync-reminder");
    await expect(page.getByText("Nothing new in 45 days")).toBeVisible();
    await expect(page.locator("time[datetime='2024-03-17T09:00:00Z']")).toBeVisible();
    await expect(reminder.getByRole("link", { name: "import a highlights file" })).toHaveAttribute(
      "href",
      "/app/import",
    );

    await reminder.getByRole("button", { name: "Dismiss" }).click();

    await expect
      .poll(() => mock.dismissals)
      .toEqual(["POST /sync/kindle-cloud/reminders/7/dismiss"]);
    // The banner is the reminder, so it has to be gone on the next read rather than re-rendered.
    await expect(page.getByTestId("sync-reminder")).toHaveCount(0);
    await expect(
      page.getByText("Reminder dismissed. It comes back only if nothing new arrives again."),
    ).toBeVisible();
  });

  test("a sign-in problem outranks the quiet-period reminder instead of stacking on it", async ({
    page,
  }) => {
    await mockSync(
      page,
      statusResponse("auth_expired", {
        reminder: REMINDER,
        failure: {
          code: "auth_expired",
          detail: "Amazon signed this browser out, so the notebook could not be read.",
          recoverySteps: ["Open your Kindle Notebook in this browser and sign in to Amazon."],
          occurredAt: "2024-05-01T10:07:00Z",
        },
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.locator(".callout")).toHaveCount(1);
    await expect(page.getByText("Amazon asked for a sign-in")).toBeVisible();
    await expect(page.getByTestId("sync-reminder")).toHaveCount(0);
  });

  test("disconnecting asks first and promises the highlights stay", async ({ page }) => {
  const mock = await mockSync(page, statusResponse("connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await page.getByRole("button", { name: "Disconnect" }).click();
    await expect(page.getByText("Highlights already imported stay in your library.")).toBeVisible();
    await expect(page.getByRole("button", { name: "Keep connected" })).toBeVisible();

    await page.getByRole("button", { name: "Yes, disconnect" }).click();
    await expect
      .poll(() => mock.requests.includes("DELETE /sync/kindle-cloud/connection"))
      .toBe(true);
  });

  test("an expired Amazon session is explained, then retried in the paired region", async ({
    page,
  }) => {
    const mock = await mockSync(
      page,
      statusResponse("auth_expired", {
        failure: {
          code: "auth_expired",
          detail: "Amazon signed this browser out, so the notebook could not be read.",
          recoverySteps: [
            "Open your Kindle Notebook in this browser and sign in to Amazon.",
            "Relego retries on its next check-in; nothing already imported is affected.",
          ],
          occurredAt: "2024-05-01T10:07:00Z",
        },
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    // The sign-in banner replaces the generic failure report instead of stacking on top of it.
    await expect(page.locator(".callout")).toHaveCount(1);
    await expect(page.getByText("Amazon asked for a sign-in")).toBeVisible();
    await expect(page.getByRole("listitem").filter({ hasText: "sign in to Amazon" })).toBeVisible();

    // The way out is Amazon, on the region this connection was paired with.
    await expect(page.getByRole("link", { name: /Open your Kindle Notebook/ })).toHaveAttribute(
      "href",
      "https://read.amazon.co.uk/notebook",
    );
    await expect(page.getByText("Relego never sees your Amazon password.")).toBeVisible();

    await page.getByRole("button", { name: "Retry sync" }).click();

    await expect.poll(() => mock.commands).toEqual(["sync"]);
    await expect(
      page.getByText("Sync requested. Your browser starts on its next check-in."),
    ).toBeVisible();
  });

  test("a failed sync offers both repairs and the local fallback", async ({ page }) => {
    const mock = await mockSync(
      page,
      statusResponse("failed", {
        failure: {
          code: "provider_contract_changed",
          detail: "The Kindle Notebook page no longer matches what the extension reads.",
          recoverySteps: ["Update the Relego extension to the latest version."],
          occurredAt: "2024-05-01T10:07:00Z",
        },
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByRole("button", { name: "Sync now" })).toBeEnabled();
    await expect(page.getByRole("button", { name: "Full resync" })).toBeEnabled();
    await expect(page.getByRole("link", { name: "Import a file instead" })).toHaveAttribute(
      "href",
      "/app/import",
    );

    await page.getByRole("button", { name: "Full resync" }).click();

    await expect.poll(() => mock.commands).toEqual(["full_resync"]);
    await expect(
      page.getByText(
        "Full resync requested. Your browser re-reads every book on its next check-in.",
      ),
    ).toBeVisible();
  });

  test("commands wait for the sync that is already running", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("syncing", { currentJob: job() }));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByText("Commands run when this sync finishes.")).toBeVisible();
    await expect(page.getByRole("button", { name: "Sync now" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Full resync" })).toBeDisabled();
    expect(mock.commands).toEqual([]);
  });

  test("a requested sync is acknowledged, holds the buttons and then hands over to progress", async ({
    page,
  }) => {
    const mock = await mockSync(page, statusResponse("connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await page.getByRole("button", { name: "Sync now" }).click();

    // The command waits on the server for the browser's next check-in, and the page says so
    // instead of looking idle.
    const requested = page.getByTestId("sync-requested");
    await expect(requested).toBeVisible();
    await expect(requested).toContainText("Sync requested");
    await expect(requested).toContainText("at most a minute away");
    await expect(requested).toContainText("Last check-in");
    await expect(page.getByRole("button", { name: "Sync now" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Full resync" })).toBeDisabled();

    // The request lives on the server, so reloading the page does not lose sight of it.
    await page.reload({ waitUntil: "networkidle" });
    await expect(page.getByTestId("sync-requested")).toBeVisible();
    await expect(page.getByRole("button", { name: "Full resync" })).toBeDisabled();

    // The browser takes the command and the job starts: the request gives way to real progress.
    mock.status = statusResponse("syncing", { currentJob: job() });
    await expect(page.getByTestId("sync-requested")).toHaveCount(0, { timeout: 10_000 });
    await expect(page.getByRole("progressbar", { name: "Sync progress" })).toBeVisible();
    await expect(page.getByTestId("sync-timing")).toContainText("Running for");
    await expect(page.getByTestId("sync-timing")).toContainText("left");

    // Nothing else can be asked for while it runs.
    await expect(page.getByRole("button", { name: "Sync now" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Full resync" })).toBeDisabled();

    // And once it finishes, both buttons come back.
    mock.status = statusResponse("completed", {
      lastCompletedJob: job({ status: "completed_with_changes", phase: "done", booksDone: 120 }),
    });
    await expect(page.getByRole("button", { name: "Sync now" })).toBeEnabled({ timeout: 10_000 });
    await expect(page.getByRole("button", { name: "Full resync" })).toBeEnabled();
  });

  test("a queued request says when the browser has stopped checking in", async ({ page }) => {
    const connected = statusResponse("connected");
    await mockSync(
      page,
      statusResponse("connected", {
        connection: {
          ...(connected.connection as Payload),
          lastHeartbeatAt: new Date(Date.now() - 5 * 60_000).toISOString(),
        },
        schedule: { ...(connected.schedule as Payload), pendingCommand: "sync" },
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    const requested = page.getByTestId("sync-requested");
    await expect(requested).toContainText("has not checked in for 5 min");
    await expect(requested).toContainText("Open the browser");
    await expect(page.getByRole("button", { name: "Sync now" })).toBeDisabled();
  });

  test("the browser's last check-in is shown as an age next to the timestamp", async ({ page }) => {
    await mockSync(page, statusResponse("connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByTestId("sync-heartbeat")).toContainText("ago");
  });

  test("the navigation marks Sync when Amazon sign-in is needed", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("connected"));
    await page.goto("/app", { waitUntil: "networkidle" });

    const rail = page.locator('.nav-item[href="/app/kindle-sync"]');
    await expect(rail).toBeVisible();
    await expect(rail.locator(".attention")).toHaveCount(0);

    mock.status = statusResponse("auth_expired", {
      failure: {
        code: "auth_expired",
        detail: "Amazon signed this browser out, so the notebook could not be read.",
        recoverySteps: ["Sign in to Amazon again."],
        occurredAt: "2024-05-01T10:07:00Z",
      },
    });
    await page.reload({ waitUntil: "networkidle" });

    // Away from the Sync page the user still sees that something needs them.
    await expect(rail.locator(".attention")).toBeVisible();
    await expect(rail).toContainText("Amazon sign-in needed");
  });

  test("the region is shown read-only", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByTestId("sync-region")).toHaveText("United Kingdom (read.amazon.co.uk)");

    // Changing the region is the extension's job, and the page says where to do it.
    await expect(page.getByText("Your region is set in the Relego extension")).toBeVisible();

    // A region is never something the user types: the region row holds no control, and nothing on
    // the surface takes a host. The frequency selector is the only control here.
    await expect(page.getByTestId("sync-region").locator("input, select, textarea")).toHaveCount(0);
    await expect(page.locator('section[aria-labelledby="sync-h"] input')).toHaveCount(0);
    await expect(page.locator('section[aria-labelledby="sync-h"] textarea')).toHaveCount(0);
    await expect(page.locator('section[aria-labelledby="sync-h"] select')).toHaveCount(1);
    expect(mock.requests).not.toContain("PUT /sync/kindle-cloud/region");
    expect(mock.requests).not.toContain("POST /sync/kindle-cloud/region");
  });

  test("before a browser pairs, the region is named as the extension's choice", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("awaiting_pairing"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByTestId("pairing-region")).toContainText("Chosen in the extension");
    await expect(page.getByTestId("pairing-region")).toContainText(
      "you pick one from a list rather than typing a host",
    );
    expect(mock.requests).not.toContain("PUT /sync/kindle-cloud/region");
  });
});

test.describe("sync frequency", () => {
  test("offers the ten validated frequencies, in order", async ({ page }) => {
    await mockSync(page, statusResponse("connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    const select = page.getByTestId("sync-frequency");
    await expect(select).toHaveValue("360");

    // Every choice is named in words a person reads, not in minutes alone.
    await expect(select.locator("option")).toHaveText(FREQUENCY_LABELS);
  });

  test("says the server does not schedule, and names the browser's next run", async ({ page }) => {
    await mockSync(page, statusResponse("connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    const note = page.getByTestId("sync-next-run");
    await expect(note).toContainText("Relego's server does not schedule it");
    await expect(note).toContainText("Next sync");
    await expect(note).toContainText("as reported by your browser");
  });

  test("holds a new frequency as pending until the browser reports it", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    const select = page.getByTestId("sync-frequency");
    const note = page.getByTestId("sync-next-run");

    // The stored period was applied by the extension long ago, so it is shown as settled.
    await expect(note).toContainText("Next sync");

    await select.selectOption("120");

    expect(mock.scheduleRequests).toEqual([120]);
    expect(mock.requests).toContain("PUT /sync/kindle-cloud/schedule");
    await expect(select).toHaveValue("120");
    await expect(note).toContainText("Applies when your browser next reports.");
    await expect(note).not.toContainText("Next sync");

    // The extension applies the period on its next heartbeat; the page learns it from a status
    // read, which is what a return to the page performs.
    reportApplied(mock, 120);
    await page.reload({ waitUntil: "networkidle" });

    await expect(page.getByTestId("sync-frequency")).toHaveValue("120");
    await expect(page.getByTestId("sync-next-run")).toContainText(
      "as reported by your browser",
    );
  });

  test("keeps the stored frequency when the server refuses the change", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("connected"));
    mock.rejectInterval = 120;
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    const select = page.getByTestId("sync-frequency");
    await select.selectOption("120");

    expect(mock.scheduleRequests).toEqual([120]);

    // The refusal is named where the choice was made, and the choice falls back to what is stored.
    await expect(page.locator(".toast")).toContainText("intervalMinutes must be one of");
    await expect(select).toHaveValue("360");
    await expect(page.getByTestId("sync-next-run")).toContainText("Next sync");
  });
});
