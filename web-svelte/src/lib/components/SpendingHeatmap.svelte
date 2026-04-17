<script lang="ts">
	let { data }: { data: Map<string, number> } = $props();

	// Build weeks-of-cells covering min..max date range.
	type Cell = { iso: string; date: Date; amount: number } | null;

	const grid = $derived.by(() => {
		if (data.size === 0) return { weeks: [] as Cell[][], maxAmount: 0, total: 0, days: 0, months: [] as { label: string; weekIndex: number }[] };

		const dates = Array.from(data.keys()).sort();
		let minD = new Date(dates[0] + 'T00:00:00');
		let maxD = new Date(dates[dates.length - 1] + 'T00:00:00');

		// Pad: start on Sunday of first week, end on Saturday of last week
		const start = new Date(minD);
		start.setDate(start.getDate() - start.getDay());
		const end = new Date(maxD);
		end.setDate(end.getDate() + (6 - end.getDay()));

		const weeks: Cell[][] = [];
		let current = new Date(start);
		let maxAmount = 0;
		let total = 0;
		let days = 0;
		const months: { label: string; weekIndex: number }[] = [];
		let lastMonth = -1;
		let weekIndex = 0;

		while (current <= end) {
			const week: Cell[] = [];
			for (let d = 0; d < 7; d++) {
				if (current < minD || current > maxD) {
					week.push(null);
				} else {
					const iso = toIso(current);
					const amount = data.get(iso) ?? 0;
					if (amount > 0) {
						days++;
						total += amount;
						if (amount > maxAmount) maxAmount = amount;
					}
					week.push({ iso, date: new Date(current), amount });
				}
				if (current.getDate() === 1 && current.getMonth() !== lastMonth) {
					months.push({
						label: current.toLocaleString(undefined, { month: 'short' }),
						weekIndex
					});
					lastMonth = current.getMonth();
				}
				current.setDate(current.getDate() + 1);
			}
			weeks.push(week);
			weekIndex++;
		}
		return { weeks, maxAmount, total, days, months };
	});

	function toIso(d: Date): string {
		const y = d.getFullYear();
		const m = String(d.getMonth() + 1).padStart(2, '0');
		const day = String(d.getDate()).padStart(2, '0');
		return `${y}-${m}-${day}`;
	}

	function color(amount: number, max: number): string {
		if (amount <= 0 || max <= 0) return '#f1f5f9';
		const t = Math.min(1, Math.pow(amount / max, 0.5));
		// Interpolate from light indigo → deep indigo/red
		// buckets for that GitHub feel
		if (t < 0.2) return '#e0e7ff';
		if (t < 0.4) return '#a5b4fc';
		if (t < 0.6) return '#818cf8';
		if (t < 0.8) return '#6366f1';
		return '#4338ca';
	}

	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
	}

	function tooltipText(c: Cell): string {
		if (!c) return '';
		const label = c.date.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });
		if (c.amount === 0) return `${label} — no spending`;
		return `${label} — $${c.amount.toFixed(2)}`;
	}
</script>

{#if grid.weeks.length === 0}
	<p class="muted">No dated transactions to display.</p>
{:else}
	<div class="heatmap-wrap">
		<div class="stats-strip">
			<span><strong>${fmt(grid.total)}</strong> total</span>
			<span><strong>{grid.days}</strong> active days</span>
			<span>peak: <strong>${fmt(grid.maxAmount)}</strong></span>
		</div>
		<div class="heatmap-scroll">
			<div class="heatmap">
				<div class="months-row">
					{#each grid.months as m}
						<span class="month-label" style="grid-column: {m.weekIndex + 2};">{m.label}</span>
					{/each}
				</div>
				<div class="grid">
					<div class="day-labels">
						<span>Mon</span>
						<span>Wed</span>
						<span>Fri</span>
					</div>
					<div class="cells">
						{#each grid.weeks as week}
							<div class="week">
								{#each week as cell}
									{#if cell}
										<div
											class="cell"
											style="background: {color(cell.amount, grid.maxAmount)};"
											title={tooltipText(cell)}
										></div>
									{:else}
										<div class="cell empty"></div>
									{/if}
								{/each}
							</div>
						{/each}
					</div>
				</div>
				<div class="legend">
					<span>Less</span>
					<span class="sw" style="background:#f1f5f9"></span>
					<span class="sw" style="background:#e0e7ff"></span>
					<span class="sw" style="background:#a5b4fc"></span>
					<span class="sw" style="background:#818cf8"></span>
					<span class="sw" style="background:#6366f1"></span>
					<span class="sw" style="background:#4338ca"></span>
					<span>More</span>
				</div>
			</div>
		</div>
	</div>
{/if}

<style>
	.heatmap-wrap {
		display: flex;
		flex-direction: column;
		gap: 10px;
	}

	.stats-strip {
		display: flex;
		gap: 18px;
		flex-wrap: wrap;
		font-size: 0.85rem;
		color: #4b5563;
	}

	.stats-strip strong {
		color: #1f2937;
	}

	.heatmap-scroll {
		overflow-x: auto;
		padding-bottom: 4px;
	}

	.heatmap {
		display: inline-block;
		min-width: 100%;
	}

	.months-row {
		display: grid;
		grid-auto-flow: column;
		grid-auto-columns: 14px;
		gap: 3px;
		height: 16px;
		margin-bottom: 2px;
		margin-left: 28px;
		font-size: 0.7rem;
		color: #6b7280;
		position: relative;
	}

	.month-label {
		grid-row: 1;
		white-space: nowrap;
	}

	.grid {
		display: flex;
		gap: 6px;
	}

	.day-labels {
		display: grid;
		grid-template-rows: repeat(7, 14px);
		gap: 3px;
		font-size: 0.65rem;
		color: #9ca3af;
		width: 22px;
		padding-top: 0;
	}
	.day-labels span:nth-child(1) { grid-row: 2; }
	.day-labels span:nth-child(2) { grid-row: 4; }
	.day-labels span:nth-child(3) { grid-row: 6; }

	.cells {
		display: flex;
		gap: 3px;
	}

	.week {
		display: grid;
		grid-template-rows: repeat(7, 14px);
		gap: 3px;
	}

	.cell {
		width: 14px;
		height: 14px;
		border-radius: 3px;
		transition: transform 0.1s;
	}

	.cell:hover:not(.empty) {
		transform: scale(1.4);
		outline: 1px solid #1f2937;
	}

	.cell.empty {
		background: transparent;
	}

	.legend {
		display: flex;
		align-items: center;
		gap: 4px;
		font-size: 0.72rem;
		color: #6b7280;
		margin-top: 8px;
	}

	.legend .sw {
		width: 12px;
		height: 12px;
		border-radius: 3px;
		display: inline-block;
	}
</style>
