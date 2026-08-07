import { expect, test } from "@playwright/test";

test.describe("library", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
  });

  // The shortcuts used to be `onKeyDown` handlers on each row, so they only fired once a
  // row already had DOM focus. A user landing on the page and pressing `j` got nothing.
  // These tests deliberately never call focus() first.
  test.describe("shortcuts work without focusing a row first", () => {
    test("j and k walk the list, starting at the first row", async ({ page }) => {
      const rows = page.locator("tbody tr");
      await expect(rows.first()).toBeVisible();
      expect(await page.evaluate(() => document.activeElement?.tagName)).toBe("BODY");

      await page.keyboard.press("j");
      await expect(rows.nth(0)).toBeFocused();

      await page.keyboard.press("j");
      await expect(rows.nth(1)).toBeFocused();

      await page.keyboard.press("k");
      await expect(rows.nth(0)).toBeFocused();

      // Already at the top: k holds position rather than wrapping or losing focus.
      await page.keyboard.press("k");
      await expect(rows.nth(0)).toBeFocused();
    });

    test("n opens rename on the cursor row", async ({ page }) => {
      await expect(page.locator("tbody tr").first()).toBeVisible();

      await page.keyboard.press("n");
      await expect(page.getByRole("dialog", { name: /rename/i })).toBeVisible();
    });

    test("e excludes the cursor book", async ({ page }) => {
      const row = page.locator("tbody tr").first();
      await expect(row).toBeVisible();
      const before = await row.locator("td").last().innerText();

      await page.keyboard.press("e");
      await expect(page.locator(".toast")).toContainText(/recap/i);
      await expect(row.locator("td").last()).not.toHaveText(before);

      await page.keyboard.press("e");
      await expect(row.locator("td").last()).toHaveText(before);
    });

    test("typing in search is never intercepted", async ({ page }) => {
      await page.keyboard.press("/");
      await page.keyboard.type("junk");

      await expect(page.locator(".search input")).toHaveValue("junk");
      await expect(page.getByRole("dialog")).toHaveCount(0);
    });

    test("shortcuts stand down while a dialog owns the keyboard", async ({ page }) => {
      await expect(page.locator("tbody tr").first()).toBeVisible();
      await page.keyboard.press("n");
      await expect(page.getByRole("dialog", { name: /rename/i })).toBeVisible();

      // `e` would toggle exclusion if the page layer were still listening.
      await page.locator("dialog button[type=button]").first().focus();
      await page.keyboard.press("e");
      await expect(page.locator(".toast")).toHaveCount(0);
    });
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
