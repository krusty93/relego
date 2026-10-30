import { test, expect } from '@playwright/test';

test('moving over the screen wipes a transparent trail through the mist', async ({ page }) => {
	await page.goto('/');
	const canvas = page.locator('.recap__mist');
	await expect(canvas).toBeVisible();
	const alpha = () => canvas.evaluate((el: HTMLCanvasElement) => {
		return el.getContext('2d')!.getImageData(Math.floor(el.width / 2), Math.floor(el.height / 2), 1, 1).data[3];
	});
	expect(await alpha()).toBeGreaterThan(230);
	await page.locator('#hero .recap').hover();
	await expect.poll(alpha).toBeLessThan(20);
});

test('keyboard controls reveal and re-mist the sample', async ({ page }) => {
	await page.goto('/');
	const canvas = page.locator('.recap__mist');
	await page.getByRole('button', { name: 'Reveal recap' }).focus();
	await page.keyboard.press('Enter');
	await expect(canvas).toHaveAttribute('data-misted', 'false');
	await page.getByRole('button', { name: 'Mist again' }).click();
	await expect(canvas).toHaveAttribute('data-misted', 'true');
});

test('reduced motion shows clear content and responds to a live preference change', async ({ page }) => {
	await page.goto('/');
	await expect(page.locator('.recap__mist')).toBeVisible();
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await expect(page.locator('.recap__mist')).toBeHidden();
	await expect(page.getByRole('button', { name: 'Reveal recap' })).toBeHidden();
	await expect(page.locator('.recap__quote').first()).toBeVisible();
	await page.emulateMedia({ reducedMotion: 'no-preference' });
	await expect(page.locator('.recap__mist')).toBeVisible();
});

test('touch taps wipe, controls work on mobile, and vertical scrolling remains available', async ({ browser }) => {
	const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
	const page = await context.newPage();
	await page.goto('/');
	await page.locator('#hero .recap').scrollIntoViewIfNeeded();
	await page.locator('#hero .recap').tap();
	const canvas = page.locator('.recap__mist');
	await expect.poll(() => canvas.evaluate((el: HTMLCanvasElement) =>
		el.getContext('2d')!.getImageData(Math.floor(el.width / 2), Math.floor(el.height / 2), 1, 1).data[3],
	)).toBeLessThan(20);
	await page.getByRole('button', { name: 'Reveal recap' }).tap();
	await expect(canvas).toHaveAttribute('data-misted', 'false');
	await expect(page.locator('#hero .recap')).toHaveCSS('touch-action', 'auto');
	await context.close();
});

test('without JavaScript no mist obscures the original reader', async ({ browser }) => {
	const context = await browser.newContext({ javaScriptEnabled: false });
	const page = await context.newPage();
	await page.goto('/');
	await expect(page.locator('.recap__mist')).toBeHidden();
	await expect(page.locator('.hero__reader')).toBeVisible();
	await context.close();
});
