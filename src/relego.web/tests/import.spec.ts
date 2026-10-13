import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";

const CLIPPINGS = fileURLToPath(
  new URL("../../Relego.Tests/Fixtures/kindle-highlights.txt", import.meta.url),
);

test("re-importing a known file reports its duplicates", async ({ page }) => {
  await page.goto("/app/import", { waitUntil: "networkidle" });
  await page.locator("input[type=file]").setInputFiles(CLIPPINGS);

  const panel = page.locator(".import-panel");
  await expect(panel.locator(".dl")).toBeVisible();
  await expect(panel).toContainText("Already had");
});

// Cloud sync is the other way in, so the manual route has to name it rather than leave the
// reader to find it.
test("import points at cloud sync as the way to stop doing this by hand", async ({ page }) => {
  await page.goto("/app/import", { waitUntil: "networkidle" });

  const link = page.getByRole("link", { name: "Connect your Kindle Notebook" });
  await expect(link).toHaveAttribute("href", "/app/sync");

  await link.click();
  await expect(page).toHaveURL(/\/app\/sync$/);
});

// Import is reachable from both list views, not just the library.
for (const [name, path] of [
  ["library", "/app"],
  ["highlights", "/app/highlights"],
  ["a book's highlights", "/app/books/1"],
] as const) {
  test(`${name} offers a route to import`, async ({ page }) => {
    await page.goto(path, { waitUntil: "networkidle" });

    await page.locator(".view-head").getByRole("button", { name: "Import highlights" }).click();
    await expect(page).toHaveURL(/\/import$/);
  });
}
