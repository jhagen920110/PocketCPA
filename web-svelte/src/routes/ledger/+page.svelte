<script lang="ts">
	import { onMount } from 'svelte';
	import { fade, fly } from 'svelte/transition';
	import { getLedger, type LedgerEntry } from '$lib/api';
	import SpendingHeatmap from '$lib/components/SpendingHeatmap.svelte';

	let entries = $state<LedgerEntry[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	// Filters
	let query = $state('');
	let selectedCategory = $state<string>('all');
	let selectedMonth = $state<string>('all');
	let selectedBank = $state<string>('all');
	let minAmount = $state<string>('');
	let maxAmount = $state<string>('');

	// Sort
	type SortKey = 'date' | 'amount' | 'category' | 'merchant';
	let sortKey = $state<SortKey>('date');
	let sortDir = $state<'asc' | 'desc'>('desc');

	onMount(async () => {
		try {
			entries = await getLedger();
		} catch (e: any) {
			error = e?.message ?? 'Failed to load ledger';
		} finally {
			loading = false;
		}
	});

	const categories = $derived(
		Array.from(new Set(entries.map((e) => e.category))).sort()
	);
	const months = $derived(
		Array.from(new Set(entries.map((e) => e.month))).filter(Boolean).sort().reverse()
	);
	const banks = $derived(
		Array.from(new Set(entries.map((e) => e.bank).filter((b): b is string => !!b))).sort()
	);

	// Parse a date string + analysis month ("YYYY-MM") into ISO YYYY-MM-DD, best-effort.
	function toIso(dateStr: string, fallbackMonth: string): string | null {
		if (!dateStr) return null;
		const s = dateStr.trim();

		// Already ISO-ish
		const iso = s.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/);
		if (iso) return `${iso[1]}-${pad(iso[2])}-${pad(iso[3])}`;

		// MM/DD/YY or MM/DD/YYYY
		const full = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{2,4})$/);
		if (full) {
			const m = pad(full[1]);
			const d = pad(full[2]);
			let y = full[3];
			if (y.length === 2) y = (parseInt(y, 10) >= 70 ? '19' : '20') + y;
			return `${y}-${m}-${d}`;
		}

		// MM/DD  (no year)
		const md = s.match(/^(\d{1,2})\/(\d{1,2})$/);
		if (md && fallbackMonth) {
			const [fy] = fallbackMonth.split('-');
			return `${fy}-${pad(md[1])}-${pad(md[2])}`;
		}

		// "Dec 18" style
		const mon = s.match(/^([A-Za-z]{3,})\s+(\d{1,2})$/);
		if (mon && fallbackMonth) {
			const m = monthNameToNum(mon[1]);
			const [fy] = fallbackMonth.split('-');
			if (m) return `${fy}-${pad(m)}-${pad(mon[2])}`;
		}
		return null;
	}

	function pad(n: string | number) {
		return String(n).padStart(2, '0');
	}

	function monthNameToNum(name: string): string | null {
		const map: Record<string, string> = {
			jan: '01', feb: '02', mar: '03', apr: '04', may: '05', jun: '06',
			jul: '07', aug: '08', sep: '09', oct: '10', nov: '11', dec: '12'
		};
		return map[name.slice(0, 3).toLowerCase()] ?? null;
	}

	const filtered = $derived.by(() => {
		const q = query.trim().toLowerCase();
		const minA = minAmount === '' ? null : parseFloat(minAmount);
		const maxA = maxAmount === '' ? null : parseFloat(maxAmount);
		return entries.filter((e) => {
			if (selectedCategory !== 'all' && e.category !== selectedCategory) return false;
			if (selectedMonth !== 'all' && e.month !== selectedMonth) return false;
			if (selectedBank !== 'all' && (e.bank ?? '') !== selectedBank) return false;
			if (minA !== null && !isNaN(minA) && e.amount < minA) return false;
			if (maxA !== null && !isNaN(maxA) && e.amount > maxA) return false;
			if (q) {
				const hay = `${e.merchant} ${e.description} ${e.category} ${e.bank ?? ''}`.toLowerCase();
				if (!hay.includes(q)) return false;
			}
			return true;
		});
	});

	const sorted = $derived.by(() => {
		const arr = [...filtered];
		const dir = sortDir === 'asc' ? 1 : -1;
		arr.sort((a, b) => {
			let va: string | number;
			let vb: string | number;
			switch (sortKey) {
				case 'amount':
					va = a.amount; vb = b.amount; break;
				case 'category':
					va = a.category; vb = b.category; break;
				case 'merchant':
					va = (a.merchant || a.description).toLowerCase();
					vb = (b.merchant || b.description).toLowerCase();
					break;
				case 'date':
				default:
					va = toIso(a.date, a.month) ?? '';
					vb = toIso(b.date, b.month) ?? '';
			}
			if (va < vb) return -1 * dir;
			if (va > vb) return 1 * dir;
			return 0;
		});
		return arr;
	});

	// Summary stats over filtered set
	const summary = $derived.by(() => {
		let income = 0, spend = 0;
		for (const e of filtered) {
			if (e.amount > 0) spend += e.amount;
			else income += -e.amount;
		}
		return { income, spend, net: spend - income, count: filtered.length };
	});

	function setSort(k: SortKey) {
		if (sortKey === k) {
			sortDir = sortDir === 'asc' ? 'desc' : 'asc';
		} else {
			sortKey = k;
			sortDir = k === 'date' || k === 'amount' ? 'desc' : 'asc';
		}
	}

	function clearFilters() {
		query = '';
		selectedCategory = 'all';
		selectedMonth = 'all';
		selectedBank = 'all';
		minAmount = '';
		maxAmount = '';
	}

	// Heatmap data: map iso-date → total spend
	const heatmapData = $derived.by(() => {
		const map = new Map<string, number>();
		for (const e of filtered) {
			if (e.amount <= 0) continue;
			const iso = toIso(e.date, e.month);
			if (!iso) continue;
			map.set(iso, (map.get(iso) ?? 0) + e.amount);
		}
		return map;
	});

	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}
