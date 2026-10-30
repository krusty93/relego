import { test, expect } from '@playwright/test';

test('letter reader renders and responds to pointer and keyboard pulse', async ({ page }) => {
	await page.goto('/');
	const stage = page.locator('.hero__glyph-stage');
	await expect(stage).toHaveClass(/is-ready/);
	expect(Number(await stage.getAttribute('data-glyph-count'))).toBeGreaterThan(2000);
	const canvas = stage.locator('canvas');
	const before = await canvas.evaluate((el: HTMLCanvasElement) => el.toDataURL());
	await stage.hover({ position: { x: 100, y: 120 } });
	await expect.poll(() => canvas.evaluate((el: HTMLCanvasElement) => el.toDataURL())).not.toBe(before);
	await page.getByRole('button', { name: 'Scatter the letters' }).focus();
	await page.keyboard.press('Enter');
	await expect.poll(() => canvas.evaluate((el: HTMLCanvasElement) => el.toDataURL())).not.toBe(before);
});

test('reduced motion presents a static letter reader', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await page.goto('/');
	const canvas = page.locator('.hero__glyphs');
	await expect(canvas).toBeVisible();
	const before = await canvas.evaluate((el: HTMLCanvasElement) => el.toDataURL());
	await page.locator('.hero__glyph-stage').hover();
	await expect(page.getByRole('button', { name: 'Scatter the letters' })).toBeHidden();
	expect(await canvas.evaluate((el: HTMLCanvasElement) => el.toDataURL())).toBe(before);
});

test('mobile can scatter with a tap and themes repaint the letters', async ({ page }) => {
	await page.setViewportSize({ width: 390, height: 844 });
	await page.goto('/');
	await page.getByRole('button', { name: 'Scatter the letters' }).click();
	const canvas = page.locator('.hero__glyphs');
	await page.emulateMedia({ reducedMotion: 'reduce' });
	const light = await canvas.evaluate((el: HTMLCanvasElement) => el.toDataURL());
	await page.evaluate(() => { document.documentElement.dataset.theme = 'dark'; });
	await expect.poll(() => canvas.evaluate((el: HTMLCanvasElement) => el.toDataURL())).not.toBe(light);
});

test('without JavaScript the original reader remains visible', async ({ browser }) => {
	const context = await browser.newContext({ javaScriptEnabled: false });
	const page = await context.newPage();
	await page.goto('/');
	await expect(page.locator('.hero__reader')).toBeVisible();
	await expect(page.locator('.hero__glyphs')).toBeHidden();
	await context.close();
});
