import { expect, test } from "@playwright/test";

test.describe("highlights", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/highlights", { waitUntil: "networkidle" });
  });

  test("only one highlight body is expanded at a time", async ({ page }) => {
    await expect(page.locator(".hl-body:visible")).toHaveCount(0);

    await page.locator(".hl-summary").first().click();
    await expect(page.locator(".hl-body:visible")).toHaveCount(1);

    await page.locator(".hl-summary").nth(1).click();
    await expect(page.locator(".hl-body:visible")).toHaveCount(1);
  });

  test("number keys set the recap weight", async ({ page }) => {
    await page.locator(".hl-summary").first().focus();
    await page.keyboard.press("5");

    await expect(page.locator(".hl .weight").first()).toHaveAttribute(
      "aria-label",
      "Recap weight 5 of 5",
    );
  });

  test("j moves focus down the list", async ({ page }) => {
    await page.locator(".hl-summary").first().focus();
    await page.keyboard.press("j");

    await expect(page.locator(".hl-summary").nth(1)).toBeFocused();
  });

  test("excluding offers an undo that restores the highlight", async ({ page }) => {
    await page.locator(".hl-summary").first().focus();
    await page.keyboard.press("e");

    const toast = page.locator(".toast");
    await expect(toast).toHaveCount(1);
    await expect(page.locator(".toast-action")).toHaveCount(1);

    await page.locator(".toast-action").click();
    await expect(page.locator('.hl .tag[data-tone="excluded"]')).toHaveCount(0);
  });
});
