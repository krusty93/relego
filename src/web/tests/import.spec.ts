import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";

const CLIPPINGS = fileURLToPath(
  new URL("../../Relego.Tests/Fixtures/kindle-highlights.txt", import.meta.url),
);

test("re-importing a known file reports its duplicates", async ({ page }) => {
  await page.goto("/import", { waitUntil: "networkidle" });
  await page.locator("input[type=file]").setInputFiles(CLIPPINGS);

  const panel = page.locator(".import-panel");
  await expect(panel.locator(".dl")).toBeVisible();
  await expect(panel).toContainText("Already had");
});

// Import is reachable from both list views, not just the library.
for (const [name, path] of [
  ["library", "/"],
  ["highlights", "/highlights"],
  ["a book's highlights", "/books/1"],
] as const) {
  test(`${name} offers a route to import`, async ({ page }) => {
    await page.goto(path, { waitUntil: "networkidle" });

    await page.locator(".view-head").getByRole("button", { name: "Import highlights" }).click();
    await expect(page).toHaveURL(/\/import$/);
  });
}
