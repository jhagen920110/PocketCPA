<script lang="ts">
	import { fly, slide } from 'svelte/transition';
	import type { Analysis } from '$lib/api';
	import DonutChart from './DonutChart.svelte';
	import { categoryColor, shortCategory } from '$lib/colors';

	let { analysis }: { analysis: Analysis } = $props();

	const sorted = $derived([...analysis.categories].sort((a, b) => b.total - a.total));

	let expandedIndex = $state<number | null>(null);

	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}
	function fmt0(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
	}
	function formatMonth(m: string): string {
		const parts = (m ?? '').split('-');
		if (parts.length !== 2) return m;
		const d = new Date(parseInt(parts[0]), parseInt(parts[1]) - 1, 1);
		return d.toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
	}

	function toggle(i: number) {
		expandedIndex = expandedIndex === i ? null : i;
	}

	// Flatten all txns (positive amounts only) for stats.
	const allTxs = $derived.by(() => {
		const out: { date: string; merchant: string; amount: number; category: string }[] = [];
		for (const c of analysis.categories) {
			for (const t of c.transactions) {
				if (t.amount <= 0) continue;
				out.push({
					date: t.date,
					merchant: (t.merchant || t.description || '—').trim(),
					amount: t.amount,
					category: shortCategory(c.name)
				});
			}
		}
		return out;
	});

	// Locally-computed fun stats (merged with AI-provided ones, de-duped).
	interface FunFact { emoji: string; label: string; value: string; }
	const funStats = $derived.by(() => {
		const out: FunFact[] = [];
		const txs = allTxs;
		if (txs.length === 0) return analysis.funStats ?? [];

		// Biggest purchase
		const biggest = [...txs].sort((a, b) => b.amount - a.amount)[0];
		if (biggest) {
			out.push({
				emoji: '💥',
				label: 'Biggest purchase',
				value: `$${fmt0(biggest.amount)} at ${biggest.merchant}`
			});
		}

		// Top merchant by total
		const byMerch = new Map<string, { amount: number; count: number }>();
		for (const t of txs) {
			const cur = byMerch.get(t.merchant) ?? { amount: 0, count: 0 };
			cur.amount += t.amount;
			cur.count += 1;
			byMerch.set(t.merchant, cur);
		}
		const topMerch = Array.from(byMerch.entries()).sort((a, b) => b[1].amount - a[1].amount)[0];
		if (topMerch) {
			out.push({
				emoji: '🏆',
				label: 'Top merchant',
				value: `${topMerch[0]} — ${topMerch[1].count} visit${topMerch[1].count === 1 ? '' : 's'}, $${fmt0(topMerch[1].amount)}`
			});
		}

		// Most-visited merchant by count (if different)
		const byCount = Array.from(byMerch.entries()).sort((a, b) => b[1].count - a[1].count)[0];
		if (byCount && topMerch && byCount[0] !== topMerch[0] && byCount[1].count >= 3) {
			out.push({
				emoji: '🔁',
				label: 'Most frequent',
				value: `${byCount[0]} — ${byCount[1].count} times`
			});
		}

		// Avg per transaction
		out.push({
			emoji: '📊',
			label: 'Avg per txn',
			value: `$${fmt0(analysis.totalSpent / txs.length)}`
		});

		// Eat out share
		const eatOut = txs.filter((t) => t.category === 'Eat Out');
		if (eatOut.length > 0) {
			const total = eatOut.reduce((s, t) => s + t.amount, 0);
			const pct = Math.round((total / analysis.totalSpent) * 100);
			out.push({
				emoji: '🍔',
				label: 'Eating out',
				value: `${eatOut.length} meal${eatOut.length === 1 ? '' : 's'} (${pct}% of spend)`
			});
		}

		// Shopping share
		const shopping = txs.filter((t) => t.category === 'Shopping');
		if (shopping.length > 0) {
			const total = shopping.reduce((s, t) => s + t.amount, 0);
			const pct = Math.round((total / analysis.totalSpent) * 100);
			if (pct >= 10) {
				out.push({
					emoji: '🛍️',
					label: 'Shopping',
					value: `$${fmt0(total)} (${pct}% of spend)`
				});
			}
		}

		// Merge in AI fun-stats at the end (if any), skipping obvious dupes.
		for (const s of analysis.funStats ?? []) {
			if (!out.some((x) => x.label.toLowerCase() === s.label.toLowerCase())) out.push(s);
		}
		return out;
	});

	// Top 5 largest transactions (bonus view)
	const topTxs = $derived.by(() =>
		[...allTxs].sort((a, b) => b.amount - a.amount).slice(0, 5)
	);
