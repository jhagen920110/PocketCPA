// Generates PWA / iOS / favicon icons from ../../app-icon-base.png.
// Run with: node scripts/generate-icons.mjs
import sharp from 'sharp';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { mkdirSync } from 'node:fs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SRC = resolve(__dirname, '../../app-icon-base.png');
const OUT = resolve(__dirname, '../static/icons');
mkdirSync(OUT, { recursive: true });

// Soft mint gradient — gentler than the old flat #6ee7b7. Pale mint in the
// top-left easing into a slightly deeper (but still soft) emerald in the
// bottom-right, with a subtle light highlight. iOS bakes this in since it
// doesn't render transparency well on the home screen.
const GRAD_TOP = '#EAFBF2'; // very pale mint
const GRAD_BOT = '#A7F3D0'; // emerald-200

function gradientSvg(size) {
	return Buffer.from(`
<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="${GRAD_TOP}"/>
      <stop offset="100%" stop-color="${GRAD_BOT}"/>
    </linearGradient>
    <radialGradient id="h" cx="30%" cy="25%" r="65%">
      <stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.55"/>
      <stop offset="60%" stop-color="#FFFFFF" stop-opacity="0"/>
    </radialGradient>
  </defs>
  <rect width="${size}" height="${size}" fill="url(#g)"/>
  <rect width="${size}" height="${size}" fill="url(#h)"/>
</svg>`);
}

async function gradientCanvas(size) {
	return sharp(gradientSvg(size)).png().toBuffer();
}

// Lazily produce a version of the source icon with the outer background
// removed via corner flood-fill. The source PNG has a faint vignette around
// the book that defeats plain `.trim()`, and chroma-keying white would also
// erase the book's interior pages. Flood-filling from the corners only
// targets the connected exterior region.
let _cutout = null;
async function getCutoutIcon() {
	if (_cutout) return _cutout;
	const { data, info } = await sharp(SRC)
		.ensureAlpha()
		.raw()
		.toBuffer({ resolveWithObject: true });
	const { width: w, height: h, channels } = info;
	const buf = Buffer.from(data); // mutable copy
	const visited = new Uint8Array(w * h);
	const stack = [];

	// Sample the average corner color as the "background" reference.
	const sampleAt = (x, y) => {
		const i = (y * w + x) * channels;
		return [buf[i], buf[i + 1], buf[i + 2]];
	};
	const corners = [sampleAt(0, 0), sampleAt(w - 1, 0), sampleAt(0, h - 1), sampleAt(w - 1, h - 1)];
	const ref = [0, 1, 2].map((c) => corners.reduce((a, p) => a + p[c], 0) / corners.length);

	// Generous tolerance — we want to fill the faint vignette too, but not
	// the book. The book frame is a saturated blue-gray, well outside this
	// distance from near-white.
	const TOL = 55;
	const near = (i) => {
		const dr = buf[i] - ref[0];
		const dg = buf[i + 1] - ref[1];
		const db = buf[i + 2] - ref[2];
		return dr * dr + dg * dg + db * db <= TOL * TOL;
	};

	const push = (x, y) => {
		if (x < 0 || y < 0 || x >= w || y >= h) return;
		const p = y * w + x;
		if (visited[p]) return;
		const i = p * channels;
		if (!near(i)) return;
		visited[p] = 1;
		buf[i + 3] = 0; // transparent
		stack.push(x, y);
	};

	for (const [sx, sy] of [
		[0, 0],
		[w - 1, 0],
		[0, h - 1],
		[w - 1, h - 1]
	]) {
		push(sx, sy);
	}
	while (stack.length) {
		const y = stack.pop();
		const x = stack.pop();
		push(x + 1, y);
		push(x - 1, y);
		push(x, y + 1);
		push(x, y - 1);
	}

	_cutout = await sharp(buf, { raw: { width: w, height: h, channels } }).png().toBuffer();
	return _cutout;
}

async function makeFlat(size, outName, pad = 0.12, withGradient = false) {
	const inner = Math.round(size * (1 - pad * 2));
	const src = withGradient ? await getCutoutIcon() : SRC;

	const icon = await sharp(src)
		.resize(inner, inner, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
		.toBuffer();

	const base = withGradient
		? sharp(await gradientCanvas(size))
		: sharp({
				create: {
					width: size,
					height: size,
					channels: 4,
					background: { r: 0, g: 0, b: 0, alpha: 0 }
				}
			});

	const offset = Math.round((size - inner) / 2);
	await base
		.composite([{ input: icon, top: offset, left: offset }])
		.png()
		.toFile(resolve(OUT, outName));
	console.log('✓', outName);
}

// Padding lets the mint gradient frame the logo (the base PNG has an opaque
// light background, so without padding the gradient would be hidden).
const PAD = 0.1;
const MASK_PAD = 0.18; // maskable icons need a bigger safe zone

// iOS apple-touch-icon: 180px, soft mint gradient baked in.
await makeFlat(180, 'apple-touch-icon.png', PAD, true);

// Android/PWA "any" icons: gradient baked in so the logo sits on brand color
// regardless of launcher background.
await makeFlat(192, 'icon-192.png', PAD, true);
await makeFlat(512, 'icon-512.png', PAD, true);

// Maskable icons: gradient background with larger safe-zone padding.
await makeFlat(192, 'icon-192-maskable.png', MASK_PAD, true);
await makeFlat(512, 'icon-512-maskable.png', MASK_PAD, true);

// Favicons: transparent so browser tabs stay clean at 16/32 px.
await makeFlat(32, 'favicon-32.png', 0.0, false);
await makeFlat(16, 'favicon-16.png', 0.0, false);

console.log('\nAll icons written to static/icons/');
