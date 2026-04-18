// Shared palette — deliberately emerald/teal-led so it doesn't clash with the
// indigo sibling app. Category colors are deterministic per name via a stable
// hash, so "Groceries" is the same color on every screen.

export const PRIMARY = '#059669'; // emerald-600
export const PRIMARY_DARK = '#047857'; // emerald-700
export const PRIMARY_LIGHT = '#d1fae5'; // emerald-100
export const PRIMARY_TEXT = '#065f46'; // emerald-800

// Distinguishable palette — hues spread around the wheel so adjacent
// categories never look alike. Used as fallback for unknown categories.
export const CATEGORY_PALETTE = [
	'#10b981', // emerald
	'#f97316', // orange
	'#3b82f6', // blue
	'#ec4899', // pink
	'#8b5cf6', // violet
	'#ef4444', // red
	'#06b6d4', // cyan
	'#eab308', // yellow
	'#a855f7', // purple
	'#14b8a6', // teal
	'#f43f5e', // rose
	'#6366f1', // indigo
	'#84cc16', // lime
	'#64748b' // slate (fallback)
];

// Explicit per-category colors — hand-picked so common categories are
// unmistakably distinct. Unknown categories fall back to the hashed palette.
const CATEGORY_COLOR_MAP: Record<string, string> = {
	Groceries: '#10b981', // emerald — fresh food
	'Eat Out': '#f97316', // orange — warm restaurants
	Transport: '#3b82f6', // blue — motion/vehicles
	Shopping: '#ec4899', // pink — retail
	Subscription: '#8b5cf6', // violet — recurring/digital
	Entertainment: '#ef4444', // red — fun/energy
	Utilities: '#06b6d4', // cyan — water/electric/gas
	Health: '#f43f5e', // rose — medical
	Travel: '#0ea5e9', // sky — airlines/hotels
	Personal: '#a855f7', // purple — self-care
	Education: '#6366f1', // indigo — school
	Maintenance: '#ca8a04', // amber-dark — home upkeep
	Cash: '#eab308', // yellow — cash/ATM
	Other: '#64748b' // slate — neutral
};

function hash(s: string): number {
	let h = 2166136261 >>> 0;
	for (let i = 0; i < s.length; i++) {
		h ^= s.charCodeAt(i);
		h = Math.imul(h, 16777619);
	}
	return h >>> 0;
}

/**
 * Normalize category names — both the new short names the backend emits AND
 * the older verbose names stored in historical analyses — to a single short
 * display label. This keeps coloring and grouping stable across reanalyses.
 */
export function shortCategory(name: string | undefined | null): string {
	const raw = (name ?? '').trim();
	if (!raw) return 'Other';
	const k = raw.toLowerCase();
	if (k.includes('grocer')) return 'Groceries';
	if (k.includes('dining') || k.includes('restaurant') || k === 'eat out' || k === 'eating out')
		return 'Eat Out';
	if (k.includes('transport') || k === 'gas' || k.includes('fuel')) return 'Transport';
	if (k.includes('shopping') || k.includes('retail')) return 'Shopping';
	if (k.includes('subscription') && !k.includes('entertain')) return 'Subscription';
	if (k.includes('entertain')) return 'Entertainment';
	if (k.includes('utilit') || k.includes('bills')) return 'Utilities';
	if (k.includes('health') || k.includes('medical')) return 'Health';
	if (k.includes('travel') || k.includes('hotel')) return 'Travel';
	if (k.includes('personal')) return 'Personal';
	if (k.includes('education') || k.includes('school')) return 'Education';
	if (k.includes('maintenance') || k.includes('home')) return 'Maintenance';
	if (k.includes('cash') || k.includes('atm')) return 'Cash';
	if (k.includes('fee') || k.includes('interest')) return 'Other';
	return raw;
}

export function categoryColor(name: string | undefined | null): string {
	const s = shortCategory(name);
	if (!s) return CATEGORY_PALETTE[CATEGORY_PALETTE.length - 1];
	// Explicit mapping for known categories — guarantees distinct hues.
	if (s in CATEGORY_COLOR_MAP) return CATEGORY_COLOR_MAP[s];
	// Unknown custom category — fall back to hashed palette.
	return CATEGORY_PALETTE[hash(s.toLowerCase()) % CATEGORY_PALETTE.length];
}

/** Lighter tint of a hex color for backgrounds (mixed with white at `alpha`). */
export function tint(hex: string, alpha = 0.15): string {
	const m = hex.match(/^#?([0-9a-f]{6})$/i);
	if (!m) return hex;
	const n = parseInt(m[1], 16);
	const r = (n >> 16) & 0xff;
	const g = (n >> 8) & 0xff;
	const b = n & 0xff;
	return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

// 7-layer heat for calendar cells (white → deep emerald).
export const HEAT_STEPS = [
	'#f8fafc', // bucket 0 (no spend)
	'#d1fae5',
	'#a7f3d0',
	'#6ee7b7',
	'#34d399',
	'#10b981',
	'#047857'
];

/** Given an amount and max in the visible period, return one of 7 heat colors. */
export function heatBucket(amount: number, max: number): string {
	if (amount <= 0 || max <= 0) return HEAT_STEPS[0];
	// Non-linear so small-spend days aren't invisible
	const t = Math.min(1, Math.pow(amount / max, 0.55));
	const idx = Math.min(6, Math.max(1, Math.ceil(t * 6)));
	return HEAT_STEPS[idx];
}
