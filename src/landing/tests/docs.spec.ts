import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const docsPages = [
	'/docs/',
	'/docs/capture/',
	'/docs/import/',
	'/docs/select/',
	'/docs/deliver/',
	'/docs/revisit/',
	'/docs/reference/cli/',
	'/docs/reference/settings/',
	'/docs/reference/environment/',
	'/docs/reference/troubleshooting/',
	'/docs/reference/verifying-releases/',
];

test.describe('Docs', () => {
	test('every documented route renders with its title', async ({ page }) => {
		for (const path of docsPages) {
			const response = await page.goto(path);
			expect(response?.status(), `${path} should resolve`).toBe(200);
			await expect(page.locator('main h1')).toBeVisible();
		}
	});

	test('the overview renders all five round-trip stations', async ({ page }) => {
		await page.goto('/docs/');

		const stations = page.locator('.roundtrip__station');
		await expect(stations).toHaveCount(5);
		await expect(stations.first()).toContainText('Capture');
		await expect(stations.last()).toContainText('Revisit');
	});

	test('the retired Store route redirects into Import', async ({ page }) => {
		await page.goto('/docs/store/');
		await expect(page).toHaveURL(/\/docs\/import\/$/);
	});

	test('Import leads with the web UI and keeps the CLI as the second path', async ({ page }) => {
		await page.goto('/docs/import/');

		const options = page.locator('.sl-markdown-content h3', { hasText: /^Option/ });
		await expect(options).toHaveCount(2);
		await expect(options.first()).toContainText('Option 1');
		await expect(options.first()).toContainText('web UI');
		await expect(options.nth(1)).toContainText('Option 2');
		await expect(options.nth(1)).toContainText('command line');

		// The server has to be running before either path works.
		const steps = page.locator('.sl-markdown-content h2');
		await expect(steps.first()).toContainText('Start the server');
	});

	test('the landing page links into the docs', async ({ page }) => {
		await page.goto('/');
		await page.click('nav a:has-text("Docs")');
		await expect(page).toHaveURL(/\/docs\/$/);
	});

	test('the docs site title links back to the landing page', async ({ page }) => {
		await page.goto('/docs/');
		await expect(page.locator('a.relego-sitetitle')).toHaveAttribute('href', '/');
	});

	test('the theme chosen on the landing page carries into the docs', async ({ page }) => {
		await page.emulateMedia({ colorScheme: 'light' });
		await page.goto('/');
		await page.click('#theme-toggle');
		await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

		await page.goto('/docs/');
		await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
	});

	test('no critical or serious axe violations on desktop', async ({ page }) => {
		for (const path of ['/docs/', '/docs/reference/cli/', '/docs/deliver/']) {
			await page.goto(path);
			const results = await new AxeBuilder({ page }).analyze();
			const blocking = results.violations.filter(
				(violation) => violation.impact === 'critical' || violation.impact === 'serious',
			);
			expect(blocking, `${path}: ${blocking.map((v) => v.id).join(', ')}`).toEqual([]);
		}
	});

	test('no critical or serious axe violations on a phone viewport', async ({ page }) => {
		await page.setViewportSize({ width: 390, height: 844 });

		for (const path of ['/docs/', '/docs/reference/settings/']) {
			await page.goto(path);
			const results = await new AxeBuilder({ page }).analyze();
			const blocking = results.violations.filter(
				(violation) => violation.impact === 'critical' || violation.impact === 'serious',
			);
			expect(blocking, `${path}: ${blocking.map((v) => v.id).join(', ')}`).toEqual([]);
		}
	});

	test('reference tables stay readable on a phone instead of overflowing the page', async ({
		page,
	}) => {
		await page.setViewportSize({ width: 390, height: 844 });
		await page.goto('/docs/reference/settings/');

		const overflows = await page.evaluate(
			() => document.documentElement.scrollWidth > window.innerWidth + 1,
		);
		expect(overflows, 'the page itself must not scroll horizontally').toBe(false);

		const tableScrolls = await page.evaluate(() => {
			const table = document.querySelector('.sl-markdown-content table');
			return table ? table.scrollWidth > table.clientWidth : false;
		});
		expect(tableScrolls, 'wide tables scroll inside their own box').toBe(true);
	});
});
