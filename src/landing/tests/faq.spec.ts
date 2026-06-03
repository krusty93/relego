import { test, expect } from '@playwright/test';

test.describe('FAQ Section', () => {
	test.beforeEach(async ({ page }) => {
		await page.goto('/');
	});

	test('renders 6 FAQ accordion items with visible questions', async ({ page }) => {
		const items = page.locator('#faq details');
		await expect(items).toHaveCount(6);
		// All summaries should be visible
		for (let i = 0; i < 6; i++) {
			await expect(items.nth(i).locator('summary')).toBeVisible();
		}
	});

	test('clicking a collapsed FAQ item expands the answer', async ({ page }) => {
		const first = page.locator('#faq details').first();
		// Initially closed
		await expect(first).not.toHaveAttribute('open', '');
		const answer = first.locator('div');
		await expect(answer).toBeHidden();
		// Click to open
		await first.locator('summary').click();
		await expect(first).toHaveAttribute('open', '');
		await expect(answer).toBeVisible();
	});

	test('clicking an expanded FAQ item collapses the answer', async ({ page }) => {
		const first = page.locator('#faq details').first();
		// Open it
		await first.locator('summary').click();
		await expect(first).toHaveAttribute('open', '');
		// Close it
		await first.locator('summary').click();
		await expect(first).not.toHaveAttribute('open', '');
	});

	test('FAQ content addresses required topics', async ({ page }) => {
		const faq = page.locator('#faq');
		const text = await faq.textContent();
		expect(text).toMatch(/cloud|subscription/i);
		expect(text).toMatch(/SMTP/i);
		expect(text).toMatch(/frequency|how often/i);
		expect(text).toMatch(/Kindle/i);
		expect(text).toMatch(/data|stored/i);
		expect(text).toMatch(/exclude/i);
	});
});
