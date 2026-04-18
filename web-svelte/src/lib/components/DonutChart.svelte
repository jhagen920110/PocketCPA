<script lang="ts">
	import { tweened } from 'svelte/motion';
	import { cubicOut } from 'svelte/easing';
	import { fade } from 'svelte/transition';
	import { categoryColor, shortCategory } from '$lib/colors';

	interface CategoryData {
		name: string;
		total: number;
		percentage: number;
	}

	let {
		categories,
		totalSpent,
		minimal = false
	}: { categories: CategoryData[]; totalSpent: number; minimal?: boolean } = $props();

	let hoveredIndex = $state<number | null>(null);
	let animationProgress = tweened(0, { duration: 900, easing: cubicOut });

	$effect(() => {
		animationProgress.set(1);
	});

	// Display-group by short category so historical "Dining Out / Restaurants"
	// and new "Eat Out" show up once under the short label.
	const grouped = $derived.by(() => {
		const m = new Map<string, { total: number }>();
		for (const c of categories) {
			const key = shortCategory(c.name);
			const cur = m.get(key) ?? { total: 0 };
			cur.total += c.total;
			m.set(key, cur);
		}
		const total = Array.from(m.values()).reduce((s, v) => s + v.total, 0) || totalSpent || 1;
		return Array.from(m.entries())
			.map(([name, v]) => ({ name, total: v.total, percentage: (v.total / total) * 100 }))
			.sort((a, b) => b.total - a.total);
	});

	// Geometry: viewBox is oversized (400x300) so there's room for leader labels.
	const VB_W = 400;
	const VB_H = 300;
	const cx = VB_W / 2;
	const cy = 150;
	const radius = 92;
	const strokeWidth = 32;
	const labelR1 = radius + 4; // leader starts just outside the ring
	const labelR2 = radius + 12; // short leader — keeps text close to the donut
	const textGap = 3; // tiny gap between leader end and label text

	function polar(angle: number, r: number) {
		return { x: cx + r * Math.cos(angle), y: cy + r * Math.sin(angle) };
	}

	function describeArc(startAngle: number, endAngle: number, expand: boolean) {
		const r = expand ? radius + 4 : radius;
		const sw = expand ? strokeWidth + 3 : strokeWidth;
		if (endAngle - startAngle >= 2 * Math.PI - 0.001) {
			const mid = startAngle + Math.PI;
			const p1 = polar(startAngle, r);
			const pm = polar(mid, r);
			return {
				d: `M ${p1.x} ${p1.y} A ${r} ${r} 0 0 1 ${pm.x} ${pm.y} A ${r} ${r} 0 0 1 ${p1.x} ${p1.y}`,
				strokeWidth: sw
			};
		}
		const p1 = polar(startAngle, r);
		const p2 = polar(endAngle, r);
		const largeArc = endAngle - startAngle > Math.PI ? 1 : 0;
		return {
			d: `M ${p1.x} ${p1.y} A ${r} ${r} 0 ${largeArc} 1 ${p2.x} ${p2.y}`,
			strokeWidth: sw
		};
	}

	const segments = $derived.by(() => {
		const progress = $animationProgress;
		let current = -Math.PI / 2;
		return grouped.map((cat, i) => {
			const slice = (cat.percentage / 100) * 2 * Math.PI * progress;
			const start = current;
			const end = current + Math.max(slice, 0.005);
			current = end;
			const mid = (start + end) / 2;
			const leader1 = polar(mid, labelR1);
			const leader2 = polar(mid, labelR2);
			const right = leader2.x >= cx;
			const textX = right
				? Math.min(VB_W - 2, leader2.x + textGap)
				: Math.max(2, leader2.x - textGap);
			return {
				...describeArc(start, end, hoveredIndex === i),
				color: categoryColor(cat.name),
				name: cat.name,
				total: cat.total,
				percentage: cat.percentage,
				index: i,
				mid,
				leader1,
				leader2,
				textX,
				textY: leader2.y,
				anchor: right ? 'start' : 'end',
				showLabel: cat.percentage >= 3 // hide tiny slices
			};
		});
	});

	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}
	function fmt0(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
	}
</script>

