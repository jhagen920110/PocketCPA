<script lang="ts">
	import { tweened } from 'svelte/motion';
	import { cubicOut } from 'svelte/easing';
	import { fade, fly } from 'svelte/transition';
	import { categoryColor, shortCategory } from '$lib/colors';

	interface CategoryData {
		name: string;
		total: number;
		percentage: number;
	}

	let {
		categories,
		totalSpent
	}: { categories: CategoryData[]; totalSpent: number } = $props();

	let selected = $state<number | null>(null);

	// Re-group categories by short name (fold legacy names into new ones).
	const grouped = $derived.by(() => {
		const m = new Map<string, number>();
		for (const c of categories) {
			const k = shortCategory(c.name);
			m.set(k, (m.get(k) ?? 0) + c.total);
		}
		const total = Array.from(m.values()).reduce((s, v) => s + v, 0) || totalSpent || 1;
		return Array.from(m.entries())
			.map(([name, amt]) => ({
				name,
				total: amt,
				percentage: (amt / total) * 100,
				color: categoryColor(name),
				emoji: emojiFor(name)
			}))
			.sort((a, b) => b.total - a.total);
	});

	function emojiFor(name: string): string {
		const k = name.toLowerCase();
		if (k.includes('groc')) return '🛒';
		if (k.includes('eat')) return '🍔';
		if (k.includes('transport')) return '🚗';
		if (k.includes('shop')) return '🛍️';
		if (k.includes('subscription')) return '📺';
		if (k.includes('entertain')) return '🎬';
		if (k.includes('utilit')) return '💡';
		if (k.includes('health')) return '🏥';
		if (k.includes('travel')) return '✈️';
		if (k.includes('personal')) return '💅';
		if (k.includes('education')) return '🎓';
		if (k.includes('maintenance')) return '🔧';
		if (k.includes('cash')) return '💵';
		return '💳';
	}

	// ---- Pack circles into a canvas with a smarter, center-anchored layout.
	// We spiral outward from the center and pick the closest valid position;
	// this gives a tight, non-overlapping cluster and avoids the "right-edge
	// stack" fallback that made tiny bubbles collide with big ones. ----
	const VB_W = 400;
	const VB_H = 280;
	// Slices smaller than this % are hidden in the bubble canvas (they still
	// appear in the legend chips below, so nothing is lost).
	const MIN_BUBBLE_PCT = 2;

	const progress = tweened(0, { duration: 700, easing: cubicOut });
	$effect(() => {
		progress.set(1);
	});

	interface Bubble {
		cx: number;
		cy: number;
		r: number;
		name: string;
		total: number;
		percentage: number;
		color: string;
		emoji: string;
		index: number;
	}

	const bubbles = $derived.by<Bubble[]>(() => {
		const cats = grouped.filter((c) => c.percentage >= MIN_BUBBLE_PCT);
		if (cats.length === 0) return [];
		const p = $progress;
		const maxTotal = Math.max(...cats.map((c) => c.total));

		// Target radius range. Slightly smaller rMax than before so a dominant
		// category doesn't hog space and force tiny bubbles to the edges.
		const rMax = 62;
		const rMin = 22;
		const rOf = (total: number) => {
			if (maxTotal <= 0) return rMin;
			const t = Math.sqrt(total / maxTotal);
			return rMin + (rMax - rMin) * t;
		};

		const pad = 5;
		const cx0 = VB_W / 2;
		const cy0 = VB_H / 2;

		const placed: Bubble[] = [];
		for (let i = 0; i < cats.length; i++) {
			const c = cats[i];
			const r = rOf(c.total);

			const fits = (x: number, y: number) => {
				if (x - r < 2 || x + r > VB_W - 2) return false;
				if (y - r < 2 || y + r > VB_H - 2) return false;
				for (const q of placed) {
					const dx = q.cx - x;
					const dy = q.cy - y;
					if (Math.sqrt(dx * dx + dy * dy) < q.r + r + pad) return false;
				}
				return true;
			};

			let best: { x: number; y: number } | null = null;

			if (placed.length === 0) {
				// Anchor the biggest in the center.
				if (fits(cx0, cy0)) best = { x: cx0, y: cy0 };
			} else {
				// Spiral outward from the center and take the first fit. This
				// produces a tight cluster while still keeping bubbles apart.
				const maxRadius = Math.sqrt(VB_W * VB_W + VB_H * VB_H);
				outer: for (let d = 6; d <= maxRadius; d += 3) {
					const steps = Math.max(24, Math.floor((2 * Math.PI * d) / 4));
					for (let s = 0; s < steps; s++) {
						const ang = (s / steps) * 2 * Math.PI;
						const x = cx0 + d * Math.cos(ang);
						const y = cy0 + d * Math.sin(ang);
						if (fits(x, y)) {
							best = { x, y };
							break outer;
						}
					}
				}
			}

			if (!best) continue; // couldn't fit — legend still shows it

			placed.push({
				cx: best.x,
				cy: best.y,
				r: r * p,
				name: c.name,
				total: c.total,
				percentage: c.percentage,
				color: c.color,
				emoji: c.emoji,
				index: grouped.findIndex((g) => g.name === c.name)
			});
		}
		return placed;
	});

	function fmt0(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
	}
	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}

	function toggle(i: number) {
		selected = selected === i ? null : i;
	}

	const selectedBubble = $derived(selected !== null ? bubbles[selected] : null);
</script>

