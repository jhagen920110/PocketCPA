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

async function makeFlat(size, outName, pad = 0.12, withGradient = false) {
	const inner = Math.round(size * (1 - pad * 2));
	const radius = Math.round(inner * 0.18); // iOS-style squircle corners

	// Render the source icon at inner size...
	const iconRaw = await sharp(SRC)
		.resize(inner, inner, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
		.toBuffer();

	// ...then mask it with a rounded rect so the base PNG's white background
	// reads as a polished card rather than a hard square.
	const mask = Buffer.from(
		`<svg xmlns="http://www.w3.org/2000/svg" width="${inner}" height="${inner}"><rect width="${inner}" height="${inner}" rx="${radius}" ry="${radius}" fill="#fff"/></svg>`
	);
	const icon = withGradient
		? await sharp(iconRaw)
				.composite([{ input: mask, blend: 'dest-in' }])
				.png()
				.toBuffer()
		: iconRaw;

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
