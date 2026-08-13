import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const docsPages = [
	'/docs/',
	'/docs/round-trip/',
	'/docs/import/',
	'/docs/deliver/',
	'/docs/revisit/',
	'/docs/reference/cli/',
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

	test('the round-trip overview renders all three stations', async ({ page }) => {
		await page.goto('/docs/round-trip/');

		const stations = page.locator('.roundtrip__station');
		await expect(stations).toHaveCount(3);
		await expect(stations.first()).toContainText('Import highlights');
		await expect(stations.nth(1)).toContainText('Deliver');
		await expect(stations.last()).toContainText('Revisit');
	});

	test('the docs homepage launches a reader into their first recap', async ({ page }) => {
		await page.goto('/docs/');
		await expect(page.getByRole('heading', { name: 'What is Relego?' })).toBeVisible();
		await expect(page.getByRole('heading', { name: 'Set up your first recap' })).toBeVisible();
		await expect(
			page.locator('.sl-markdown-content img[src="/images/docs/relego-web-library.webp"]'),
		).toBeVisible();
		await expect(
			page.locator('.first-recap__start'),
		).toHaveAttribute('href', '/docs/round-trip/');
		await expect(page.locator('.first-recap__checklist li')).toHaveCount(4);
		await expect(page.locator('.first-recap__checklist')).toContainText('Send a recap now');
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

	test('docs pages do not separate the title from the content', async ({ page }) => {
		for (const path of ['/docs/', '/docs/import/', '/docs/deliver/', '/docs/revisit/']) {
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

	test('the mobile contents control does not show Starlight’s synthetic Overview label', async ({
		page,
	}) => {
		await page.setViewportSize({ width: 390, height: 844 });
		await page.goto('/docs/import/');

		const currentPageLabel = page.locator('mobile-starlight-toc .display-current');
		await expect(currentPageLabel).toBeHidden();
	});

	test('the right-hand TOC indents nested headings', async ({ page }) => {
		await page.goto('/docs/import/');

		const nestedHeading = page.locator('starlight-toc a[href="#in-the-web-interface"]');
		await expect(nestedHeading).toContainText('In the web interface');
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

	test('the former Settings reference redirects to the CLI configuration guide', async ({ page }) => {
		await page.goto('/docs/reference/settings/');
		await expect(page).toHaveURL(/\/docs\/reference\/cli\/#configuration-and-delivery$/);
		await expect(page.getByRole('link', { name: 'Settings', exact: true })).toHaveCount(0);
	});

	test('Deliver combines recap choices with delivery setup', async ({ page }) => {
		await page.goto('/docs/deliver/');
		await expect(page.locator('main h1')).toContainText('Deliver');
		await expect(page.locator('.sl-markdown-content h2', { hasText: 'How the choice is made' })).toBeVisible();
		await expect(page.locator('.sl-markdown-content h2', { hasText: 'Choose a relay' })).toBeVisible();
	});

	test('Import makes the web interface primary and the CLI optional', async ({ page }) => {
		await page.goto('/docs/import/');

		await expect(page.locator('#your-readers-file')).toBeVisible();
		await expect(page.getByRole('heading', { name: 'Start the server' })).toBeVisible();
		await expect(page.getByRole('heading', { name: 'In the web interface' })).toBeVisible();
		await expect(page.getByRole('heading', { name: 'Prefer the command line?' })).toBeVisible();
		await expect(
			page.locator('.sl-markdown-content').getByText(
				'The web interface is the standard way to import.',
			),
		).toBeVisible();
		await expect(
			page.getByRole('link', { name: 'Using Docker with a reader' }),
		).toHaveAttribute('href', '/docs/reference/cli/#using-docker-with-a-reader');
	});

	test('the web interface carries first-time readers through their first recap', async ({ page }) => {
		await page.goto('/docs/round-trip/');
		await expect(page.getByRole('heading', { name: 'Your first recap' })).toBeVisible();
		await expect(page.getByText('Open Recaps and select Send recap now.')).toBeVisible();

		await page.goto('/docs/revisit/');
		await expect(page.getByRole('heading', { name: 'Send your first recap now' })).toBeVisible();
		await expect(page.getByText('Open Recaps in the web interface')).toBeVisible();
		await expect(page.getByText('relego recap trigger')).toHaveCount(0);
	});

	test('the recap sample follows the document heading hierarchy', async ({ page }) => {
		await page.goto('/docs/revisit/');
		await expect(
			page.locator('.starlight-aside').getByText(
				'Sample recap: Relego Daily Recap (2026-05-21 18:00)',
			),
		).toBeVisible();

		const results = await new AxeBuilder({ page }).analyze();
		expect(results.violations.filter((violation) => violation.id === 'heading-order')).toEqual([]);
	});

	test('advanced delivery and Docker setup live in the reference pages', async ({ page }) => {
		await page.goto('/docs/deliver/');
		await expect(page.getByRole('link', { name: 'smtp4dev demo profile' })).toHaveAttribute(
			'href',
			'/docs/reference/environment/#a-working-example',
		);
		await expect(page.getByRole('heading', { name: 'Try it without a relay first' })).toHaveCount(0);

		await page.goto('/docs/reference/cli/');
		await expect(page.getByRole('heading', { name: 'Using Docker with a reader' })).toBeVisible();
		await expect(page.getByRole('heading', { name: 'Configuration and delivery' })).toBeVisible();
		await expect(page.getByText('Integer from 1 to 15')).toBeVisible();
	});

	test('the landing page links into the docs', async ({ page }) => {
		await page.goto('/');
		await page.click('nav a:has-text("Docs")');
		await expect(page).toHaveURL(/\/docs\/$/);
	});

	test('docs illustrate the first-run path with web interface screenshots', async ({ page }) => {
		const screenshots = [
			['/docs/import/', '/images/docs/relego-web-import.webp', '/images/docs/relego-web-import-mobile.webp'],
			['/docs/deliver/', '/images/docs/relego-web-settings.webp', '/images/docs/relego-web-settings-mobile.webp'],
			['/docs/revisit/', '/images/docs/relego-web-recaps.webp', '/images/docs/relego-web-recaps-mobile.webp'],
		];

		for (const [path, image] of screenshots) {
			await page.goto(path);
			await expect(page.locator(`.sl-markdown-content img[src="${image}"]`)).toBeVisible();
		}

		await page.setViewportSize({ width: 390, height: 844 });
		for (const [path, image, mobileImage] of screenshots) {
			await page.goto(path);
			await expect(page.locator(`.sl-markdown-content img[src="${image}"]`)).toHaveCSS(
				'content',
				`url("http://localhost:4321${mobileImage}")`,
			);
		}

		await page.goto('/');
		await expect(page.locator('#web-interface img')).toHaveAttribute(
			'src',
			'/images/docs/relego-web-library.webp',
		);
		await expect(page.locator('#web-interface img')).toHaveJSProperty(
			'currentSrc',
			'http://localhost:4321/images/docs/relego-web-library-mobile.webp',
		);
		await expect(page.locator('img[src*="tui-demo"]')).toHaveCount(0);
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

		for (const path of ['/docs/', '/docs/reference/cli/']) {
			await page.goto(path);
			const results = await new AxeBuilder({ page }).analyze();
			const blocking = results.violations.filter(
				(violation) => violation.impact === 'critical' || violation.impact === 'serious',
			);
			expect(blocking, `${path}: ${blocking.map((v) => v.id).join(', ')}`).toEqual([]);
		}
	});

	test('CLI reference reflows into labeled rows on a phone without horizontal panning', async ({
		page,
	}) => {
		await page.setViewportSize({ width: 390, height: 844 });
		await page.goto('/docs/reference/cli/');

		const overflows = await page.evaluate(
			() => document.documentElement.scrollWidth > window.innerWidth + 1,
		);
		expect(overflows, 'the page itself must not scroll horizontally').toBe(false);

		const mobileTableLayout = await page.evaluate(() => {
			const table = document.querySelector('.sl-markdown-content table');
			const firstRow = table?.querySelector('tbody tr');
			const firstCell = table?.querySelector('tbody td');

			return {
				hasHorizontalScroll: table ? table.scrollWidth > table.clientWidth : false,
				rowDisplay: firstRow ? getComputedStyle(firstRow).display : null,
				firstLabel: firstCell
					? getComputedStyle(firstCell, '::before').content
					: null,
			};
		});
		expect(mobileTableLayout.hasHorizontalScroll).toBe(false);
		expect(mobileTableLayout.rowDisplay).toBe('block');
		expect(mobileTableLayout.firstLabel).toBe('"Command"');
	});

	test('compact mobile docs controls have comfortable touch targets', async ({ page }) => {
		await page.setViewportSize({ width: 390, height: 844 });
		await page.goto('/docs/revisit/');

		const controls = await page.evaluate(() =>
			[
				document.querySelector<HTMLElement>('header.header site-search button[data-open-modal]'),
				document.querySelector<HTMLElement>(
					'.sidebar-content ul.top-level > li > details > summary',
				),
			].map((element) => {
				const rect = element?.getBoundingClientRect();
				return { width: rect?.width ?? 0, height: rect?.height ?? 0 };
			}),
		);

		for (const control of controls) {
			expect(control.width).toBeGreaterThanOrEqual(44);
			expect(control.height).toBeGreaterThanOrEqual(44);
		}
	});
});
