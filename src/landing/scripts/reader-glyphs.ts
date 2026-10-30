interface Glyph {
	x: number;
	y: number;
	dx: number;
	dy: number;
	vx: number;
	vy: number;
	letter: string;
	shade: number;
}

const stage = document.querySelector<HTMLElement>('.hero__glyph-stage');
const canvas = stage?.querySelector<HTMLCanvasElement>('canvas');
const button = document.querySelector<HTMLButtonElement>('.hero__scatter');
const context = canvas?.getContext('2d');

if (stage && canvas && context && button) {
	const reduced = matchMedia('(prefers-reduced-motion: reduce)');
	const fine = matchMedia('(hover: hover) and (pointer: fine)');
	const mask = document.createElement('canvas');
	mask.width = 300;
	mask.height = 420;
	const ink = mask.getContext('2d');
	if (!ink) throw new Error('Unable to prepare the reader letter silhouette.');
	ink.fillStyle = '#211a16';
	ink.beginPath();
	ink.roundRect(16, 8, 268, 392, 20);
	ink.fill();
	ink.clearRect(34, 30, 232, 320);
	ink.fillStyle = '#211a16';
	ink.font = '18px Georgia';
	ink.fillText('relego.', 48, 62);
	ink.font = '23px Georgia';
	ink.fillText('Read again.', 48, 105);
	ink.fillRect(48, 120, 122, 7);
	for (let row = 0; row < 20; row++) {
		if (row === 7 || row === 8) continue;
		ink.globalAlpha = row < 7 ? 0.85 : 0.5;
		ink.fillRect(48, 143 + row * 8, row % 5 === 4 ? 106 : 200, 3);
	}
	ink.globalAlpha = 1;
	const pixels = ink.getImageData(0, 0, 300, 420).data;
	const letters = 'relegoreadremember';
	const glyphs: Glyph[] = [];
	for (let y = 0; y < 420; y += 4) {
		for (let x = 0; x < 300; x += 4) {
			const alpha = pixels[(y * 300 + x) * 4 + 3] / 255;
			if (alpha > 0.1) glyphs.push({
				x: x / 300, y: y / 420, dx: 0, dy: 0, vx: 0, vy: 0,
				letter: letters[(x * 3 + y * 7) % letters.length], shade: alpha,
			});
		}
	}
	let width = 0;
	let height = 0;
	let frame = 0;
	let visible = true;
	let pointer: { x: number; y: number } | null = null;
	let color = '';
	let lastTime = 0;

	const draw = (time = 0) => {
		frame = 0;
		const step = Math.min(2, Math.max(0.5, (time - lastTime) / 16.67 || 1));
		lastTime = time;
		context.clearRect(0, 0, width, height);
		const scale = Math.min((width - 48) / 300, (height - 55) / 420);
		const left = (width - 300 * scale) / 2;
		const top = (height - 420 * scale) / 2 - 8;
		context.fillStyle = color;
		context.font = `${Math.max(4, 4.5 * scale)}px ui-monospace, monospace`;
		context.textBaseline = 'middle';
		let moving = false;
		for (const glyph of glyphs) {
			const homeX = left + glyph.x * 300 * scale;
			const homeY = top + glyph.y * 420 * scale;
			if (!reduced.matches) {
				if (pointer) {
					const px = homeX + glyph.dx - pointer.x;
					const py = homeY + glyph.dy - pointer.y;
					const distance = Math.hypot(px, py);
					if (distance < 70) {
						const force = (1 - distance / 70) * 2.8 * step;
						glyph.vx += px / Math.max(distance, 1) * force;
						glyph.vy += py / Math.max(distance, 1) * force;
					}
				}
				glyph.vx = (glyph.vx - glyph.dx * 0.035 * step) * Math.pow(0.82, step);
				glyph.vy = (glyph.vy - glyph.dy * 0.035 * step) * Math.pow(0.82, step);
				glyph.dx += glyph.vx * step;
				glyph.dy += glyph.vy * step;
			}
			moving ||= Math.abs(glyph.vx) + Math.abs(glyph.vy)
				+ (pointer ? 0 : Math.abs(glyph.dx) + Math.abs(glyph.dy)) > 0.1;
			context.globalAlpha = glyph.shade;
			context.fillText(glyph.letter, homeX + glyph.dx, homeY + glyph.dy);
		}
		context.globalAlpha = 1;
		context.font = '14px Georgia';
		context.textAlign = 'center';
		context.fillText('Every letter, read again.', width / 2, height - 26);
		context.textAlign = 'start';
		if (moving && visible && !document.hidden && !reduced.matches) frame = requestAnimationFrame(draw);
	};
	const queue = () => {
		if (!frame && visible && !document.hidden) {
			lastTime = performance.now();
			frame = requestAnimationFrame(draw);
		}
	};
	const reset = () => {
		pointer = null;
		cancelAnimationFrame(frame);
		frame = 0;
		glyphs.forEach((g) => { g.dx = g.dy = g.vx = g.vy = 0; });
		button.hidden = reduced.matches;
		draw();
	};
	const resize = () => {
		const rect = canvas.getBoundingClientRect();
		width = rect.width;
		height = rect.height;
		const ratio = Math.min(devicePixelRatio || 1, 2);
		canvas.width = Math.round(width * ratio);
		canvas.height = Math.round(height * ratio);
		context.setTransform(ratio, 0, 0, ratio, 0, 0);
		color = getComputedStyle(stage).getPropertyValue('--color-text').trim();
		reset();
	};

	canvas.hidden = false;
	stage.classList.add('is-ready');
	stage.dataset.glyphCount = String(glyphs.length);
	new ResizeObserver(resize).observe(stage);
	new MutationObserver(resize).observe(document.documentElement, {
		attributes: true, attributeFilter: ['data-theme'],
	});
	new IntersectionObserver(([entry]) => {
		visible = entry.isIntersecting;
		if (!visible) reset();
	}).observe(stage);
	stage.addEventListener('pointermove', (event) => {
		if (reduced.matches || !fine.matches || event.pointerType !== 'mouse') return;
		const rect = canvas.getBoundingClientRect();
		pointer = { x: event.clientX - rect.left, y: event.clientY - rect.top };
		queue();
	}, { passive: true });
	stage.addEventListener('pointerleave', () => { pointer = null; queue(); });
	button.addEventListener('click', () => {
		if (reduced.matches) return;
		pointer = null;
		glyphs.forEach((g, index) => {
			const angle = index * 2.39996;
			g.vx = Math.cos(angle) * 15;
			g.vy = Math.sin(angle) * 15;
		});
		queue();
	});
	reduced.addEventListener('change', reset);
	fine.addEventListener('change', reset);
	document.addEventListener('visibilitychange', reset);
	resize();
}
