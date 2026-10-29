import { expect, test } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

test.describe("settings", () => {
  test.beforeEach(async ({ page, request }) => {
    const delivery = await request.patch("/settings", {
      data: { kindleEmail: "reader@kindle.com", deliveryEmail: "reader@example.com" },
    });
    expect(delivery.ok()).toBe(true);
    const smtp = await request.put("/settings/smtp", {
      data: {
        host: "smtp.example.com", port: 587, fromAddress: "sender@example.com",
        username: "", password: "",
      },
    });
    expect(smtp.ok()).toBe(true);
    await page.route("**/settings/test-*-email", (route) =>
      route.fulfill({ json: { message: "Test email sent." } }),
    );
    await page.goto("/app/settings", { waitUntil: "networkidle" });
  });

  test("sections appear in the intended order", async ({ page }) => {
    await expect(page.locator(".settings-nav a")).toHaveText([
      "Connection",
      "Appearance",
      "Recaps",
    ]);

    const sectionIds = await page
      .locator(".settings > div > section.panel")
      .evaluateAll((sections) => sections.map((section) => section.id));
    expect(sectionIds).toEqual([
      "s-conn",
      "s-appearance",
      "s-recaps",
    ]);
    await expect(page.locator("#s-recaps > header > h2")).toHaveText("Recaps");
    await expect(page.locator("#s-recaps > .settings-section > header > h3")).toHaveText([
      "When recaps go out",
      "Email server",
      "Where recaps go",
    ]);
    await expect(page.locator("#s-recaps .panel")).toHaveCount(0);
  });

  test("section subtitles use the available width on desktop and mobile", async ({ page }) => {
    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: 950 });
      const subtitles = page.locator(".settings .panel > header > p, .settings-section > header > p");
      expect(await subtitles.count()).toBeGreaterThan(0);

      for (const subtitle of await subtitles.all()) {
        await expect(subtitle).toHaveCSS("max-width", "none");
        const widths = await subtitle.evaluate((element) => ({
          subtitle: element.getBoundingClientRect().width,
          header: element.parentElement!.getBoundingClientRect().width,
        }));
        expect(Math.abs(widths.subtitle - widths.header)).toBeLessThan(1);
      }
    }
  });

  test("delivery addresses survive a reload", async ({ page }) => {
    await page.locator("#kindle").fill("tested-reader@kindle.com");
    await expect(page.locator("#s-delivery button[type=submit]")).toBeDisabled();
    await page.locator("#s-delivery").getByRole("button", { name: "Send test" }).first().click();
    await expect(page.locator("#s-delivery button[type=submit]")).toBeEnabled();
    await page.locator("#s-delivery button[type=submit]").click();
    await expect(page.locator(".toast").filter({ hasText: "Settings saved." })).toBeVisible();

    await page.reload({ waitUntil: "networkidle" });
    await expect(page.locator("#kindle")).toHaveValue("tested-reader@kindle.com");
  });

  test("an invalid address is reported on the field itself", async ({ page }) => {
    await page.locator("#inbox").fill("me@inbox");
    await page.locator("#inbox").blur();

    await expect(page.locator("#inbox-err")).toBeVisible();
    await expect(page.locator("#inbox")).toHaveAttribute("aria-invalid", "true");

    await page.locator("#inbox").fill("");
  });

  // A Send-to-Kindle address on any other domain is silently dropped by Amazon hours
  // later, so the mistake has to be caught here rather than at delivery time.
  test.describe("Send-to-Kindle domain", () => {
    test("a well-formed address on the wrong domain is rejected", async ({ page }) => {
      await page.locator("#kindle").fill("reader@gmail.com");
      await page.locator("#kindle").blur();

      await expect(page.locator("#kindle-err")).toContainText("@kindle.com");
      await expect(page.locator("#kindle")).toHaveAttribute("aria-invalid", "true");
      await expect(page.getByRole("button", { name: "Send test" }).first()).toBeDisabled();
      await expect(page.locator(".toast")).toHaveCount(0);
    });

    test("kindle.com and its subdomains are accepted", async ({ page }) => {
      for (const address of ["reader@kindle.com", "reader@free.kindle.com"]) {
        await page.locator("#kindle").fill(address);
        await page.locator("#kindle").blur();

        await expect(page.locator("#kindle-err")).toHaveCount(0);
        await expect(page.getByRole("button", { name: "Send test" }).first()).toBeEnabled();
      }
    });

    test("a malformed address reports the format problem, not the domain", async ({ page }) => {
      await page.locator("#kindle").fill("reader@kindle");
      await page.locator("#kindle").blur();

      await expect(page.locator("#kindle-err")).toContainText("doesn't look like an email");
    });
  });

  test("weekly cadence reveals the day picker", async ({ page }) => {
    await page.locator("#s-schedule .seg button", { hasText: "Weekly" }).click();
    await expect(page.locator("#day")).toBeVisible();

    await page.locator("#s-schedule .seg button", { hasText: "Daily" }).click();
    await expect(page.locator("#day")).toBeHidden();
  });

  test("each changed destination must pass its own test before explicit saving", async ({ page }) => {
    const saved = await (await page.request.get("/settings")).json();
    const panel = page.locator("#s-delivery");
    const save = panel.getByRole("button", { name: "Save changes" });
    await page.locator("#kindle").fill("draft-reader@kindle.com");
    await page.locator("#inbox").fill("draft-reader@example.com");

    await expect(save).toBeDisabled();
    const kindleRequest = page.waitForRequest("**/settings/test-kindle-email");
    await panel.getByRole("button", { name: "Send test" }).first().click();
    expect((await kindleRequest).postDataJSON()).toEqual({ toAddress: "draft-reader@kindle.com" });
    await expect(panel.getByRole("status").first()).toContainText("Test succeeded");
    await expect(panel.getByRole("status").first().locator("svg")).toBeVisible();
    await expect(save).toBeDisabled();

    const inboxRequest = page.waitForRequest("**/settings/test-recap-email");
    await panel.getByRole("button", { name: "Send test" }).last().click();
    expect((await inboxRequest).postDataJSON()).toEqual({ toAddress: "draft-reader@example.com" });
    await expect(save).toBeEnabled();
    expect(await (await page.request.get("/settings")).json()).toEqual(saved);

    const savedResponse = page.waitForResponse((response) =>
      response.url().endsWith("/settings") && response.request().method() === "PATCH",
    );
    await save.click();
    expect((await savedResponse).ok()).toBe(true);
    await expect(save).toBeDisabled();
    await page.reload({ waitUntil: "networkidle" });
    await expect(page.locator("#kindle")).toHaveValue("draft-reader@kindle.com");
    await expect(page.locator("#inbox")).toHaveValue("draft-reader@example.com");
  });

  test("failed delivery tests show an icon and can be retried; editing invalidates success", async ({ page }) => {
    const panel = page.locator("#s-delivery");
    const saved = await (await page.request.get("/settings")).json();
    let fails = true;
    await page.route("**/settings/test-kindle-email", (route) =>
      route.fulfill({
        status: fails ? 502 : 200,
        json: fails ? { detail: "SMTP authentication failed. Check your credentials." } : {},
      }),
    );
    await page.locator("#kindle").fill("retry@kindle.com");
    await panel.getByRole("button", { name: "Send test" }).first().click();
    await expect(panel.getByRole("status").first()).toContainText("Test failed: SMTP authentication failed");
    await expect(panel.getByRole("status").first().locator("svg")).toBeVisible();
    await expect(panel.getByRole("button", { name: "Save changes" })).toBeDisabled();

    fails = false;
    await panel.getByRole("button", { name: "Send test" }).first().click();
    await expect(panel.getByRole("button", { name: "Save changes" })).toBeEnabled();
    await page.locator("#kindle").fill("edited@kindle.com");
    await expect(panel.locator(".test-result").first()).toBeEmpty();
    await expect(panel.getByRole("button", { name: "Save changes" })).toBeDisabled();
    await panel.locator("form").evaluate((form: HTMLFormElement) => form.requestSubmit());
    expect(await (await page.request.get("/settings")).json()).toEqual(saved);
  });

  test("a response for an earlier draft cannot unlock saving", async ({ page }) => {
    let finish!: () => void;
    const pending = new Promise<void>((resolve) => { finish = resolve; });
    await page.route("**/settings/test-kindle-email", async (route) => {
      await pending;
      await route.fulfill({ json: {} });
    });
    await page.locator("#kindle").fill("earlier@kindle.com");
    const request = page.waitForRequest("**/settings/test-kindle-email");
    await page.locator("#s-delivery").getByRole("button", { name: "Send test" }).first().click();
    await request;
    await page.locator("#kindle").fill("latest@kindle.com");
    const response = page.waitForResponse("**/settings/test-kindle-email");
    finish();
    await response;
    await expect(page.locator("#s-delivery").getByRole("button", { name: "Send test" }).first()).toBeEnabled();
    await expect(page.locator("#s-delivery .test-result").first()).toBeEmpty();
    await expect(page.locator("#s-delivery button[type=submit]")).toBeDisabled();
  });

  for (const field of ["kindle", "inbox"] as const) {
    test(`clearing ${field} needs no test and leaves the other destination intact`, async ({ page }) => {
      const saved = await (await page.request.get("/settings")).json();
      await page.locator(`#${field}`).fill("");
      await expect(page.locator("#s-delivery button[type=submit]")).toBeEnabled();
      const savedResponse = page.waitForResponse((response) =>
        response.url().endsWith("/settings") && response.request().method() === "PATCH",
      );
      await page.locator("#s-delivery button[type=submit]").click();
      expect((await savedResponse).ok()).toBe(true);
      await expect(page.locator("#s-delivery button[type=submit]")).toBeDisabled();
      await page.reload({ waitUntil: "networkidle" });
      await expect(page.locator(`#${field}`)).toHaveValue("");
      const other = field === "kindle" ? "inbox" : "kindle";
      await expect(page.locator(`#${other}`)).toHaveValue(
        (other === "kindle" ? saved.kindleEmail : saved.deliveryEmail) ?? "",
      );
    });
  }

  test("saving the schedule does not discard tested delivery drafts", async ({ page }) => {
    await page.locator("#kindle").fill("unsaved@kindle.com");
    await page.locator("#s-delivery").getByRole("button", { name: "Send test" }).first().click();
    await expect(page.locator("#s-delivery button[type=submit]")).toBeEnabled();

    const savedResponse = page.waitForResponse((response) =>
      response.url().endsWith("/settings") && response.request().method() === "PATCH",
    );
    await page.locator("#s-schedule button[type=submit]").click();
    expect((await savedResponse).ok()).toBe(true);

    await expect(page.locator("#kindle")).toHaveValue("unsaved@kindle.com");
    await expect(page.locator("#s-delivery button[type=submit]")).toBeEnabled();
    expect((await (await page.request.get("/settings")).json()).kindleEmail).toBe("reader@kindle.com");
  });

  test("SMTP test uses unsaved form values and only Save changes persists them", async ({ page }) => {
    const saved = await (await page.request.get("/settings/smtp")).json();
    const panel = page.locator("#s-smtp");
    const save = panel.getByRole("button", { name: "Save changes" });
    await page.route("**/settings/smtp/test", (route) =>
      route.fulfill({ json: { success: true, message: "Test message accepted." } }),
    );
    await page.locator("#host").fill("draft.example.com");
    await page.locator("#port").fill("2525");
    await page.locator("#user").fill("draft-user");
    await page.locator("#pass").fill("draft-secret");
    await page.locator("#from").fill("draft@example.com");
    await expect(save).toBeDisabled();

    const request = page.waitForRequest("**/settings/smtp/test");
    await panel.getByRole("button", { name: "Test connection" }).click();
    expect((await request).postDataJSON()).toEqual({
      settings: {
        host: "draft.example.com", port: 2525, username: "draft-user",
        password: "draft-secret", fromAddress: "draft@example.com",
      },
      toAddress: null,
    });
    await expect(panel.getByRole("status")).toContainText("Test succeeded");
    await expect(panel.getByRole("status").locator("svg")).toBeVisible();
    await expect(save).toBeEnabled();
    expect(await (await page.request.get("/settings/smtp")).json()).toEqual(saved);

    const savedResponse = page.waitForResponse((response) =>
      response.url().endsWith("/settings/smtp") && response.request().method() === "PUT",
    );
    await save.click();
    expect((await savedResponse).ok()).toBe(true);
    await expect(save).toBeDisabled();
    await page.reload({ waitUntil: "networkidle" });
    await expect(page.locator("#host")).toHaveValue("draft.example.com");
    await expect(page.locator("#pass")).toHaveValue("");
    await expect(page.locator("#pass")).toHaveAttribute("placeholder", /Saved/);
  });

  test("SMTP failure stays visible and a retry unlocks only the tested draft", async ({ page }) => {
    const panel = page.locator("#s-smtp");
    const saved = await (await page.request.get("/settings/smtp")).json();
    let succeeds = false;
    await page.route("**/settings/smtp/test", (route) =>
      route.fulfill({ json: { success: succeeds, message: succeeds ? "Connected." : "Check the SMTP host." } }),
    );
    await page.locator("#host").fill("retry.example.com");
    await panel.getByRole("button", { name: "Test connection" }).click();
    await expect(panel.getByRole("status")).toContainText("Test failed: Check the SMTP host.");
    await expect(panel.getByRole("status").locator("svg")).toBeVisible();
    await expect(panel.getByRole("button", { name: "Save changes" })).toBeDisabled();

    succeeds = true;
    const request = page.waitForRequest("**/settings/smtp/test");
    await panel.getByRole("button", { name: "Test connection" }).click();
    expect((await request).postDataJSON().settings).not.toHaveProperty("password");
    await expect(panel.getByRole("button", { name: "Save changes" })).toBeEnabled();
    await page.locator("#pass").fill("different-secret");
    await expect(panel.locator(".test-result")).toBeEmpty();
    await expect(panel.getByRole("button", { name: "Save changes" })).toBeDisabled();
    await panel.locator("form").evaluate((form: HTMLFormElement) => form.requestSubmit());
    expect(await (await page.request.get("/settings/smtp")).json()).toEqual(saved);
  });

  test("test results remain accessible on desktop and mobile in both themes", async ({ page }, testInfo) => {
    await page.route("**/settings/test-recap-email", (route) =>
      route.fulfill({ status: 502, json: { detail: "Check your SMTP credentials and try again." } }),
    );
    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: 950 });
      for (const theme of ["light", "dark"] as const) {
        await page.evaluate((value) => localStorage.setItem("relego.theme", value), theme);
        await page.reload({ waitUntil: "networkidle" });
        await page.locator("#kindle").fill("accessible@kindle.com");
        await page.locator("#inbox").fill("accessible@example.com");
        await page.locator("#s-delivery").getByRole("button", { name: "Send test" }).first().click();
        await page.locator("#s-delivery").getByRole("button", { name: "Send test" }).last().click();
        await expect(page.locator("#s-delivery").getByRole("status").first()).toContainText("Test succeeded");
        await expect(page.locator("#s-delivery").getByRole("status").last()).toContainText("Test failed");
        const { violations } = await new AxeBuilder({ page }).include("#s-recaps").analyze();
        expect(violations).toEqual([]);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        await page.locator("#s-recaps").screenshot({
          path: testInfo.outputPath(`recaps-${width}-${theme}.png`),
        });
      }
    }
  });
});