</script>

<section class="card" in:fly={{ y: 30, duration: 500 }}>
	<div class="results-header">
		<h2>📊 Analysis Results</h2>
		{#if analysis.bank}
			<span class="bank-badge">🏦 {analysis.bank}</span>
		{/if}
	</div>

	<!-- Donut Chart -->
	<DonutChart categories={analysis.categories} totalSpent={analysis.totalSpent} />

	<!-- Summary: Month + Total (dropped Categories count per feedback) -->
	<div class="summary-cards two-up">
		<div class="summary-card">
			<div class="summary-label">Month</div>
			<div class="summary-value">{formatMonth(analysis.month)}</div>
		</div>
		<div class="summary-card">
			<div class="summary-label">Total Spent</div>
			<div class="summary-value">${fmt(analysis.totalSpent)}</div>
		</div>
	</div>

	<!-- Top 5 purchases — new quick-read section -->
	{#if topTxs.length > 0}
		<h3>🔝 Top 5 purchases</h3>
		<ul class="top-tx-list">
			{#each topTxs as t, i}
				<li class="top-tx">
					<span class="top-tx-rank">#{i + 1}</span>
					<span class="top-tx-dot" style="background: {categoryColor(t.category)}"></span>
					<span class="top-tx-merchant" title={t.merchant}>{t.merchant}</span>
					<span class="top-tx-cat">{t.category}</span>
					<span class="top-tx-amt">${fmt(t.amount)}</span>
				</li>
			{/each}
		</ul>
	{/if}

	<!-- Category bars (click to expand) -->
	<h3>Spending by Category</h3>
	<p class="hint">Click a category to see every purchase.</p>
	<div class="category-bars">
		{#each sorted as cat, i}
			<div class="category-bar-row" in:slide={{ delay: i * 60, duration: 300 }}>
				<button class="category-bar-button" onclick={() => toggle(i)} aria-expanded={expandedIndex === i}>
					<div class="category-bar-label">
						<span class="cat-name">
							<span class="cat-dot" style="background: {categoryColor(cat.name)}"></span>
							{shortCategory(cat.name)}
						</span>
						<span class="cat-amount">${fmt(cat.total)} &middot; {cat.percentage.toFixed(1)}%</span>
					</div>
					<div class="category-bar-track">
						<div
							class="category-bar-fill"
							style="width: {Math.min(cat.percentage, 100)}%; background: {categoryColor(cat.name)}"
						></div>
					</div>
				</button>

				{#if expandedIndex === i}
					<div class="tx-list" transition:slide={{ duration: 200 }}>
						{#each [...cat.transactions].sort((a, b) => b.amount - a.amount) as t}
							<div class="tx-row">
								<span class="tx-date">{t.date}</span>
								<span class="tx-merchant" title={t.description}>
									{t.merchant || t.description}
								</span>
								<span class="tx-amount">${t.amount.toFixed(2)}</span>
							</div>
						{/each}
					</div>
				{/if}
			</div>
		{/each}
	</div>

	<!-- Fun Stats (local + AI merged) -->
	{#if funStats.length > 0}
		<h3>✨ Fun Stats</h3>
		<div class="fun-stats">
			{#each funStats as s}
				<div class="fun-stat">
					<div class="fun-emoji">{s.emoji}</div>
					<div class="fun-text">
						<div class="fun-label">{s.label}</div>
						<div class="fun-value">{s.value}</div>
					</div>
				</div>
			{/each}
		</div>
	{/if}

	<!-- Insights -->
	{#if analysis.insights.length > 0}
		<h3>💡 Insights</h3>
		<ul>
			{#each analysis.insights as insight}
				<li>{insight}</li>
			{/each}
		</ul>
	{/if}

	<!-- Suggestions -->
	{#if analysis.suggestions.length > 0}
		<h3>💰 Suggestions</h3>
		<ul>
			{#each analysis.suggestions as suggestion}
				<li>{suggestion}</li>
			{/each}
		</ul>
	{/if}
</section>

<style>
	.summary-cards {
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 12px;
		margin-bottom: 24px;
	}
	.summary-cards.two-up {
		grid-template-columns: repeat(2, 1fr);
	}

	.summary-card {
		background: #f4f7fb;
		border-radius: 12px;
		padding: 16px;
		text-align: center;
	}

	.summary-label {
		font-size: 0.8rem;
		color: #667085;
		text-transform: uppercase;
		letter-spacing: 0.5px;
		margin-bottom: 4px;
	}

	.summary-value {
		font-size: 1.3rem;
		font-weight: 700;
	}

	h3 {
		margin: 20px 0 6px;
		font-size: 1.05rem;
	}

	.hint {
		color: #667085;
		font-size: 0.85rem;
		margin-bottom: 12px;
	}

	.category-bars {
		display: flex;
		flex-direction: column;
		gap: 10px;
		margin-bottom: 24px;
	}

	.category-bar-button {
		background: none;
		border: none;
		padding: 8px 10px;
		width: 100%;
		text-align: left;
		cursor: pointer;
		border-radius: 8px;
		transition: background 0.15s;
	}

	.category-bar-button:hover {
		background: #f4f7fb;
	}

	.category-bar-label {
		display: flex;
		justify-content: space-between;
		align-items: center;
		font-size: 0.92rem;
		margin-bottom: 6px;
	}

	.cat-name {
		display: flex;
		align-items: center;
		gap: 8px;
		font-weight: 600;
	}

	.cat-dot {
		width: 10px;
		height: 10px;
		border-radius: 3px;
		flex-shrink: 0;
	}

	.cat-amount {
		color: #1f2937;
		font-weight: 600;
	}

	.category-bar-track {
		height: 8px;
		background: #f4f7fb;
		border-radius: 5px;
		overflow: hidden;
	}

	.category-bar-fill {
		height: 100%;
		border-radius: 5px;
		transition: width 0.4s ease;
	}

	.tx-list {
		padding: 8px 12px 4px 24px;
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.tx-row {
		display: grid;
		grid-template-columns: 56px 1fr auto;
		gap: 12px;
		align-items: center;
		padding: 6px 8px;
		font-size: 0.88rem;
		border-bottom: 1px solid rgba(148, 163, 184, 0.12);
	}

	.tx-row:last-child {
		border-bottom: none;
	}

	.tx-date {
		color: #667085;
		font-variant-numeric: tabular-nums;
	}

	.tx-merchant {
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.tx-amount {
		font-weight: 600;
		color: #1f2937;
		font-variant-numeric: tabular-nums;
	}

	ul {
		list-style: disc;
		padding-left: 20px;
		margin-bottom: 16px;
	}

	li {
		margin-bottom: 6px;
		line-height: 1.5;
		font-size: 0.95rem;
	}

	@media (max-width: 500px) {
		.summary-cards {
			grid-template-columns: 1fr;
		}
	}

	.fun-stats {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
		gap: 10px;
		margin-bottom: 20px;
	}

	.fun-stat {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 12px 14px;
		background: linear-gradient(135deg, #ecfdf5 0%, #f0fdfa 100%);
		border-radius: 10px;
		border: 1px solid #a7f3d0;
	}

	.fun-emoji {
		font-size: 1.6rem;
		line-height: 1;
	}

	.fun-text {
		min-width: 0;
		flex: 1;
	}

	.fun-label {
		font-size: 0.75rem;
		color: #6b7280;
		text-transform: uppercase;
		letter-spacing: 0.03em;
		margin-bottom: 2px;
	}

	.fun-value {
		font-size: 0.92rem;
		font-weight: 600;
		color: #1f2937;
		line-height: 1.35;
		word-break: break-word;
		overflow-wrap: anywhere;
	}

	.results-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 10px;
		flex-wrap: wrap;
		margin-bottom: 14px;
	}

	.bank-badge {
		display: inline-flex;
		align-items: center;
		gap: 4px;
		padding: 4px 12px;
		background: #d1fae5;
		color: #065f46;
		border-radius: 999px;
		font-size: 0.82rem;
		font-weight: 600;
	}

	.fun-stat {
		align-items: flex-start;
	}

	/* Top-N transactions list */
	.top-tx-list {
		list-style: none;
		padding: 0;
		margin: 0 0 20px;
		display: flex;
		flex-direction: column;
		gap: 6px;
	}
	.top-tx {
		display: grid;
		grid-template-columns: 28px 10px 1fr auto auto;
		align-items: center;
		gap: 10px;
		padding: 8px 10px;
		background: #f8fafc;
		border: 1px solid rgba(148, 163, 184, 0.18);
		border-radius: 10px;
		font-size: 0.88rem;
	}
	.top-tx-rank {
		font-weight: 700;
		color: #64748b;
		font-variant-numeric: tabular-nums;
	}
	.top-tx-dot {
		width: 10px;
		height: 10px;
		border-radius: 3px;
	}
	.top-tx-merchant {
		font-weight: 600;
		color: #1f2937;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
	.top-tx-cat {
		font-size: 0.75rem;
		color: #64748b;
	}
	.top-tx-amt {
		font-weight: 700;
		color: #1f2937;
		font-variant-numeric: tabular-nums;
	}
</style>
