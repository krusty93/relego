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

function statusResponse(status: string, overrides: Payload = {}): Payload {
  const connected = status !== "not_connected" && status !== "awaiting_pairing";

  return {
    providerId: "kindle-cloud",
    status,
    connection: {
      state: connected ? "active" : "pending_pairing",
      connectedAt: connected ? "2024-05-01T09:00:00Z" : null,
      lastHeartbeatAt: connected ? "2024-05-01T10:05:00Z" : null,
      browserReporting: connected,
      region: connected
        ? { id: "uk", displayName: "United Kingdom", host: "read.amazon.co.uk" }
        : null,
    },
    schedule: {
      intervalMinutes: 360,
      allowedIntervalMinutes: [15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440],
      nextRunAt: connected ? "2024-05-01T16:05:00Z" : null,
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
    startedAt: "2024-05-01T10:00:00Z",
    endedAt: null,
    ...overrides,
  };
}

interface SyncMock {
  status: Payload;
  requests: string[];
  statusCalls: number;
}

/** Serves the sync surface from a stub so every state is reachable without the extension. */
async function mockSync(page: Page, initial: Payload): Promise<SyncMock> {
  const mock: SyncMock = { status: initial, requests: [], statusCalls: 0 };

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
  for (const width of [1440, 390]) {
    for (const initial of ["not_connected", "awaiting_pairing"]) {
      test(`serializes token creation from ${initial} at ${width}px`, async ({ page }) => {
        await page.setViewportSize({ width, height: 950 });
        const mock = await mockSync(page, statusResponse(initial));
        let release!: () => void;
        const pending = new Promise<void>((resolve) => { release = resolve; });
        let posts = 0;
        await page.route("**/sync/kindle-cloud/connection", async (route) => {
          posts++;
          await pending;
          mock.status = statusResponse("awaiting_pairing");
          await route.fulfill({ status: 201, json: { providerId: "kindle-cloud", token: "LATEST-CODE" } });
        });
        await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });
        const button = page.getByRole("button", {
          name: initial === "not_connected" ? "Connect Kindle cloud" : "Generate a new code",
        });
        await button.evaluate((element: HTMLButtonElement) => {
          element.click();
          element.click();
          element.click();
        });
        await expect.poll(() => posts).toBe(1);
        await expect(page.getByRole("button", { name: initial === "not_connected" ? "Starting…" : "Generating…" }))
          .toBeDisabled();
        if (initial === "awaiting_pairing") {
          await expect(page.getByRole("button", { name: "Cancel setup" })).toBeDisabled();
        }
        release();
        await expect(oneTimeCode(page)).toHaveText("LATEST-CODE");
        await expect(page.getByRole("button", { name: "Generate a new code" })).toBeEnabled();
        expect(posts).toBe(1);
      });
    }
  }

  test("token creation can retry after a failed request", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("not_connected"));
    let posts = 0;
    await page.route("**/sync/kindle-cloud/connection", async (route) => {
      posts++;
      if (posts === 1) {
        await route.fulfill({ status: 503, json: { detail: "Server is temporarily unavailable." } });
        return;
      }
      mock.status = statusResponse("awaiting_pairing");
      await route.fulfill({ status: 201, json: { providerId: "kindle-cloud", token: "RETRY-CODE" } });
    });
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });
    const button = page.getByRole("button", { name: "Connect Kindle cloud" });
    await button.click();
    await expect.poll(() => posts).toBe(1);
    await expect(page.getByText("Server is temporarily unavailable.", { exact: false })).toBeVisible();
    await expect(button).toBeEnabled();
    await button.click();
    await expect(oneTimeCode(page)).toHaveText("RETRY-CODE");
    expect(posts).toBe(2);
  });

  test("the connect flow reads the limits before it offers the button", async ({ page }) => {
    const mock = await mockSync(page, statusResponse("not_connected"));
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "Coverage & limits" })).toBeVisible();
    await expect(page.getByText("Cloud only")).toBeVisible();
    await expect(page.getByText("Publisher limits")).toBeVisible();
    await expect(page.getByRole("heading", { name: "What this needs" })).toBeVisible();
    await expect(page.getByText("Chrome or Firefox with the Relego extension")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Privacy" })).toBeVisible();
    await expect(page.getByText("Your data path")).toBeVisible();

    // The disclosures are not decoration: the user meets them before the decision.
    const privacy = await page.getByRole("heading", { name: "Privacy" }).boundingBox();
    const button = await page.getByRole("button", { name: "Connect Kindle cloud" }).boundingBox();
    expect(privacy!.y).toBeLessThan(button!.y);

    await page.getByRole("button", { name: "Connect Kindle cloud" }).click();

    await expect(oneTimeCode(page)).toHaveText("K7QP-2M4X-9TDH");
    await expect(page.getByText("Choose your Amazon region in the extension")).toBeVisible();
    expect(mock.requests).toContain("POST /sync/kindle-cloud/connection");
  });

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
          occurredAt: "2024-05-01T10:07:00Z",
        },
      }),
    );
    await page.goto("/app/kindle-sync", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "Last sync did not finish" })).toBeVisible();
    await expect(page.getByText("Highlights already saved are still in your library.", { exact: false })).toBeVisible();
    await expect(page.getByText("Nothing partial was imported.", { exact: false })).toHaveCount(0);
    await expect(page.getByText("provider_contract_changed")).toBeVisible();
    await expect(page.getByText("no longer matches what the extension reads")).toBeVisible();
    await expect(page.getByRole("listitem").filter({ hasText: "Update the Relego extension" })).toBeVisible();
    await expect(page.getByRole("listitem").filter({ hasText: "Run a sync again" })).toBeVisible();
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
});
