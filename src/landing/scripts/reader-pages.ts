const hero = document.querySelector<HTMLElement>('#hero');
const reader = hero?.querySelector<HTMLElement>('.hero__reader');
const controls = hero?.querySelector<HTMLElement>('.hero__page-controls');
const status = hero?.querySelector<HTMLElement>('[data-page-status]');
const pages = Array.from(hero?.querySelectorAll<HTMLElement>('[data-recap-page]') ?? []);

if (hero && reader && controls && status && pages.length) {
	const reduced = matchMedia('(prefers-reduced-motion: reduce)');
	const fine = matchMedia('(hover: hover) and (pointer: fine)');
	let page = 0;
	let frame = 0;
	let x = 0;
	let y = 0;
	let targetX = 0;
	let targetY = 0;
	let refresh: Animation | undefined;

	controls.hidden = false;
	controls.querySelectorAll<HTMLButtonElement>('[data-page-direction]').forEach((button) => {
		button.addEventListener('click', () => {
			page = (page + Number(button.dataset.pageDirection) + pages.length) % pages.length;
			pages.forEach((element, index) => { element.hidden = index !== page; });
			status.textContent = `Page ${page + 1} of ${pages.length}`;
			refresh?.cancel();
			if (!reduced.matches) {
				// A brief e-ink refresh, rather than a paper curl on a digital screen.
				refresh = pages[page].animate(
					[{ opacity: 0.25, transform: 'translateX(6px)' }, { opacity: 1, transform: 'translateX(0)' }],
					{ duration: 360, easing: 'cubic-bezier(0.16, 1, 0.3, 1)' },
				);
			}
		});
	});

	const draw = () => {
		x += (targetX - x) * 0.12;
		y += (targetY - y) * 0.12;
		reader.style.setProperty('--reader-x', x.toFixed(4));
		reader.style.setProperty('--reader-y', y.toFixed(4));
		frame = Math.abs(targetX - x) + Math.abs(targetY - y) > 0.002
			? requestAnimationFrame(draw) : 0;
	};
	const queue = () => { if (!frame) frame = requestAnimationFrame(draw); };
	const reset = () => { targetX = 0; targetY = 0; queue(); };
	const stop = () => {
		cancelAnimationFrame(frame);
		frame = 0;
		x = y = targetX = targetY = 0;
		reader.style.removeProperty('--reader-x');
		reader.style.removeProperty('--reader-y');
		refresh?.cancel();
	};
	const hold = () => {
		cancelAnimationFrame(frame);
		frame = 0;
		targetX = x;
		targetY = y;
	};

	controls.addEventListener('pointerenter', hold);
	controls.addEventListener('focusin', hold);
	hero.addEventListener('pointermove', (event) => {
		if (reduced.matches || !fine.matches || event.pointerType !== 'mouse') return;
		if (controls.matches(':hover') || controls.contains(document.activeElement)) return;
		const rect = hero.getBoundingClientRect();
		targetX = Math.max(-1, Math.min(1, (event.clientX - rect.left) / rect.width * 2 - 1));
		targetY = Math.max(-1, Math.min(1, (event.clientY - rect.top) / rect.height * 2 - 1));
		queue();
	}, { passive: true });
	hero.addEventListener('pointerleave', reset);
	reduced.addEventListener('change', stop);
	fine.addEventListener('change', stop);
	document.addEventListener('visibilitychange', () => { if (document.hidden) stop(); });
	new IntersectionObserver(([entry]) => { if (!entry.isIntersecting) stop(); }).observe(hero);
}
export {};
