import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
	testDir: './tests',
	retries: process.env.CI ? 2 : 0,
	use: {
		baseURL: 'http://localhost:4321/',
		screenshot: 'only-on-failure',
	},
	projects: [
		{
			name: 'chromium',
			use: {
				...devices['Desktop Chrome'],
			},
		},
	],
	webServer: {
		command: 'pnpm run dev',
		url: 'http://localhost:4321/',
		reuseExistingServer: !process.env.CI,
		timeout: 120_000,
	},
});