<div class="bubbles-wrap">
	<div class="bubbles-header">
		<div class="bubbles-total">
			<div class="bubbles-total-label">Total</div>
			<div class="bubbles-total-value">${fmt0(totalSpent)}</div>
		</div>
		<div class="bubbles-hint">Tap a bubble or chip</div>
	</div>

	<svg
		viewBox="0 0 {VB_W} {VB_H}"
		class="bubbles-svg"
		preserveAspectRatio="xMidYMid meet"
		role="img"
		aria-label="Spending by category"
	>
		{#each bubbles as b (b.name)}
			{@const isSel = selected === b.index}
			<!-- svelte-ignore a11y_click_events_have_key_events a11y_no_static_element_interactions -->
			<g
				class="bubble"
				class:selected={isSel}
				class:dimmed={selected !== null && !isSel}
				role="button"
				tabindex="0"
				aria-label={`${b.name}, $${fmt0(b.total)} (${b.percentage.toFixed(0)}%)`}
				onclick={() => toggle(b.index)}
			>
				<circle cx={b.cx} cy={b.cy} r={b.r} fill={b.color} fill-opacity="0.18" stroke={b.color} stroke-width="2" />
				{#if b.r > 26}
					<text x={b.cx} y={b.cy - 6} text-anchor="middle" class="bubble-emoji" style="font-size: {Math.min(b.r * 0.65, 30)}px">{b.emoji}</text>
					<text x={b.cx} y={b.cy + Math.min(b.r * 0.45, 18)} text-anchor="middle" class="bubble-pct" fill={b.color} style="font-size: {Math.min(b.r * 0.35, 14)}px">{b.percentage.toFixed(0)}%</text>
				{:else}
					<text x={b.cx} y={b.cy + 4} text-anchor="middle" class="bubble-emoji" style="font-size: {Math.min(b.r * 0.85, 20)}px">{b.emoji}</text>
				{/if}
			</g>
		{/each}
	</svg>

	{#if selectedBubble}
		<div class="bubble-detail" in:fly={{ y: 10, duration: 180 }} out:fade={{ duration: 120 }}>
			<span class="bubble-detail-emoji">{selectedBubble.emoji}</span>
			<span class="bubble-detail-name" style="color: {selectedBubble.color}">{selectedBubble.name}</span>
			<span class="bubble-detail-amt">${fmt(selectedBubble.total)}</span>
			<span class="bubble-detail-pct">{selectedBubble.percentage.toFixed(1)}% of spending</span>
		</div>
	{/if}

	<div class="bubble-legend">
		{#each grouped as c, i}
			<button
				type="button"
				class="legend-chip"
				class:active={selected === i}
				onclick={() => toggle(i)}
				style="--chip-color: {c.color};"
			>
				<span class="legend-emoji">{c.emoji}</span>
				<span class="legend-name">{c.name}</span>
				<span class="legend-val">${fmt0(c.total)}</span>
			</button>
		{/each}
	</div>
</div>

<style>
	.bubbles-wrap {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}
	.bubbles-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0 4px;
	}
	.bubbles-total-label {
		font-size: 0.68rem;
		color: #94a3b8;
		text-transform: uppercase;
		letter-spacing: 0.06em;
		font-weight: 700;
	}
	.bubbles-total-value {
		font-size: 1.3rem;
		font-weight: 800;
		color: #1f2937;
		font-variant-numeric: tabular-nums;
	}
	.bubbles-hint {
		font-size: 0.72rem;
		color: #94a3b8;
	}
	.bubbles-svg {
		width: 100%;
		height: auto;
		max-height: 300px;
	}
	.bubble {
		cursor: pointer;
		transform-origin: center;
		transition: filter 0.15s ease, opacity 0.15s ease;
	}
	.bubble circle {
		transition: fill-opacity 0.18s ease, stroke-width 0.18s ease;
	}
	.bubble:hover circle,
	.bubble.selected circle {
		fill-opacity: 0.35;
		stroke-width: 3;
	}
	.bubble.dimmed {
		opacity: 0.3;
	}
	.bubble-emoji {
		font-family: 'Apple Color Emoji', 'Segoe UI Emoji', 'Noto Color Emoji', sans-serif;
		dominant-baseline: middle;
		pointer-events: none;
	}
	.bubble-pct {
		font-weight: 700;
		pointer-events: none;
	}

	.bubble-detail {
		display: flex;
		flex-wrap: wrap;
		align-items: baseline;
		gap: 8px;
		padding: 10px 12px;
		background: #f8fafc;
		border-radius: 10px;
		font-size: 0.9rem;
	}
	.bubble-detail-emoji {
		font-size: 1.1rem;
	}
	.bubble-detail-name {
		font-weight: 700;
	}
	.bubble-detail-amt {
		font-weight: 700;
		color: #1f2937;
		font-variant-numeric: tabular-nums;
	}
	.bubble-detail-pct {
		color: #64748b;
		font-size: 0.82rem;
	}

	.bubble-legend {
		display: flex;
		flex-wrap: wrap;
		gap: 6px;
	}
	.legend-chip {
		display: inline-flex;
		align-items: center;
		gap: 6px;
		padding: 6px 10px;
		border-radius: 999px;
		border: 1px solid rgba(148, 163, 184, 0.25);
		background: white;
		font-family: inherit;
		font-size: 0.78rem;
		font-weight: 600;
		color: #475569;
		cursor: pointer;
		transition: border-color 0.15s, background 0.15s;
	}
	.legend-chip:hover {
		border-color: var(--chip-color, #94a3b8);
	}
	.legend-chip.active {
		border-color: var(--chip-color, #059669);
		background: color-mix(in srgb, var(--chip-color, #059669) 10%, white);
		color: #111827;
	}
	.legend-emoji {
		font-size: 0.95rem;
	}
	.legend-val {
		color: #1f2937;
		font-variant-numeric: tabular-nums;
	}
</style>
