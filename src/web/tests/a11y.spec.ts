import { expect, test, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const ROUTES = [
  ["library", "/"],
  ["highlights", "/highlights"],
  ["recaps", "/recaps"],
  ["import", "/import"],
  ["settings", "/settings"],
] as const;

const THEMES = ["light", "dark"] as const;

async function expectNoViolations(page: Page) {
  const { violations } = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "best-practice"])
    .analyze();

  expect(
    violations.map((v) => `[${v.impact}] ${v.id}: ${v.help} — ${v.nodes[0]?.target.join(" ")}`),
  ).toEqual([]);
}

for (const theme of THEMES) {
  test.describe(`${theme} theme`, () => {
    test.beforeEach(async ({ page }) => {
      await page.emulateMedia({ colorScheme: theme });
      await page.addInitScript((value) => localStorage.setItem("relego.theme", value), theme);
    });

    for (const [name, path] of ROUTES) {
      test(`${name} has no accessibility violations`, async ({ page }) => {
        await page.goto(path, { waitUntil: "networkidle" });
        await expect(page.locator("h1")).toBeVisible();
        await expectNoViolations(page);
      });
    }

    test("overlays and expanded rows have no accessibility violations", async ({
      page,
      isMobile,
    }, testInfo) => {
      test.skip(isMobile === true, "Overlay chrome is desktop-only.");
      testInfo.setTimeout(90_000);

      await page.goto("/highlights", { waitUntil: "networkidle" });
      await page.locator(".hl-summary").first().click();
      await expect(page.locator(".hl-body").first()).toBeVisible();
      await expectNoViolations(page);

      await page.keyboard.press("Control+k");
      await expect(page.getByRole("dialog", { name: "Command palette" })).toBeVisible();
      await expectNoViolations(page);
      await page.keyboard.press("Escape");

      await page.locator("main").click({ position: { x: 10, y: 10 } });
      await page.keyboard.press("?");
      await expect(page.getByRole("dialog", { name: /shortcut/i })).toBeVisible();
      await expectNoViolations(page);
      await page.keyboard.press("Escape");

      await page.goto("/", { waitUntil: "networkidle" });
      await page.locator("tbody tr").first().focus();
      await page.keyboard.press("n");
      await expect(page.locator("#rename-title")).toBeVisible();
      await expectNoViolations(page);
    });
  });
}
