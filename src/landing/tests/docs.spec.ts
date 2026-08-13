import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const docsPages = [
	'/docs/',
	'/docs/import/',
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
			await expect(page.locator('main h1').first()).toBeVisible();
		}
	});

	test('the overview renders all three round-trip stations', async ({ page }) => {
		await page.goto('/docs/');

		const stations = page.locator('.roundtrip__station');
		await expect(stations).toHaveCount(3);
		await expect(stations.first()).toContainText('Import highlights');
		await expect(stations.nth(1)).toContainText('Deliver');
		await expect(stations.last()).toContainText('Revisit');
	});

	test('round-trip pages promote their opening copy into the title subtitle', async ({ page }) => {
		const subtitles = {
			'/docs/import/': 'The trip starts on the device',
			'/docs/deliver/': 'A library of ten thousand highlights is a graveyard',
			'/docs/revisit/': 'The last stage is the only one that matters',
		};

		for (const [path, text] of Object.entries(subtitles)) {
			await page.goto(path);
			await expect(page.locator('.relego-page-subtitle')).toContainText(text);
		}
	});

	test('round-trip pages keep one separator around the title', async ({ page }) => {
		for (const path of ['/docs/import/', '/docs/deliver/', '/docs/revisit/']) {
			await page.goto(path);
			await expect
				.poll(() =>
					page
						.locator('main > .content-panel:has(.sl-markdown-content)')
						.evaluate((panel) => getComputedStyle(panel).borderTopStyle),
				)
				.toBe('none');
		}
	});

	test('the right-hand TOCs hide the synthetic Overview entry', async ({ page }) => {
		await page.goto('/docs/');

		const overviewLinks = page.locator(
			'starlight-toc a[href="#_top"], mobile-starlight-toc a[href="#_top"]',
		);
		await expect(overviewLinks).toHaveCount(2);
		await expect
			.poll(() =>
				overviewLinks.evaluateAll((links) =>
					links.every((link) => getComputedStyle(link.parentElement!).display === 'none'),
				),
			)
			.toBe(true);
	});

	test('the right-hand TOC indents nested headings', async ({ page }) => {
		await page.goto('/docs/import/');

		const nestedHeading = page.locator('starlight-toc a[href="#option-1-the-web-ui"]');
		await expect(nestedHeading).toContainText('Option 1');
		await expect
			.poll(() => nestedHeading.evaluate((link) => parseFloat(getComputedStyle(link).paddingInlineStart)))
			.toBeGreaterThan(12);
	});

	test('the retired Store route redirects into Import', async ({ page }) => {
		await page.goto('/docs/store/');
		await expect(page).toHaveURL(/\/docs\/import\/$/);
	});

	test('the retired Capture route redirects into Import highlights', async ({ page }) => {
		await page.goto('/docs/capture/');
		await expect(page).toHaveURL(/\/docs\/import\/$/);
	});

	test('the retired Select route redirects into Deliver', async ({ page }) => {
		await page.goto('/docs/select/');
		await expect(page).toHaveURL(/\/docs\/deliver\/$/);
	});

	test('Deliver combines recap choices with delivery setup', async ({ page }) => {
		await page.goto('/docs/deliver/');
		await expect(page.locator('main h1')).toContainText('Deliver');
		await expect(page.locator('.sl-markdown-content h2', { hasText: 'How the choice is made' })).toBeVisible();
		await expect(page.locator('.sl-markdown-content h2', { hasText: 'Choose a relay' })).toBeVisible();
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
		await expect(steps.filter({ hasText: 'Start the server' })).toBeVisible();
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
