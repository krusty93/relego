export interface SiteConfig {
	name: string;
	logotype: string;
	tagline: string;
	githubUrl: string;
	docsUrl: string;
	docsImportUrl: string;
	extensionUrl: string;
	extensionFirefoxUrl: string;
	extensionDocsUrl: string;
	license: string;
	licenseUrl: string;
	contributingUrl: string;
}

export const siteConfig = {
	name: 'Relego',
	logotype: 'relego.',
	tagline: 'Revisit your highlights, delivered to your eReader. For free.',
	githubUrl: 'https://github.com/Krusty93/relego',
	docsUrl: '/docs/',
	docsImportUrl: '/docs/import/',
	extensionUrl: 'https://chromewebstore.google.com/detail/relego/nlpcdgfkcoiiighmhemlpkjkcnoojdhp',
	extensionFirefoxUrl: 'https://addons.mozilla.org/en-US/firefox/addon/relego/',
	extensionDocsUrl: '/docs/reference/kindle-extension/',
	license: 'MIT',
	licenseUrl: 'https://github.com/Krusty93/relego/blob/main/LICENSE',
	contributingUrl: 'https://github.com/Krusty93/relego/blob/main/CONTRIBUTING.md',
} as const satisfies SiteConfig;
