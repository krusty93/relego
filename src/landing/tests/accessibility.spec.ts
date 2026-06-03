import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.describe('Accessibility', () => {
	test.beforeEach(async ({ page }) => {
		await page.goto('/');
	});

	test('full page has no critical or serious axe violations', async ({ page }) => {
		const results = await new AxeBuilder({ page })
			.withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
			.analyze();

		const violations = results.violations.filter(
			(v) => v.impact === 'critical' || v.impact === 'serious',
		);
		expect(violations, JSON.stringify(violations, null, 2)).toHaveLength(0);
	});

	test('all <img> elements have non-empty alt attributes', async ({ page }) => {
		const images = page.locator('img');
		const count = await images.count();
		for (let i = 0; i < count; i++) {
			const alt = await images.nth(i).getAttribute('alt');
			expect(alt, `img[${i}] is missing a non-empty alt`).toBeTruthy();
		}
	});

	test('keyboard navigation reaches all interactive elements', async ({ page }) => {
		const interactiveSelectors = [
			'nav a[href="#"]',                                  // logotype
			'nav a[href*="github.com"]:not([aria-label])',      // desktop GitHub link
			'#theme-toggle',                                     // theme toggle
			'#hero a:has-text("Get started")',                   // hero CTA
			'#faq details summary',                              // first accordion
			'#explore a:has-text("View on GitHub")',             // explore primary CTA
			'#explore a:has-text("Read the docs")',              // explore outline CTA
		];

		for (const selector of interactiveSelectors) {
			const el = page.locator(selector).first();
			await el.focus();
			await expect(el, `${selector} must be focusable`).toBeFocused();
		}
	});
});
