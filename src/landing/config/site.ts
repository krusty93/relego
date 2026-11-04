export interface SiteConfig {
	name: string;
	logotype: string;
	tagline: string;
	githubUrl: string;
	docsUrl: string;
	docsImportUrl: string;
	extensionUrl: string;
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
	extensionUrl: 'https://github.com/Krusty93/relego/releases',
	license: 'MIT',
	licenseUrl: 'https://github.com/Krusty93/relego/blob/main/LICENSE',
	contributingUrl: 'https://github.com/Krusty93/relego/blob/main/CONTRIBUTING.md',
} as const satisfies SiteConfig;
