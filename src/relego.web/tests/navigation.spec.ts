import { expect, test } from "@playwright/test";

test.describe("navigation and global keys", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
  });

  test("feedback opens the GitHub issue chooser above shortcuts", async ({ page }) => {
    const feedback = page.getByRole("link", { name: "Give feedback", exact: false });
    await expect(feedback).toBeVisible();
    await expect(feedback).toHaveAttribute(
      "href",
      "https://github.com/krusty93/relego/issues/new/choose",
    );
    await expect(feedback).toHaveAttribute("target", "_blank");
    await expect(feedback).toHaveAttribute("rel", "noopener noreferrer");
    await expect(feedback.locator("xpath=following-sibling::*[1]")).toHaveText("Shortcuts ?");

    await page.context().route("https://github.com/**", (route) =>
      route.fulfill({ contentType: "text/html", body: "<title>GitHub issue chooser</title>" }),
    );
    await feedback.focus();
    const popupPromise = page.waitForEvent("popup");
    await page.keyboard.press("Enter");
    const popup = await popupPromise;
    await expect(popup).toHaveURL("https://github.com/krusty93/relego/issues/new/choose");
    await expect(page).toHaveURL(/localhost:\d+\/app$/);
    await popup.close();
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
