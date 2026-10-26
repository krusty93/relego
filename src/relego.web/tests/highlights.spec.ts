import { expect, test } from "@playwright/test";

test.describe("highlights", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/app/highlights", { waitUntil: "networkidle" });
  });

  test("only one highlight body is expanded at a time", async ({ page }) => {
    await expect(page.locator(".hl-body:visible")).toHaveCount(0);

    await page.locator(".hl-summary").first().click();
    await expect(page.locator(".hl-body:visible")).toHaveCount(1);

    await page.locator(".hl-summary").nth(1).click();
    await expect(page.locator(".hl-body:visible")).toHaveCount(1);
  });

  // Same regression as the library list: the keys used to require an already-focused card.
  test.describe("shortcuts work without focusing a card first", () => {
    test("j and k walk the list, starting at the first card", async ({ page }) => {
      const cards = page.locator(".hl-summary");
      await expect(cards.first()).toBeVisible();
      expect(await page.evaluate(() => document.activeElement?.tagName)).toBe("BODY");

      await page.keyboard.press("j");
      await expect(cards.nth(0)).toBeFocused();

      await page.keyboard.press("j");
      await expect(cards.nth(1)).toBeFocused();

      await page.keyboard.press("k");
      await expect(cards.nth(0)).toBeFocused();
    });

    test("number keys set the weight on the cursor card", async ({ page }) => {
      await expect(page.locator(".hl-summary").first()).toBeVisible();

      await page.keyboard.press("5");
      await expect(page.locator(".hl .weight").first()).toHaveAttribute(
        "aria-label",
        "Recap weight 5 of 5",
      );
    });

    test("e excludes the cursor highlight", async ({ page }) => {
      await expect(page.locator(".hl-summary").first()).toBeVisible();

      await page.keyboard.press("e");
      await expect(page.locator('.hl .tag[data-tone="excluded"]')).toHaveCount(1);

      await page.locator(".toast-action").click();
      await expect(page.locator('.hl .tag[data-tone="excluded"]')).toHaveCount(0);
    });
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

  test("dragging across the quote selects text instead of expanding the card", async ({ page }) => {
    const quote = page.locator(".hl-quote").first();
    await expect(quote).toHaveCSS("cursor", "text");
    const box = await quote.boundingBox();
    if (!box) throw new Error("Highlight quote has no box");

    await page.mouse.move(box.x + 4, box.y + box.height / 4);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 4, { steps: 8 });
    await page.mouse.up();

    const selected = await page.evaluate(() => window.getSelection()?.toString() ?? "");
    expect(selected.trim().length).toBeGreaterThan(0);
    await expect(page.locator(".hl-body:visible")).toHaveCount(0);
  });

  test("a plain click still expands the card", async ({ page }) => {
    await page.locator(".hl-summary").first().click();
    await expect(page.locator(".hl-body").first()).toBeVisible();
  });

  test("the card still toggles from the keyboard", async ({ page }) => {
    const first = page.locator(".hl-summary").first();
    await first.focus();

    await page.keyboard.press("Enter");
    await expect(page.locator(".hl-body").first()).toBeVisible();

    await page.keyboard.press(" ");
    await expect(page.locator(".hl-body").first()).toBeHidden();
  });

  test("copying puts the whole highlight on the clipboard", async ({ page, context }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);
    await page.locator(".hl-summary").first().click();

    const text = await page.locator(".hl-quote").first().evaluate((node) => node.textContent);
    await page.getByRole("button", { name: "Copy highlight text" }).first().click();

    await expect(page.locator(".toast")).toContainText("Highlight copied.");
    await expect
      .poll(() => page.evaluate(() => navigator.clipboard.readText()))
      .toBe(text);
  });

  test("deleting takes a second confirmation and Keep backs out", async ({ page }) => {
    const before = await page.locator(".hl").count();

    await page.locator(".hl-summary").first().click();
    await page.getByRole("button", { name: "Delete", exact: true }).click();

    const confirm = page.getByRole("group", { name: "Confirm delete" });
    await expect(confirm).toBeVisible();
    await expect(page.locator(".hl")).toHaveCount(before);

    await confirm.getByRole("button", { name: "Keep" }).click();
    await expect(confirm).toBeHidden();
    await expect(page.locator(".hl")).toHaveCount(before);
  });

  test("collapsing a row abandons a pending delete", async ({ page }) => {
    await page.locator(".hl-summary").first().click();
    await page.getByRole("button", { name: "Delete", exact: true }).click();
    await expect(page.getByRole("button", { name: "Yes, delete" })).toBeVisible();

    await page.locator(".hl-summary").first().click();
    await expect(page.getByRole("button", { name: "Yes, delete" })).toBeHidden();

    await page.locator(".hl-summary").first().click();
    await expect(page.getByRole("button", { name: "Yes, delete" })).toBeHidden();
  });

  test("confirming the delete removes the highlight", async ({ page }) => {
    const before = await page.locator(".hl").count();

    await page.locator(".hl-summary").last().click();
    await page.getByRole("button", { name: "Delete", exact: true }).click();
    await page.getByRole("button", { name: "Yes, delete" }).click();

    await expect(page.locator(".hl")).toHaveCount(before - 1);
  });
});
