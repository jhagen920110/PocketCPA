<script lang="ts">
	import { fly, slide } from 'svelte/transition';
	import type { Analysis } from '$lib/api';
	import DonutChart from './DonutChart.svelte';

	let { analysis }: { analysis: Analysis } = $props();

	const sorted = $derived([...analysis.categories].sort((a, b) => b.total - a.total));

	const COLORS = [
		'#4f46e5', '#06b6d4', '#f59e0b', '#ef4444', '#10b981',
		'#8b5cf6', '#ec4899', '#f97316', '#14b8a6', '#6366f1',
		'#84cc16', '#e11d48', '#0ea5e9', '#a855f7', '#64748b'
	];

	let expandedIndex = $state<number | null>(null);

	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}

	function toggle(i: number) {
		expandedIndex = expandedIndex === i ? null : i;
	}
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

	<!-- Summary -->
	<div class="summary-cards">
		<div class="summary-card">
			<div class="summary-label">Month</div>
			<div class="summary-value">{analysis.month}</div>
		</div>
		<div class="summary-card">
			<div class="summary-label">Total Spent</div>
			<div class="summary-value">${fmt(analysis.totalSpent)}</div>
		</div>
		<div class="summary-card">
			<div class="summary-label">Categories</div>
			<div class="summary-value">{analysis.categories.length}</div>
		</div>
	</div>

	<!-- Category bars (click to expand) -->
	<h3>Spending by Category</h3>
	<p class="hint">Click a category to see every purchase.</p>
	<div class="category-bars">
		{#each sorted as cat, i}
			<div class="category-bar-row" in:slide={{ delay: i * 60, duration: 300 }}>
				<button class="category-bar-button" onclick={() => toggle(i)} aria-expanded={expandedIndex === i}>
					<div class="category-bar-label">
						<span class="cat-name">
							<span class="cat-dot" style="background: {COLORS[i % COLORS.length]}"></span>
							{cat.name}
							<span class="cat-count">({cat.transactions.length})</span>
						</span>
						<span class="cat-amount">${fmt(cat.total)} &middot; {cat.percentage.toFixed(1)}%</span>
					</div>
					<div class="category-bar-track">
						<div
							class="category-bar-fill"
							style="width: {Math.min(cat.percentage, 100)}%; background: {COLORS[i % COLORS.length]}"
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

	<!-- Fun Stats -->
	{#if analysis.funStats && analysis.funStats.length > 0}
		<h3>✨ Fun Stats</h3>
		<div class="fun-stats">
			{#each analysis.funStats as s}
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

	.cat-count {
		color: #667085;
		font-weight: 400;
		font-size: 0.85rem;
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
		background: linear-gradient(135deg, #f5f3ff 0%, #eff6ff 100%);
		border-radius: 10px;
		border: 1px solid #e0e7ff;
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
		background: #eef2ff;
		color: #4338ca;
		border-radius: 999px;
		font-size: 0.82rem;
		font-weight: 600;
	}

	.fun-stat {
		align-items: flex-start;
	}
</style>
