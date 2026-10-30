const screen = document.querySelector<HTMLElement>('#hero .recap');
const canvas = screen?.querySelector<HTMLCanvasElement>('.recap__mist');
const controls = document.querySelector<HTMLElement>('.hero__mist-controls');
const context = canvas?.getContext('2d');

if (screen && canvas && controls && context) {
	const reduced = matchMedia('(prefers-reduced-motion: reduce)');
	let width = 0;
	let height = 0;
	let frame = 0;
	let previous: { x: number; y: number } | null = null;
	let pending: { x: number; y: number; radius: number } | null = null;
	let revealed = false;
	let visible = true;

	const cancel = () => {
		cancelAnimationFrame(frame);
		frame = 0;
		pending = previous = null;
	};
	const fog = () => {
		cancel();
		revealed = false;
		canvas.dataset.misted = 'true';
		context.globalCompositeOperation = 'source-over';
		context.clearRect(0, 0, width, height);
		const wash = context.createLinearGradient(0, 0, width, height);
		wash.addColorStop(0, 'rgba(225, 222, 209, 0.97)');
		wash.addColorStop(0.5, 'rgba(240, 235, 222, 0.94)');
		wash.addColorStop(1, 'rgba(203, 198, 185, 0.97)');
		context.fillStyle = wash;
		context.fillRect(0, 0, width, height);

		// A fixed seed keeps droplets stable between responsive redraws.
		let seed = 93;
		const random = () => {
			seed = (seed * 1664525 + 1013904223) >>> 0;
			return seed / 4294967296;
		};
		for (let i = 0; i < 380; i++) {
			const x = random() * width;
			const y = random() * height;
			const radius = 0.6 + random() * 2.8;
			const drop = context.createRadialGradient(x - radius * 0.3, y - radius * 0.4, 0, x, y, radius);
			drop.addColorStop(0, 'rgba(255, 252, 241, 0.8)');
			drop.addColorStop(0.45, 'rgba(243, 238, 223, 0.1)');
			drop.addColorStop(1, 'rgba(95, 83, 65, 0.22)');
			context.fillStyle = drop;
			context.beginPath();
			context.ellipse(x, y, radius, radius * 1.18, -0.3, 0, Math.PI * 2);
			context.fill();
		}
		context.fillStyle = '#6c6158';
		context.font = 'italic 21px Georgia';
		context.textAlign = 'center';
		context.fillText('Your words,', width / 2, height * 0.46);
		context.fillText('waiting.', width / 2, height * 0.46 + 28);
	};
	const clear = () => {
		cancel();
		revealed = true;
		canvas.dataset.misted = 'false';
		context.clearRect(0, 0, width, height);
	};
	const resize = () => {
		cancel();
		canvas.hidden = reduced.matches;
		controls.hidden = reduced.matches;
		if (reduced.matches) return;
		width = screen.clientWidth;
		height = screen.clientHeight;
		const ratio = Math.min(devicePixelRatio || 1, 2);
		canvas.width = Math.round(width * ratio);
		canvas.height = Math.round(height * ratio);
		context.setTransform(ratio, 0, 0, ratio, 0, 0);
		if (revealed) clear();
		else fog();
	};
	const wipe = () => {
		frame = 0;
		if (!pending) return;
		const point = pending;
		pending = null;
		const from = previous ?? point;
		const distance = Math.hypot(point.x - from.x, point.y - from.y);
		const steps = Math.max(1, Math.ceil(distance / 8));
		context.globalCompositeOperation = 'destination-out';
		for (let i = 0; i <= steps; i++) {
			const x = from.x + (point.x - from.x) * i / steps;
			const y = from.y + (point.y - from.y) * i / steps;
			const brush = context.createRadialGradient(x, y, point.radius * 0.55, x, y, point.radius);
			brush.addColorStop(0, 'rgba(0, 0, 0, 1)');
			brush.addColorStop(1, 'rgba(0, 0, 0, 0)');
			context.fillStyle = brush;
			context.fillRect(x - point.radius, y - point.radius, point.radius * 2, point.radius * 2);
		}
		context.globalCompositeOperation = 'source-over';
		previous = point;
	};
	const queue = (event: PointerEvent) => {
		if (reduced.matches || revealed || document.hidden || !visible) return;
		if (event.pointerType !== 'mouse' && event.buttons === 0) return;
		const rect = screen.getBoundingClientRect();
		pending = {
			x: (event.clientX - rect.left) * width / rect.width,
			y: (event.clientY - rect.top) * height / rect.height,
			radius: event.buttons ? 48 : 30,
		};
		if (!frame) frame = requestAnimationFrame(wipe);
	};

	screen.addEventListener('pointermove', queue, { passive: true });
	screen.addEventListener('pointerdown', (event) => {
		queue(event);
		if (event.pointerType !== 'mouse') {
			cancelAnimationFrame(frame);
			wipe();
		}
	}, { passive: true });
	screen.addEventListener('pointerleave', cancel);
	screen.addEventListener('pointercancel', cancel);
	screen.addEventListener('pointerup', () => { previous = null; });
	controls.querySelector('[data-clear-mist]')?.addEventListener('click', clear);
	controls.querySelector('[data-reset-mist]')?.addEventListener('click', fog);
	reduced.addEventListener('change', resize);
	document.addEventListener('visibilitychange', cancel);
	new IntersectionObserver(([entry]) => {
		visible = entry.isIntersecting;
		if (!visible) cancel();
	}).observe(screen);
	new ResizeObserver(resize).observe(screen);
	resize();
}
export {};
