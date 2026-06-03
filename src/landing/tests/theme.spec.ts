import { test, expect } from '@playwright/test';

test.describe('Theme Toggle', () => {
	test.beforeEach(async ({ page }) => {
		await page.emulateMedia({ colorScheme: 'light' });
		await page.goto('/');
	});

	test('clicking theme toggle switches data-theme from light to dark', async ({ page }) => {
		const html = page.locator('html');
		await expect(html).toHaveAttribute('data-theme', 'light');

		await page.click('#theme-toggle');
		await expect(html).toHaveAttribute('data-theme', 'dark');
	});

	test('clicking theme toggle twice returns to light', async ({ page }) => {
		await page.click('#theme-toggle');
		await page.click('#theme-toggle');

		const html = page.locator('html');
		await expect(html).toHaveAttribute('data-theme', 'light');
	});

	test('selected theme persists after page reload', async ({ page }) => {
		await page.click('#theme-toggle');
		await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

		await page.reload();

		await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
		const stored = await page.evaluate(() => localStorage.getItem('theme'));
		expect(stored).toBe('dark');
	});

	test('theme toggle button has accessible aria-label', async ({ page }) => {
		const toggle = page.locator('#theme-toggle');
		await expect(toggle).toHaveAttribute('aria-label', 'Toggle theme');
	});
});
