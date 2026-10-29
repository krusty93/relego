/**
 * One small scroll engine for the landing page.
 *
 * - [data-reveal]       gains .is-in the first time it enters the viewport.
 * - [data-progress]     receives --p (0..1) on every frame the page scrolls, and a
 *                       `relego:progress` event carrying the same value.
 *     "pin"     0 when the element's top hits the viewport top, 1 when its bottom
 *               reaches the viewport bottom (for sticky, scroll-pinned stages).
 *     "enter"   0 when the top enters at the bottom of the viewport, 1 once the top
 *               has climbed to 15% of the viewport height.
 *     "through" 0 when the top enters, 1 when the bottom leaves.
 *     "leave"   0 while the element sits at the top, 1 once three quarters of it has
 *               scrolled out above the viewport (for the hero).
 * - <html> receives --page-p and .is-scrolled for the navigation bar, and .is-night
 *   while a [data-night] band sits under the header.
 */

const clamp = (value: number) => Math.min(1, Math.max(0, value));

const root = document.documentElement;
const motionAllowed = root.classList.contains('motion');

function setupReveals() {
	const targets = document.querySelectorAll<HTMLElement>('[data-reveal]');
	if (!motionAllowed || !('IntersectionObserver' in window)) {
		targets.forEach((el) => el.classList.add('is-in'));
		return;
	}

	const observer = new IntersectionObserver(
		(entries) => {
			for (const entry of entries) {
				if (entry.isIntersecting) {
					entry.target.classList.add('is-in');
					observer.unobserve(entry.target);
				}
			}
		},
		{ rootMargin: '0px 0px -12% 0px', threshold: 0.01 },
	);

	targets.forEach((el) => observer.observe(el));
}

function measure(el: HTMLElement, mode: string, vh: number) {
	const rect = el.getBoundingClientRect();
	switch (mode) {
		case 'pin':
			return clamp(-rect.top / Math.max(1, rect.height - vh));
		case 'through':
			return clamp((vh - rect.top) / Math.max(1, rect.height + vh));
		case 'leave':
			return clamp(-rect.top / Math.max(1, rect.height * 0.75));
		default:
			return clamp((vh - rect.top) / Math.max(1, vh * 0.85));
	}
}

function setupProgress() {
	const tracked = Array.from(document.querySelectorAll<HTMLElement>('[data-progress]'));
	const nights = Array.from(document.querySelectorAll<HTMLElement>('[data-night]'));
	let queued = false;

	const update = () => {
		queued = false;
		const vh = window.innerHeight;
		const max = Math.max(1, root.scrollHeight - vh);
		root.style.setProperty('--page-p', clamp(window.scrollY / max).toFixed(4));
		root.classList.toggle('is-scrolled', window.scrollY > 8);
		root.classList.toggle(
			'is-night',
			nights.some((el) => {
				const rect = el.getBoundingClientRect();
				return rect.top <= 32 && rect.bottom >= 32;
			}),
		);

		for (const el of tracked) {
			const p = measure(el, el.dataset.progress ?? 'enter', vh);
			const value = p.toFixed(4);
			if (el.style.getPropertyValue('--p') !== value) {
				el.style.setProperty('--p', value);
				el.dispatchEvent(new CustomEvent('relego:progress', { detail: p }));
			}
		}
	};

	const queue = () => {
		if (!queued) {
			queued = true;
			requestAnimationFrame(update);
		}
	};

	if (!motionAllowed) {
		// Static page: keep the nav state, never drive scroll-linked styles.
		tracked.length = 0;
	}

	window.addEventListener('scroll', queue, { passive: true });
	window.addEventListener('resize', queue);
	update();
}

setupReveals();
setupProgress();
