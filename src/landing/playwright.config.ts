import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
	testDir: './tests',
	retries: process.env.CI ? 2 : 0,
	use: {
		baseURL: 'http://localhost:4321/relego/',
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
		command: 'npm run dev -- --host 0.0.0.0 --port 4321 --base /relego/',
		url: 'http://localhost:4321/relego/',
		reuseExistingServer: !process.env.CI,
		timeout: 120_000,
	},
});
