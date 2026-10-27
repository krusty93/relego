import { expect, test } from "@playwright/test";

test.describe("settings", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/app/settings", { waitUntil: "networkidle" });
  });

  test("sections appear in the intended order", async ({ page }) => {
    await expect(page.locator(".settings-nav a")).toHaveText([
      "Connection",
      "Appearance",
      "When recaps go out",
      "Where recaps go",
      "Email server",
    ]);

    const sectionIds = await page
      .locator(".settings > div > section.panel")
      .evaluateAll((sections) => sections.map((section) => section.id));
    expect(sectionIds).toEqual([
      "s-conn",
      "s-appearance",
      "s-schedule",
      "s-delivery",
      "s-smtp",
    ]);
  });

  test("section subtitles use the available width on desktop and mobile", async ({ page }) => {
    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: 950 });
      const subtitles = page.locator(".settings .panel > header > p");
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
    await page.locator("#kindle").fill("reader@kindle.com");
    await page.locator("#s-delivery button[type=submit]").click();
    await expect(page.locator(".toast")).toContainText("saved");

    await page.reload({ waitUntil: "networkidle" });
    await expect(page.locator("#kindle")).toHaveValue("reader@kindle.com");
  });

  test("an invalid address is reported on the field itself", async ({ page }) => {
    await page.locator("#inbox").fill("me@inbox");
    await page.locator("#s-delivery button[type=submit]").click();

    await expect(page.locator("#inbox-err")).toBeVisible();
    await expect(page.locator("#inbox")).toHaveAttribute("aria-invalid", "true");

    await page.locator("#inbox").fill("");
  });

  // A Send-to-Kindle address on any other domain is silently dropped by Amazon hours
  // later, so the mistake has to be caught here rather than at delivery time.
  test.describe("Send-to-Kindle domain", () => {
    test("a well-formed address on the wrong domain is rejected", async ({ page }) => {
      await page.locator("#kindle").fill("reader@gmail.com");
      await page.locator("#s-delivery button[type=submit]").click();

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
      await page.locator("#s-delivery button[type=submit]").click();

      await expect(page.locator("#kindle-err")).toContainText("doesn't look like an email");
    });
  });

  test("weekly cadence reveals the day picker", async ({ page }) => {
    await page.locator("#s-schedule .seg button", { hasText: "Weekly" }).click();
    await expect(page.locator("#day")).toBeVisible();

    await page.locator("#s-schedule .seg button", { hasText: "Daily" }).click();
    await expect(page.locator("#day")).toBeHidden();
  });
});
