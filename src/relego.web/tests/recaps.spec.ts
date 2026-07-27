import { expect, request as playwrightRequest, test } from "@playwright/test";
import { API_URL } from "../playwright.config";

test.describe("recaps", () => {
  test("sending is blocked, with a reason, when no destination is set", async ({ page }) => {
    // The seeded server always has a reader address, so the empty state is
    // reproduced at the boundary rather than by unsetting server config.
    await page.route("**/settings", async (route) => {
      if (route.request().method() !== "GET") return route.fallback();
      const response = await route.fetch();
      const body = (await response.json()) as Record<string, unknown>;
      await route.fulfill({
        response,
        json: { ...body, kindleEmail: "", deliveryEmail: null },
      });
    });

    await page.goto("/app/recaps", { waitUntil: "networkidle" });

    await expect(page.getByRole("button", { name: "Send recap now" })).toBeDisabled();
    await expect(page.locator("#rc-blocked")).toContainText("Add a reader address first");

    await page.locator("#rc-blocked a").click();
    await expect(page).toHaveURL(/\/app\/settings$/);
  });

  test("sending is available once a destination exists", async ({ page }) => {
    await page.goto("/app/recaps", { waitUntil: "networkidle" });

    await expect(page.getByRole("button", { name: "Send recap now" })).toBeEnabled();
    await expect(page.locator("#rc-blocked")).toHaveCount(0);
  });

  // The page button is not the only way to send. On mobile the palette is the primary
  // action surface, so the same guard has to hold there.
  test("the command palette routes to settings instead of sending with no destination", async ({
    page,
  }) => {
    let sendAttempted = false;
    await page.route("**/settings", async (route) => {
      if (route.request().method() !== "GET") return route.fallback();
      const response = await route.fetch();
      const body = (await response.json()) as Record<string, unknown>;
      await route.fulfill({
        response,
        json: { ...body, kindleEmail: "", deliveryEmail: null },
      });
    });
    await page.route("**/recaps", (route) => {
      if (route.request().method() === "POST") sendAttempted = true;
      return route.fallback();
    });

    await page.goto("/app/recaps", { waitUntil: "networkidle" });

    await page.keyboard.press("ControlOrMeta+k");
    await page.getByRole("option", { name: "Send recap now" }).click();

    await expect(page).toHaveURL(/\/app\/settings$/);
    await expect(page.locator(".toast")).toContainText("Add a reader address first");
    expect(sendAttempted).toBe(false);
  });

  // The delivery time is configured in the server's timezone. A browser half a
  // day away must still see the configured clock time, labelled with the zone it
  // is actually expressed in.
  test.describe("next recap time", () => {
    test.use({ timezoneId: "Pacific/Kiritimati" });

    test("is rendered in the server's timezone, not the browser's", async ({ page }) => {
      const api = await playwrightRequest.newContext({ baseURL: API_URL });
      const response = await api.patch("/settings", {
        data: { schedule: "daily", deliveryTime: "18:00", timezone: "Europe/Rome" },
      });
      expect(response.ok()).toBeTruthy();
      await api.dispose();

      await page.goto("/app/recaps", { waitUntil: "networkidle" });

      const next = page.locator("time").first();
      await expect(next).toContainText("06:00 PM");
      await expect(page.locator(".dl dd").first().locator(".subtle")).toHaveText("Europe/Rome");

      // The same instant in the browser's own zone is the next morning, so the
      // assertion above could not pass by accident.
      const iso = await next.getAttribute("datetime");
      const local = new Date(iso!).toLocaleString("en-US", {
        timeZone: "Pacific/Kiritimati",
        hour: "2-digit",
        minute: "2-digit",
      });
      expect(local).not.toContain("06:00 PM");
    });
  });

  test("saving the schedule keeps the delivery address", async ({ page }) => {
    await page.goto("/app/settings", { waitUntil: "networkidle" });
    await page.locator("#inbox").fill("keeper@inbox.com");
    await page.locator("#s-delivery button[type=submit]").click();
    await expect(page.locator(".toast")).toContainText("saved");

    await page.locator("#s-schedule button[type=submit]").click();
    await expect(page.locator(".toast").last()).toContainText("saved");

    await page.reload({ waitUntil: "networkidle" });
    await expect(page.locator("#inbox")).toHaveValue("keeper@inbox.com");
  });
});
