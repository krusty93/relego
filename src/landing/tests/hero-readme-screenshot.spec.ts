import { test, expect } from '@playwright/test';

test('capture dark hero section for README', async ({ page }) => {
	await page.addInitScript(() => {
		window.localStorage.setItem('theme', 'dark');
	});
	await page.emulateMedia({ colorScheme: 'dark' });
	await page.setViewportSize({ width: 1600, height: 1000 });

	await page.goto('/');
	await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

	// Ensure the hero background image has finished loading.
	await page.waitForFunction(() => {
		const img = document.querySelector('#hero img') as HTMLImageElement | null;
		return !!img && img.complete && img.naturalWidth > 0;
	});

	// Hide fixed navbar to keep only the hero composition in the exported image.
	await page.addStyleTag({
		content: 'nav { display: none !important; } #hero a[href="#getting-started"] { display: none !important; }',
	});

	const hero = page.locator('#hero');
	await expect(hero).toBeVisible();
	await hero.screenshot({
		path: test.info().outputPath('landing-hero-dark.jpg'),
		type: 'jpeg',
		quality: 88,
		animations: 'disabled',
	});
});
