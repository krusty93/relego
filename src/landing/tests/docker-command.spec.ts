import { test, expect } from '@playwright/test';

const command = 'docker compose --profile app up -d';

test('the hero and closing command both copy the exact Docker command', async ({ page }) => {
	await page.addInitScript(() => {
		Object.defineProperty(navigator, 'clipboard', {
			value: {
				writeText: async (text: string) => {
					document.documentElement.setAttribute('data-copied-command', text);
				},
			},
		});
	});
	await page.goto('/');
	await expect(page.locator('#hero .hero__actions + .hero__command code')).toContainText(command);
	for (const section of ['#hero', '#explore']) {
		const container = page.locator(`${section} [data-docker-command]`);
		await expect(container.locator('code')).toContainText(command);
		await page.locator('html').evaluate((el) => el.removeAttribute('data-copied-command'));
		await container.getByRole('button', { name: 'Copy command' }).click();
		await expect(page.locator('html')).toHaveAttribute('data-copied-command', command);
		await expect(container.locator('[data-copy-status]')).toHaveText('Command copied');
	}
});

test('clipboard failure is visible, actionable, and can be retried', async ({ page }) => {
	await page.addInitScript(() => {
		let attempts = 0;
		Object.defineProperty(navigator, 'clipboard', {
			value: {
				writeText: async () => {
					if (attempts++ === 0) throw new Error('Clipboard unavailable');
				},
			},
		});
	});
	await page.goto('/');
	const container = page.locator('#hero [data-docker-command]');
	const button = container.getByRole('button', { name: 'Copy command' });
	const status = container.locator('[data-copy-status]');
	await button.click();
	await expect(status).toHaveText('Copy failed. Select the command and copy it manually.');
	await expect(status).not.toHaveClass(/sr-only/);
	await expect(button).toBeFocused();
	await button.click();
	await expect(status).toHaveText('Command copied');
	await expect(status).toHaveClass(/sr-only/);
});

test('without JavaScript the commands remain readable without dead copy controls', async ({ browser }) => {
	const context = await browser.newContext({ javaScriptEnabled: false });
	const page = await context.newPage();
	await page.goto('/');
	for (const section of ['#hero', '#explore']) {
		const container = page.locator(`${section} [data-docker-command]`);
		await expect(container.locator('code')).toContainText(command);
		await expect(container.locator('[data-copy]')).toBeHidden();
	}
	await expect(page.locator('.nav__logo')).toBeVisible();
	await expect(page.locator('.hero__page-controls')).toBeHidden();
	await expect(page.locator('[data-recap-page]').first()).toBeVisible();
	await context.close();
});
