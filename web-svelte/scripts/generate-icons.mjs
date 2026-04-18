// Generates PWA / iOS / favicon icons from ../../app-icon-base.png.
// Run with: node scripts/generate-icons.mjs
import sharp from 'sharp';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { mkdirSync, writeFileSync } from 'node:fs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SRC = resolve(__dirname, '../../app-icon-base.png');
const OUT = resolve(__dirname, '../static/icons');
mkdirSync(OUT, { recursive: true });

// White background for iOS + maskable icons (iOS doesn't support transparency
// well on the home screen — it renders white rectangles — so we bake white in).
const IOS_BG = { r: 255, g: 255, b: 255, alpha: 1 };

async function makeFlat(size, outName, pad = 0.12, bg = null) {
	const inner = Math.round(size * (1 - pad * 2));
	const icon = await sharp(SRC)
		.resize(inner, inner, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
		.toBuffer();
	const canvas = bg
		? sharp({
				create: {
					width: size,
					height: size,
					channels: 4,
					background: bg
				}
			})
		: sharp({
				create: {
					width: size,
					height: size,
					channels: 4,
					background: { r: 0, g: 0, b: 0, alpha: 0 }
				}
			});
	const offset = Math.round((size - inner) / 2);
	await canvas
		.composite([{ input: icon, top: offset, left: offset }])
		.png()
		.toFile(resolve(OUT, outName));
	console.log('✓', outName);
}

// iOS apple-touch-icon: 180px, white background, minimal padding for a bigger logo
await makeFlat(180, 'apple-touch-icon.png', 0.0, IOS_BG);

// Android/PWA "any" icons: transparent bg
await makeFlat(192, 'icon-192.png', 0.0, null);
await makeFlat(512, 'icon-512.png', 0.0, null);

// Maskable icons need safe-zone but keep the logo as large as the zone allows
await makeFlat(192, 'icon-192-maskable.png', 0.1, IOS_BG);
await makeFlat(512, 'icon-512-maskable.png', 0.1, IOS_BG);

// Favicons
await makeFlat(32, 'favicon-32.png', 0.0, null);
await makeFlat(16, 'favicon-16.png', 0.0, null);

console.log('\nAll icons written to static/icons/');
