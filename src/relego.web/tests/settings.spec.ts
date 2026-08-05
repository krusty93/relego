import { expect, test } from "@playwright/test";

test.describe("settings", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/settings", { waitUntil: "networkidle" });
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

  test("weekly cadence reveals the day picker", async ({ page }) => {
    await page.locator("#s-schedule .seg button", { hasText: "Weekly" }).click();
    await expect(page.locator("#day")).toBeVisible();

    await page.locator("#s-schedule .seg button", { hasText: "Daily" }).click();
    await expect(page.locator("#day")).toBeHidden();
  });
});
