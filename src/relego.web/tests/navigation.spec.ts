import { expect, test } from "@playwright/test";

test.describe("navigation and global keys", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
  });

  test("g chords jump between views", async ({ page }) => {
    await page.keyboard.press("g");
    await page.keyboard.press("s");
    await expect(page).toHaveURL(/\/app\/settings$/);

    await page.keyboard.press("g");
    await page.keyboard.press("l");
    await expect(page).toHaveURL(/localhost:\d+\/app$/);
  });

  test("chords stay inert while typing in search", async ({ page }) => {
    const search = page.locator(".search input");
    await search.fill("");
    await search.focus();
    await page.keyboard.type("gs");

    await expect(page).toHaveURL(/localhost:\d+\/app$/);
    await expect(search).toHaveValue("gs");

    await page.keyboard.press("Escape");
    await search.fill("");
  });

  test("the command palette opens, filters and closes", async ({ page }) => {
    await page.keyboard.press("Control+k");
    const palette = page.getByRole("dialog", { name: "Command palette" });
    await expect(palette).toBeVisible();

    await page.keyboard.type("recap");
    const options = page.locator("#palette-list [role=option]");
    await expect(options).not.toHaveCount(0);
    expect(await options.count()).toBeLessThan(8);

    await page.keyboard.press("Escape");
    await expect(palette).toBeHidden();
  });

  test("t cycles the theme, announces it and remembers the choice", async ({ page }) => {
    const html = page.locator("html");
    const before = await html.getAttribute("data-theme");

    await page.keyboard.press("t");

    await expect(html).not.toHaveAttribute("data-theme", before ?? "");
    await expect(page.locator(".toast")).not.toHaveCount(0);
    expect(await page.evaluate(() => localStorage.getItem("relego.theme"))).not.toBeNull();
  });
});
