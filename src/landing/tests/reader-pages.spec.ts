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

test('sample pages work with keyboard, wrap, and retain focus', async ({ page }) => {
	await page.goto('/');
	const next = page.getByRole('button', { name: 'Next sample page' });
	await next.focus();
	await page.keyboard.press('Enter');
	await expect(page.getByRole('status')).toHaveText('Sample page 2 of 2');
	await expect(page.locator('[data-recap-page]').nth(1)).toBeVisible();
	await expect(next).toBeFocused();
	await page.keyboard.press('Enter');
	await expect(page.getByRole('status')).toHaveText('Sample page 1 of 2');
	await page.getByRole('button', { name: 'Previous sample page' }).click();
	await expect(page.getByRole('status')).toHaveText('Sample page 2 of 2');
});

test('reduced motion keeps pages usable without pointer tilt', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await page.goto('/');
	await page.mouse.move(1100, 300);
	await expect(page.locator('.hero__reader')).toHaveCSS('transform', 'none');
	await page.getByRole('button', { name: 'Next sample page' }).click();
	await expect(page.getByRole('status')).toHaveText('Sample page 2 of 2');
});

test('mobile page controls fit the viewport', async ({ page }) => {
	await page.setViewportSize({ width: 390, height: 844 });
	await page.goto('/');
	await page.getByRole('button', { name: 'Next sample page' }).click();
	await expect(page.getByRole('status')).toHaveText('Sample page 2 of 2');
	const bounds = await page.locator('.hero__page-controls').boundingBox();
	expect(bounds).not.toBeNull();
	expect(bounds!.x).toBeGreaterThanOrEqual(0);
	expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(390);
});