</script>

<main class="container" in:fade={{ duration: 300 }}>
	<div class="page-header" in:fly={{ y: 10, duration: 300 }}>
		<div>
			<h1>📒 Household Ledger <span class="ko">가계부</span></h1>
			<p class="muted">Every transaction across every analysis, in one place.</p>
		</div>
	</div>

	{#if loading}
		<div class="card"><p>Loading your ledger…</p></div>
	{:else if error}
		<div class="card"><p style="color:#ef4444">{error}</p></div>
	{:else if entries.length === 0}
		<div class="card">
			<h3>No transactions yet</h3>
			<p class="muted">Upload a statement and run an analysis — your records will appear here.</p>
		</div>
	{:else}
		<!-- Magic: Spending Heatmap -->
		<section class="card" in:fly={{ y: 20, duration: 400 }}>
			<h2>🔥 Spending Heatmap</h2>
			<p class="hint">The calendar of your money. Darker = heavier spending day.</p>
			<SpendingHeatmap data={heatmapData} />
		</section>

		<!-- Summary for filtered set -->
		<section class="card summary-row" in:fly={{ y: 20, duration: 400, delay: 60 }}>
			<div class="sum-item">
				<div class="sum-label">Transactions</div>
				<div class="sum-value">{summary.count}</div>
			</div>
			<div class="sum-item">
				<div class="sum-label">Spent</div>
				<div class="sum-value">${fmt(summary.spend)}</div>
			</div>
			<div class="sum-item">
				<div class="sum-label">Refunds</div>
				<div class="sum-value">${fmt(summary.income)}</div>
			</div>
			<div class="sum-item">
				<div class="sum-label">Net</div>
				<div class="sum-value">${fmt(summary.net)}</div>
			</div>
		</section>

		<!-- Filters -->
		<section class="card" in:fly={{ y: 20, duration: 400, delay: 120 }}>
			<div class="filters">
				<input
					type="text"
					placeholder="Search merchant, description, category…"
					bind:value={query}
					class="filter-input grow"
				/>
				<select bind:value={selectedCategory} class="filter-input">
					<option value="all">All categories</option>
					{#each categories as c}
						<option value={c}>{c}</option>
					{/each}
				</select>
				<select bind:value={selectedMonth} class="filter-input">
					<option value="all">All months</option>
					{#each months as m}
						<option value={m}>{m}</option>
					{/each}
				</select>
				{#if banks.length > 0}
					<select bind:value={selectedBank} class="filter-input">
						<option value="all">All banks</option>
						{#each banks as b}
							<option value={b}>🏦 {b}</option>
						{/each}
					</select>
				{/if}
				<input
					type="number"
					inputmode="decimal"
					placeholder="Min $"
					bind:value={minAmount}
					class="filter-input small"
				/>
				<input
					type="number"
					inputmode="decimal"
					placeholder="Max $"
					bind:value={maxAmount}
					class="filter-input small"
				/>
				<button class="btn btn-sm" onclick={clearFilters}>Clear</button>
			</div>
		</section>

		<!-- Ledger table -->
		<section class="card" in:fly={{ y: 20, duration: 400, delay: 180 }}>
			<div class="table-wrap">
				<table class="ledger">
					<thead>
						<tr>
							<th>
								<button class="th-btn" onclick={() => setSort('date')}>
									Date {sortKey === 'date' ? (sortDir === 'asc' ? '▲' : '▼') : ''}
								</button>
							</th>
							<th>
								<button class="th-btn" onclick={() => setSort('merchant')}>
									Merchant {sortKey === 'merchant' ? (sortDir === 'asc' ? '▲' : '▼') : ''}
								</button>
							</th>
							<th>
								<button class="th-btn" onclick={() => setSort('category')}>
									Category {sortKey === 'category' ? (sortDir === 'asc' ? '▲' : '▼') : ''}
								</button>
							</th>
							<th class="num">
								<button class="th-btn" onclick={() => setSort('amount')}>
									Amount {sortKey === 'amount' ? (sortDir === 'asc' ? '▲' : '▼') : ''}
								</button>
							</th>
						</tr>
					</thead>
					<tbody>
						{#each sorted as e (e.id)}
							<tr>
								<td class="date">{e.date}<div class="month-sub">{e.month}{e.bank ? ' · ' + e.bank : ''}</div></td>
								<td class="merchant">
									<div class="m-name">{e.merchant || e.description}</div>
									{#if e.merchant && e.description && e.merchant !== e.description}
										<div class="m-desc" title={e.description}>{e.description}</div>
									{/if}
								</td>
								<td><span class="cat-pill">{e.category}</span></td>
								<td class="num" class:refund={e.amount < 0}>${fmt(e.amount)}</td>
							</tr>
						{:else}
							<tr><td colspan="4" class="muted" style="text-align:center;padding:20px;">No matching transactions.</td></tr>
						{/each}
					</tbody>
				</table>
			</div>
		</section>
	{/if}
</main>

<style>
	.container {
		max-width: 1000px;
		margin: 24px auto;
		padding: 0 20px 60px;
		display: flex;
		flex-direction: column;
		gap: 20px;
	}

	.page-header {
		display: flex;
		align-items: flex-end;
		justify-content: space-between;
		gap: 16px;
		flex-wrap: wrap;
	}

	h1 {
		font-size: 1.6rem;
	}

	.ko {
		font-size: 1rem;
		color: #667085;
		font-weight: 500;
		margin-left: 4px;
	}

	h2 {
		font-size: 1.15rem;
		margin-bottom: 4px;
	}

	.hint {
		color: #667085;
		font-size: 0.85rem;
		margin-bottom: 12px;
	}

	.summary-row {
		display: grid;
		grid-template-columns: repeat(4, 1fr);
		gap: 12px;
		padding: 16px 20px;
	}

	.sum-item {
		text-align: center;
	}

	.sum-label {
		font-size: 0.75rem;
		color: #667085;
		text-transform: uppercase;
		letter-spacing: 0.04em;
	}

	.sum-value {
		font-size: 1.15rem;
		font-weight: 700;
		margin-top: 2px;
	}

	.filters {
		display: flex;
		flex-wrap: wrap;
		gap: 8px;
	}

	.filter-input {
		padding: 8px 12px;
		border: 1px solid #d1d5db;
		border-radius: 8px;
		font-size: 0.9rem;
		background: #fff;
	}

	.filter-input.grow {
		flex: 1 1 220px;
	}

	.filter-input.small {
		width: 100px;
	}

	.table-wrap {
		overflow-x: auto;
	}

	table.ledger {
		width: 100%;
		border-collapse: collapse;
		font-size: 0.9rem;
	}

	table.ledger th,
	table.ledger td {
		padding: 10px 12px;
		text-align: left;
		border-bottom: 1px solid rgba(148, 163, 184, 0.18);
		vertical-align: top;
	}

	table.ledger .num {
		text-align: right;
		font-variant-numeric: tabular-nums;
		font-weight: 600;
	}

	table.ledger td.refund {
		color: #059669;
	}

	.th-btn {
		background: none;
		border: none;
		font-weight: 700;
		color: #374151;
		cursor: pointer;
		padding: 0;
		font-size: 0.85rem;
		text-transform: uppercase;
		letter-spacing: 0.04em;
	}

	.th-btn:hover {
		color: #4f46e5;
	}

	td.date {
		color: #1f2937;
		font-variant-numeric: tabular-nums;
		white-space: nowrap;
	}

	.month-sub {
		font-size: 0.72rem;
		color: #9ca3af;
		margin-top: 2px;
	}

	.m-name {
		font-weight: 600;
	}

	.m-desc {
		font-size: 0.78rem;
		color: #9ca3af;
		max-width: 360px;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.cat-pill {
		display: inline-block;
		padding: 2px 10px;
		background: #eef2ff;
		color: #4338ca;
		border-radius: 999px;
		font-size: 0.78rem;
		font-weight: 600;
	}

	@media (max-width: 600px) {
		.summary-row {
			grid-template-columns: repeat(2, 1fr);
		}
	}
</style>