<div class="donut-wrap" class:minimal>
	<svg viewBox="0 0 {VB_W} {VB_H}" class="donut-svg" preserveAspectRatio="xMidYMid meet">
		{#each segments as seg}
			<!-- svelte-ignore a11y_no_static_element_interactions -->
			<path
				d={seg.d}
				fill="none"
				stroke={seg.color}
				stroke-width={seg.strokeWidth}
				stroke-linecap="butt"
				class="donut-segment"
				class:hovered={hoveredIndex === seg.index}
				onmouseenter={() => (hoveredIndex = seg.index)}
				onmouseleave={() => (hoveredIndex = null)}
			/>
		{/each}
		<text x={cx} y={cy - 6} text-anchor="middle" class="center-label">Total</text>
		<text x={cx} y={cy + 18} text-anchor="middle" class="center-amount">${fmt0(totalSpent)}</text>

		{#if minimal}
			{#each segments as seg}
				{#if seg.showLabel}
					<g class="leader" class:leader-hot={hoveredIndex === seg.index}>
						<polyline
							points="{seg.leader1.x},{seg.leader1.y} {seg.leader2.x},{seg.leader2.y}"
							fill="none"
							stroke="#94a3b8"
							stroke-width="1"
							opacity="0.6"
						/>
						<text
							x={seg.textX}
							y={seg.textY + 4}
							text-anchor={seg.anchor}
							class="leader-label"
						>
							{seg.name}
						</text>
					</g>
				{/if}
			{/each}
		{/if}
	</svg>

	{#if hoveredIndex !== null && !minimal}
		<div class="donut-tooltip" transition:fade={{ duration: 120 }}>
			<div class="tooltip-color" style="background: {categoryColor(grouped[hoveredIndex].name)}"></div>
			<div>
				<div class="tooltip-name">{grouped[hoveredIndex].name}</div>
				<div class="tooltip-amount">
					${fmt(grouped[hoveredIndex].total)} ({grouped[hoveredIndex].percentage.toFixed(1)}%)
				</div>
			</div>
		</div>
	{/if}

	{#if !minimal}
		<div class="donut-legend">
			{#each grouped as cat, i}
				<!-- svelte-ignore a11y_no_static_element_interactions -->
				<div
					class="legend-item"
					class:legend-active={hoveredIndex === i}
					onmouseenter={() => (hoveredIndex = i)}
					onmouseleave={() => (hoveredIndex = null)}
				>
					<span class="legend-dot" style="background: {categoryColor(cat.name)}"></span>
					<span class="legend-name">{cat.name}</span>
					<span class="legend-value">${fmt(cat.total)}</span>
				</div>
			{/each}
		</div>
	{/if}
</div>

<style>
	.donut-wrap {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 14px;
		margin: 6px 0;
		position: relative;
	}
	.donut-wrap.minimal {
		gap: 0;
	}
	.donut-svg {
		width: 100%;
		max-width: 420px;
		height: auto;
		filter: drop-shadow(0 4px 12px rgba(15, 23, 42, 0.08));
	}
	.donut-segment {
		transition: stroke-width 0.18s ease, filter 0.18s ease;
		cursor: pointer;
	}
	.donut-segment.hovered {
		filter: brightness(1.12) drop-shadow(0 2px 8px rgba(0, 0, 0, 0.18));
	}
	.center-label {
		font-size: 14px;
		fill: #667085;
		font-weight: 500;
	}
	.center-amount {
		font-size: 22px;
		fill: #1f2937;
		font-weight: 700;
	}
	.leader-label {
		font-size: 11px;
		font-weight: 600;
		font-family: inherit;
		fill: #475569;
	}
	.leader-hot .leader-label {
		font-weight: 700;
		fill: #0f172a;
	}
	.donut-tooltip {
		display: flex;
		align-items: center;
		gap: 10px;
		background: white;
		border: 1px solid rgba(148, 163, 184, 0.18);
		border-radius: 10px;
		padding: 10px 16px;
		box-shadow: 0 4px 16px rgba(15, 23, 42, 0.12);
		position: absolute;
		pointer-events: none;
	}
	.tooltip-color {
		width: 14px;
		height: 14px;
		border-radius: 4px;
		flex-shrink: 0;
	}
	.tooltip-name {
		font-weight: 600;
		font-size: 0.9rem;
	}
	.tooltip-amount {
		color: #667085;
		font-size: 0.85rem;
	}
	.donut-legend {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 4px 16px;
		width: 100%;
		max-width: 500px;
	}
	.legend-item {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 4px 8px;
		border-radius: 6px;
		cursor: pointer;
		transition: background 0.15s;
		font-size: 0.85rem;
	}
	.legend-item:hover,
	.legend-active {
		background: rgba(5, 150, 105, 0.08);
	}
	.legend-dot {
		width: 10px;
		height: 10px;
		border-radius: 3px;
		flex-shrink: 0;
	}
	.legend-name {
		flex: 1;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
	.legend-value {
		color: #667085;
		font-weight: 600;
		white-space: nowrap;
	}
	@media (max-width: 500px) {
		.donut-legend {
			grid-template-columns: 1fr;
		}
	}
</style>
