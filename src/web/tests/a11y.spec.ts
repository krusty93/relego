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

    test("touch users reach the palette and the delete confirmation", async ({
      page,
      isMobile,
    }) => {
      test.skip(isMobile !== true, "Covers the touch path; desktop uses chords.");

      await page.goto("/highlights", { waitUntil: "networkidle" });

      await page.getByRole("button", { name: "Commands" }).tap();
      await expect(page.getByRole("dialog", { name: "Command palette" })).toBeVisible();
      await expectNoViolations(page);
      await page.keyboard.press("Escape");

      await page.locator(".hl-summary").first().tap();
      await expect(page.locator(".hl-body").first()).toBeVisible();
      await page.getByRole("button", { name: "Delete", exact: true }).first().tap();
      await expect(page.getByRole("button", { name: "Yes, delete" })).toBeVisible();
      await expectNoViolations(page);
    });

    // An outset focus ring on a control that sits flush inside an `overflow: hidden`
    // parent is clipped away entirely — the keyboard user sees no ring at all. axe
    // cannot see this, so the rule is asserted directly.
    test("focus rings are not clipped by their container", async ({ page, isMobile }) => {
      test.skip(isMobile === true, "Table rows are stacked on mobile.");

      for (const [path, selector] of [
        ["/highlights", ".hl-summary"],
        ["/", "tbody tr"],
      ] as const) {
        await page.goto(path, { waitUntil: "networkidle" });
        await expect(page.locator(selector).first()).toBeVisible();

        // Tab in rather than calling focus(), so `:focus-visible` genuinely matches.
        let reached = false;
        for (let i = 0; i < 40 && !reached; i += 1) {
          await page.keyboard.press("Tab");
          reached = await page.evaluate(
            (sel) => document.activeElement?.matches(`${sel}:focus-visible`) === true,
            selector,
          );
        }
        expect(reached, `tabbed to ${selector}`).toBe(true);

        const ring = await page.evaluate(() => {
          const el = document.activeElement!;
          const style = getComputedStyle(el);
          const rect = el.getBoundingClientRect();

          let clip: DOMRect | null = null;
          for (let node = el.parentElement; node; node = node.parentElement) {
            const parent = getComputedStyle(node);
            if (/hidden|auto|scroll|clip/.test(parent.overflowX + parent.overflowY)) {
              clip = node.getBoundingClientRect();
              break;
            }
          }

          const grow = parseFloat(style.outlineOffset) + parseFloat(style.outlineWidth);
          return {
            hasOutline: style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0,
            outer: {
              top: rect.top - grow,
              left: rect.left - grow,
              right: rect.right + grow,
              bottom: rect.bottom + grow,
            },
            clip: clip && {
              top: clip.top,
              left: clip.left,
              right: clip.right,
              bottom: clip.bottom,
            },
          };
        });

        expect(ring.hasOutline, `${selector} draws a focus outline`).toBe(true);
        if (!ring.clip) continue;

        expect(
          {
            top: ring.outer.top >= ring.clip.top,
            left: ring.outer.left >= ring.clip.left,
            right: ring.outer.right <= ring.clip.right,
            bottom: ring.outer.bottom <= ring.clip.bottom,
          },
          `${selector} focus ring escapes its clipping ancestor`,
        ).toEqual({ top: true, left: true, right: true, bottom: true });
      }
    });

    // Placeholders are text. They inherit no exemption from the 4.5:1 floor, and the
    // UA default (#757575) fails it in both themes.
    test("placeholder text meets the 4.5:1 contrast floor", async ({ page }) => {
      const measure = () =>
        page.evaluate(() => {
          function parse(value: string) {
            const [r, g, b, a = "1"] = value.match(/[\d.]+/g) ?? [];
            return [Number(r), Number(g), Number(b), Number(a)] as const;
          }
          function luminance([r, g, b]: readonly number[]) {
            const channel = (v: number) => {
              const c = v! / 255;
              return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
            };
            return 0.2126 * channel(r!) + 0.7152 * channel(g!) + 0.0722 * channel(b!);
          }
          function backgroundOf(el: Element) {
            for (let node: Element | null = el; node; node = node.parentElement) {
              const bg = parse(getComputedStyle(node).backgroundColor);
              if (bg[3] > 0) return bg;
            }
            return [255, 255, 255, 1] as const;
          }

          return [...document.querySelectorAll("input, textarea")]
            .filter((el) => (el as HTMLInputElement).placeholder)
            .map((el) => {
              const style = getComputedStyle(el, "::placeholder");
              const fg = parse(style.color);
              const bg = backgroundOf(el);
              const alpha = fg[3] * Number(style.opacity || "1");
              const blended = [0, 1, 2].map((i) => fg[i]! * alpha + bg[i]! * (1 - alpha));
              const [a, b] = [luminance(blended), luminance(bg)].sort((x, y) => y - x);
              return {
                placeholder: (el as HTMLInputElement).placeholder,
                ratio: Math.round(((a! + 0.05) / (b! + 0.05)) * 100) / 100,
              };
            });
        });

      const measured: { placeholder: string; ratio: number }[] = [];

      for (const [, path] of ROUTES) {
        await page.goto(path, { waitUntil: "networkidle" });
        await expect(page.locator("h1")).toBeVisible();
        measured.push(...(await measure()));
      }

      // The palette input lives in a `<dialog>`, so it is only measurable when open.
      await page.keyboard.press("ControlOrMeta+k");
      await expect(page.getByRole("dialog", { name: "Command palette" })).toBeVisible();
      measured.push(...(await measure()));

      expect(measured.length).toBeGreaterThan(0);
      expect(measured.filter((entry) => entry.ratio < 4.5)).toEqual([]);
    });
  });
}
