<script lang="ts">
	import { tweened } from 'svelte/motion';
	import { cubicOut } from 'svelte/easing';
	import { fade } from 'svelte/transition';

	interface CategoryData {
		name: string;
		total: number;
		percentage: number;
	}

	let { categories, totalSpent }: { categories: CategoryData[]; totalSpent: number } = $props();

	const COLORS = [
		'#4f46e5', '#06b6d4', '#f59e0b', '#ef4444', '#10b981',
		'#8b5cf6', '#ec4899', '#f97316', '#14b8a6', '#6366f1',
		'#84cc16', '#e11d48', '#0ea5e9', '#a855f7', '#64748b'
	];

	let hoveredIndex = $state<number | null>(null);
	let animationProgress = tweened(0, { duration: 1200, easing: cubicOut });

	// Kick off animation on mount
	$effect(() => {
		animationProgress.set(1);
	});

	const sorted = $derived([...categories].sort((a, b) => b.total - a.total));

	const cx = 150;
	const cy = 150;
	const radius = 120;
	const strokeWidth = 40;

	function describeArc(startAngle: number, endAngle: number, expand: boolean) {
		const r = expand ? radius + 6 : radius;
		const sw = expand ? strokeWidth + 4 : strokeWidth;
		// SVG arc can't draw a full 360° arc (endpoints coincide). Split into two halves.
		if (endAngle - startAngle >= 2 * Math.PI - 0.001) {
			const mid = startAngle + Math.PI;
			const x1 = cx + r * Math.cos(startAngle);
			const y1 = cy + r * Math.sin(startAngle);
			const xm = cx + r * Math.cos(mid);
			const ym = cy + r * Math.sin(mid);
			return {
				d: `M ${x1} ${y1} A ${r} ${r} 0 0 1 ${xm} ${ym} A ${r} ${r} 0 0 1 ${x1} ${y1}`,
				strokeWidth: sw
			};
		}
		const x1 = cx + r * Math.cos(startAngle);
		const y1 = cy + r * Math.sin(startAngle);
		const x2 = cx + r * Math.cos(endAngle);
		const y2 = cy + r * Math.sin(endAngle);
		const largeArc = endAngle - startAngle > Math.PI ? 1 : 0;
		return {
			d: `M ${x1} ${y1} A ${r} ${r} 0 ${largeArc} 1 ${x2} ${y2}`,
			strokeWidth: sw
		};
	}

	const segments = $derived.by(() => {
		const progress = $animationProgress;
		let currentAngle = -Math.PI / 2;
		return sorted.map((cat, i) => {
			const sliceAngle = (cat.percentage / 100) * 2 * Math.PI * progress;
			const startAngle = currentAngle;
			const endAngle = currentAngle + Math.max(sliceAngle, 0.01);
			currentAngle = endAngle;
			const isHovered = hoveredIndex === i;
			return {
				...describeArc(startAngle, endAngle, isHovered),
				color: COLORS[i % COLORS.length],
				name: cat.name,
				total: cat.total,
				percentage: cat.percentage,
				index: i
			};
		});
	});

	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}
</script>

<div class="donut-container">
	<svg viewBox="0 0 300 300" class="donut-svg">
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

		<!-- Center text -->
		<text x={cx} y={cy - 10} text-anchor="middle" class="center-label">Total</text>
		<text x={cx} y={cy + 18} text-anchor="middle" class="center-amount">${fmt(totalSpent)}</text>
	</svg>

	<!-- Hover tooltip -->
	{#if hoveredIndex !== null}
		<div class="donut-tooltip" transition:fade={{ duration: 150 }}>
			<div class="tooltip-color" style="background: {COLORS[hoveredIndex % COLORS.length]}"></div>
			<div>
				<div class="tooltip-name">{sorted[hoveredIndex].name}</div>
				<div class="tooltip-amount">${fmt(sorted[hoveredIndex].total)} ({sorted[hoveredIndex].percentage.toFixed(1)}%)</div>
			</div>
		</div>
	{/if}

	<!-- Legend -->
	<div class="donut-legend">
		{#each sorted as cat, i}
			<!-- svelte-ignore a11y_no_static_element_interactions -->
			<div
				class="legend-item"
				class:legend-active={hoveredIndex === i}
				onmouseenter={() => (hoveredIndex = i)}
				onmouseleave={() => (hoveredIndex = null)}
			>
				<span class="legend-dot" style="background: {COLORS[i % COLORS.length]}"></span>
				<span class="legend-name">{cat.name}</span>
				<span class="legend-value">${fmt(cat.total)}</span>
			</div>
		{/each}
	</div>
</div>

<style>
	.donut-container {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 16px;
		margin: 20px 0;
	}

	.donut-svg {
		width: 260px;
		height: 260px;
		filter: drop-shadow(0 4px 12px rgba(15, 23, 42, 0.1));
	}

	.donut-segment {
		transition: stroke-width 0.2s ease, filter 0.2s ease;
		cursor: pointer;
	}

	.donut-segment.hovered {
		filter: brightness(1.15) drop-shadow(0 2px 8px rgba(0, 0, 0, 0.2));
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
		gap: 6px 20px;
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
		background: rgba(79, 70, 229, 0.06);
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
