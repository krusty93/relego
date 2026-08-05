import { expect, test } from "@playwright/test";

test.describe("library", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
  });

  test("n renames a book and the table picks up the new title", async ({ page }) => {
    const row = page.locator("tbody tr").first();
    const original = (await row.locator("td").first().innerText()).trim();

    await row.focus();
    await page.keyboard.press("n");
    await expect(page.getByRole("dialog", { name: /rename/i })).toBeVisible();

    await page.locator("#rename-title").fill(`${original} (renamed)`);
    await page.locator("dialog button[type=submit]").click();
    await expect(page.locator("tbody")).toContainText(`${original} (renamed)`);

    await page.locator("tbody tr", { hasText: `${original} (renamed)` }).first().focus();
    await page.keyboard.press("n");
    await page.locator("#rename-title").fill(original);
    await page.locator("dialog button[type=submit]").click();
    await expect(page.locator("tbody")).not.toContainText("(renamed)");
  });

  test("opening a book drills into its highlights", async ({ page }) => {
    await page.locator("tbody tr").first().click();

    await expect(page).toHaveURL(/\/books\/\d+$/);
    await expect(page.locator(".view > .breadcrumb")).toBeVisible();
  });
});
