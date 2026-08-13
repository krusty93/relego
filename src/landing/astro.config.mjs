// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import mdx from '@astrojs/mdx';
import tailwindcss from '@tailwindcss/vite';

// https://astro.build/config
export default defineConfig({
	site: 'https://relego.app/',
	srcDir: '.',
	// Capture and Import are one stop now. Select is now the Deliver page.
	redirects: {
		'/docs/store/': '/docs/import/',
		'/docs/capture/': '/docs/import/',
		'/docs/select/': '/docs/deliver/',
		'/docs/reference/settings/': '/docs/reference/cli/#configuration-and-delivery',
	},
	integrations: [
		starlight({
			title: 'Relego docs',
			description:
				'Set up Relego and follow one highlight from your reader back to your reader.',
			// Docs live in `content/docs/docs/**`, which routes every page under `/docs`.
			customCss: ['./styles/docs.css'],
			head: [
				{
					tag: 'link',
					attrs: { rel: 'preconnect', href: 'https://fonts.googleapis.com' },
				},
				{
					tag: 'link',
					attrs: {
						rel: 'preconnect',
						href: 'https://fonts.gstatic.com',
						crossorigin: 'anonymous',
					},
				},
				{
					tag: 'link',
					attrs: {
						rel: 'stylesheet',
						href: 'https://fonts.googleapis.com/css2?family=Playfair+Display:wght@300&display=swap',
					},
				},
				{
					// Keep /docs on the same light/dark choice the landing page stores, and
					// make overflowing reference content keyboard reachable.
					tag: 'script',
					content:
						"(()=>{const g=k=>{try{return localStorage.getItem(k)}catch{return null}},s=(k,v)=>{try{localStorage.setItem(k,v)}catch{}};const shared=g('theme');if(shared==='dark'||shared==='light'){s('starlight-theme',shared);document.documentElement.dataset.theme=shared}document.documentElement.classList.toggle('relego-cli-reference',location.pathname==='/docs/reference/cli/');new MutationObserver(()=>{const t=document.documentElement.dataset.theme;if(t==='dark'||t==='light')s('theme',t)}).observe(document.documentElement,{attributes:true,attributeFilter:['data-theme']});const sync=()=>{for(const e of document.querySelectorAll('.sl-markdown-content table,.expressive-code .frame pre')){if(e.scrollWidth>e.clientWidth){e.setAttribute('tabindex','0')}else{e.removeAttribute('tabindex')}}};addEventListener('DOMContentLoaded',sync);addEventListener('resize',sync)})();",
				},
			],
			components: {
				SiteTitle: './docs-ui/SiteTitle.astro',
				PageTitle: './docs-ui/PageTitle.astro',
				Head: './docs-ui/Head.astro',
			},
			social: [
				{
					icon: 'github',
					label: 'GitHub',
					href: 'https://github.com/Krusty93/relego',
				},
			],
			editLink: {
				baseUrl:
					'https://github.com/Krusty93/relego/edit/main/src/landing/content/docs/',
			},
			lastUpdated: true,
			// The landing site owns the site-wide 404 (pages/404.astro).
			disable404Route: true,
			sidebar: [
				{ label: 'Introduction', link: '/docs/' },
				{
					label: 'The round trip',
					items: [
						{ label: 'Overview', link: '/docs/round-trip/' },
						{ label: '1 · Import highlights', link: '/docs/import/' },
						{ label: '2 · Deliver', link: '/docs/deliver/' },
						{ label: '3 · Revisit', link: '/docs/revisit/' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'CLI commands', link: '/docs/reference/cli/' },
						{ label: 'Environment variables', link: '/docs/reference/environment/' },
						{ label: 'Troubleshooting', link: '/docs/reference/troubleshooting/' },
						{ label: 'Verifying releases', link: '/docs/reference/verifying-releases/' },
					],
				},
			],
		}),
		mdx(),
	],
	vite: {
		plugins: [tailwindcss()],
	},
});
