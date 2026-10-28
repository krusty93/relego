import { expect, test } from "@playwright/test";

const routes = ["", "/highlights", "/recaps", "/import", "/sync", "/settings"];

for (const width of [1440, 1024, 393]) {
  test(`page headers use the available width at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 950 });

    for (const route of routes) {
      await page.goto(`/app${route}`, { waitUntil: "networkidle" });
      const header = page.locator(".view-head");
      const heading = header.locator(":scope > div").first();
      const description = header.locator("p");
      const actions = header.locator(".actions");
      const headerBox = (await header.boundingBox())!;
      const headingBox = (await heading.boundingBox())!;
      const descriptionBox = (await description.boundingBox())!;

      expect(descriptionBox.width).toBeCloseTo(headingBox.width, 0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);

      if (await actions.count()) {
        const actionsBox = (await actions.boundingBox())!;
        if (width > 900) {
          expect(actionsBox.x - (descriptionBox.x + descriptionBox.width)).toBeCloseTo(16, 0);
          expect(headingBox.y + headingBox.height / 2).toBeCloseTo(
            actionsBox.y + actionsBox.height / 2,
            0,
          );
        } else {
          expect(actionsBox.y - (headingBox.y + headingBox.height)).toBeCloseTo(16, 0);
          expect(actionsBox.width).toBeCloseTo(headerBox.width, 0);
        }
      } else {
        expect(descriptionBox.width).toBeCloseTo(headerBox.width, 0);
      }
    }
  });
}
