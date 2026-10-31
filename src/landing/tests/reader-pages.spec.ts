import { test, expect } from '@playwright/test';

test('reader follows the mouse and settles back on leave', async ({ page }) => {
	await page.goto('/');
	const reader = page.locator('.hero__reader');
	await page.mouse.move(1100, 300);
	await expect.poll(() => reader.evaluate((el) => Number((el as HTMLElement).style.getPropertyValue('--reader-x')))).toBeGreaterThan(0.4);
	await page.mouse.move(0, 0);
	await page.locator('#journey').scrollIntoViewIfNeeded();
	await expect.poll(() => reader.evaluate((el) => (el as HTMLElement).style.getPropertyValue('--reader-x'))).toBe('');
});

test('reader pages work with keyboard, wrap, and retain focus', async ({ page }) => {
	await page.goto('/');
	const reader = page.getByRole('figure', { name: 'Recap preview' });
	const next = reader.getByRole('button', { name: 'Next recap page' });
	const status = reader.getByRole('status');
	await expect(reader.locator('.reader__screen .hero__page-controls')).toBeVisible();
	await expect(page.getByText('Sample recap', { exact: true })).toHaveCount(0);
	await next.focus();
	await page.keyboard.press('Enter');
	await expect(status).toHaveText('Page 2 of 2');
	await expect(page.locator('[data-recap-page]').nth(1)).toBeVisible();
	await expect(next).toBeFocused();
	await page.keyboard.press('Enter');
	await expect(status).toHaveText('Page 1 of 2');
	await reader.getByRole('button', { name: 'Previous recap page' }).click();
	await expect(status).toHaveText('Page 2 of 2');
});

test('reduced motion keeps pages usable without pointer tilt', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await page.goto('/');
	await page.mouse.move(1100, 300);
	await expect(page.locator('.hero__reader')).toHaveCSS('transform', 'none');
	await page.getByRole('button', { name: 'Next recap page' }).click();
	await expect(page.locator('[data-page-status]')).toHaveText('Page 2 of 2');
});

test('focused page controls hold the reader still while the mouse moves', async ({ page }) => {
	await page.goto('/');
	const reader = page.locator('.hero__reader');
	await page.mouse.move(1100, 300);
	await expect.poll(async () => Number(await reader.evaluate((el) =>
		el.style.getPropertyValue('--reader-x'),
	))).toBeGreaterThan(0.4);
	await page.getByRole('button', { name: 'Next recap page' }).focus();
	const position = await reader.evaluate((el) => el.style.getPropertyValue('--reader-x'));
	await page.mouse.move(150, 150);
	await page.waitForTimeout(100);
	expect(await reader.evaluate((el) => el.style.getPropertyValue('--reader-x'))).toBe(position);
});

for (const width of [320, 390]) {
	test(`touch page controls stay inside the reader at ${width}px`, async ({ browser }) => {
		const context = await browser.newContext({
			viewport: { width, height: 844 },
			isMobile: true,
			hasTouch: true,
		});
		const page = await context.newPage();
		await page.goto('/');
		await page.getByRole('button', { name: 'Next recap page' }).tap();
		await expect(page.locator('[data-page-status]')).toHaveText('Page 2 of 2');
		await page.evaluate(() => Promise.all(document.getAnimations()
			.filter((animation) => animation.effect?.getComputedTiming().endTime !== Infinity)
			.map((animation) => animation.finished)));
		const { controls, screen } = await page.locator('.hero__reader').evaluate((reader) => {
			const bounds = (selector: string) => {
				const element = reader.querySelector(selector);
				if (!element) throw new Error(`Missing reader element: ${selector}`);
				const { x, y, width, height } = element.getBoundingClientRect();
				return { x, y, width, height };
			};
			return { controls: bounds('.hero__page-controls'), screen: bounds('.reader__screen') };
		});
		expect(controls.x).toBeGreaterThanOrEqual(screen.x);
		expect(controls.y).toBeGreaterThanOrEqual(screen.y);
		expect(controls.x + controls.width).toBeLessThanOrEqual(screen.x + screen.width);
		expect(controls.y + controls.height).toBeLessThanOrEqual(screen.y + screen.height - 4);
		await page.getByRole('button', { name: 'Previous recap page' }).tap();
		const content = page.locator('[data-recap-page]').first();
		expect(await content.evaluate((el) => el.scrollHeight - el.clientHeight)).toBeLessThanOrEqual(2);
		await context.close();
	});
}

test('hero and journey readers use the same brand-yellow highlighter in both themes', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await page.goto('/');
	const marks = page.locator('.recap__mark, .page__mark');
	await expect(marks).toHaveCount(4);
	for (const theme of ['light', 'dark']) {
		await page.evaluate((theme) => { document.documentElement.dataset.theme = theme; }, theme);
		const backgrounds = await marks.evaluateAll((elements) =>
			elements.map((el) => getComputedStyle(el).backgroundImage),
		);
		for (const background of backgrounds) expect(background).toContain('rgb(212, 160, 94)');
	}
});
