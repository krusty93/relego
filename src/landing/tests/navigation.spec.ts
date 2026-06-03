import { test, expect } from '@playwright/test';

test.describe('Navigation', () => {
	test.beforeEach(async ({ page }) => {
		await page.goto('/');
	});

	test('navbar renders with "relego." logotype text', async ({ page }) => {
		const logotype = page.locator('nav a').first();
		await expect(logotype).toHaveText('relego.');
	});

	test('"View on GitHub" link points to configured GitHub URL', async ({ page }) => {
		const githubLink = page.locator('nav a[href*="github.com"]').first();
		await expect(githubLink).toBeVisible();
		await expect(githubLink).toHaveAttribute('href', 'https://github.com/Krusty93/relego');
	});

	test('"View on GitHub" link opens in a new tab', async ({ page }) => {
		const githubLink = page.locator('nav a[href*="github.com"]').first();
		await expect(githubLink).toHaveAttribute('target', '_blank');
		await expect(githubLink).toHaveAttribute('rel', /noopener/);
	});

	test('clicking logotype navigates to top of page', async ({ page }) => {
		const logotype = page.locator('nav a').first();
		await expect(logotype).toHaveAttribute('href', '#');
	});

	test('footer renders with "Relego · MIT License"', async ({ page }) => {
		const footer = page.locator('footer');
		await expect(footer).toBeVisible();
		await expect(footer).toContainText('Relego · MIT License');
	});

	test('hero section renders logotype, tagline, and CTA', async ({ page }) => {
		const hero = page.locator('#hero');
		await expect(hero.locator('h1')).toContainText('relego.');
		await expect(hero.locator('p')).toContainText('Kindle');
		await expect(hero.locator('a:has-text("Get started")')).toBeVisible();
	});

	test('clicking "Get started" CTA scrolls to Getting Started section', async ({ page }) => {
		const cta = page.locator('#hero a:has-text("Get started")');
		await cta.click();
		const section = page.locator('#getting-started');
		await expect(section).toBeInViewport({ timeout: 3000 });
	});

	test('Explore section renders "View on GitHub" and "Read the docs" buttons', async ({ page }) => {
		const explore = page.locator('#explore');
		await expect(explore.locator('a:has-text("View on GitHub")')).toBeVisible();
		await expect(explore.locator('a:has-text("Read the docs")')).toBeVisible();
	});

	test('Explore "View on GitHub" links to the configured GitHub URL', async ({ page }) => {
		const link = page.locator('#explore a:has-text("View on GitHub")');
		await expect(link).toHaveAttribute('href', 'https://github.com/Krusty93/relego');
		await expect(link).toHaveAttribute('target', '_blank');
	});

	test('Explore section shows Contribute link', async ({ page }) => {
		const link = page.locator('#explore a:has-text("Contribute")');
		await expect(link).toBeVisible();
		await expect(link).toHaveAttribute('href', 'https://github.com/Krusty93/relego/blob/main/CONTRIBUTING.md');
		await expect(link).toHaveAttribute('target', '_blank');
	});
});
