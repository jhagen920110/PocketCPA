<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { fade, fly, slide } from 'svelte/transition';
	import { type LedgerEntry } from '$lib/api';
	import { categoryColor, heatBucket, shortCategory } from '$lib/colors';
	import { ledgerStore, loadLedger, selectedMonth as selectedMonthStore } from '$lib/appState';
	import { get } from 'svelte/store';

	// ---------- state ----------
	let entries = $state<LedgerEntry[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	const unsub = ledgerStore.subscribe((s) => {
		entries = s.entries;
		loading = s.loading;
		error = s.error;
	});

	type ViewMode = 'month' | 'week';
	let viewMode = $state<ViewMode>('month');

	// Anchor date — set once to the latest date with records.
	let anchor = $state<Date | null>(null);

	// Selected day for overlay.
	let selectedIso = $state<string | null>(null);

	// List filter (single-select radio-style) + sort.
	let selectedCategory = $state<string>('all');
	type SortKey = 'date' | 'amount';
	let sortKey = $state<SortKey>('date');
	let sortDir = $state<'asc' | 'desc'>('desc');

	let listOpen = $state(false);
	let expandedCats = $state<Set<string>>(new Set());
	function toggleCat(name: string) {
		const next = new Set(expandedCats);
		if (next.has(name)) next.delete(name);
		else next.add(name);
		expandedCats = next;
	}

	onMount(() => {
		(async () => {
			await loadLedger();
			if (!anchor) {
				const stored = get(selectedMonthStore);
				if (stored) {
					const [y, m] = stored.split('-').map((n) => parseInt(n, 10));
					if (!isNaN(y) && !isNaN(m)) anchor = new Date(y, m - 1, 1);
				}
				if (!anchor) anchor = latestDataDate() ?? startOfDay(new Date());
			}
		})();
		return () => { unsub(); unsubMonth(); };
	});

	// Keep shared month store in sync whenever anchor changes (month view or week view).
	$effect(() => {
		if (!anchor) return;
		const ym = `${anchor.getFullYear()}-${pad(anchor.getMonth() + 1)}`;
		if (get(selectedMonthStore) !== ym) selectedMonthStore.set(ym);
	});

	// React to external changes from other pages (e.g. dashboard switched month).
	const unsubMonth = selectedMonthStore.subscribe((v) => {
		if (!v) return;
		const [y, m] = v.split('-').map((n) => parseInt(n, 10));
		if (isNaN(y) || isNaN(m)) return;
		if (!anchor || anchor.getFullYear() !== y || anchor.getMonth() !== m - 1) {
			anchor = new Date(y, m - 1, 1);
		}
	});

	// Lock body scroll when a day is selected.
	$effect(() => {
		if (typeof document === 'undefined') return;
		if (selectedIso) {
			document.body.style.overflow = 'hidden';
		} else {
			document.body.style.overflow = '';
		}
		return () => {
			document.body.style.overflow = '';
		};
	});

	// ---------- date helpers ----------
	function startOfDay(d: Date): Date {
		const x = new Date(d);
		x.setHours(0, 0, 0, 0);
		return x;
	}
	function pad(n: string | number) {
		return String(n).padStart(2, '0');
	}
	function toIso(d: Date): string {
		return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
	}
	function parseIso(iso: string): Date {
		return new Date(iso + 'T00:00:00');
	}
	function monthNameToNum(name: string): string | null {
		const map: Record<string, string> = {
			jan: '01', feb: '02', mar: '03', apr: '04', may: '05', jun: '06',
			jul: '07', aug: '08', sep: '09', oct: '10', nov: '11', dec: '12'
		};
		return map[name.slice(0, 3).toLowerCase()] ?? null;
	}
	function entryIso(e: LedgerEntry): string | null {
		const s = (e.date ?? '').trim();
		if (!s) return null;
		const iso = s.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/);
		if (iso) return `${iso[1]}-${pad(iso[2])}-${pad(iso[3])}`;
		const full = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{2,4})$/);
		if (full) {
			const m = pad(full[1]);
			const d = pad(full[2]);
			let y = full[3];
			if (y.length === 2) y = (parseInt(y, 10) >= 70 ? '19' : '20') + y;
			return `${y}-${m}-${d}`;
		}
		// M/D without year — use the statement's reported month. If the parsed
		// month > the statement month, the txn is from the PREVIOUS year (e.g.,
		// Dec purchase on a statement that closes in January).
		const md = s.match(/^(\d{1,2})\/(\d{1,2})$/);
		if (md && e.month) {
			const [fy, fm] = e.month.split('-');
			let year = parseInt(fy, 10);
			const parsedMonth = parseInt(md[1], 10);
			if (parsedMonth > parseInt(fm, 10)) year -= 1;
			return `${year}-${pad(md[1])}-${pad(md[2])}`;
		}
		const mon = s.match(/^([A-Za-z]{3,})\s+(\d{1,2})$/);
		if (mon && e.month) {
			const m = monthNameToNum(mon[1]);
			if (m) {
				const [fy, fm] = e.month.split('-');
				let year = parseInt(fy, 10);
				if (parseInt(m, 10) > parseInt(fm, 10)) year -= 1;
				return `${year}-${pad(m)}-${pad(mon[2])}`;
			}
		}
		return null;
	}

	function latestDataDate(): Date | null {
		let latest: string | null = null;
		for (const e of entries) {
			const iso = entryIso(e);
			if (iso && (!latest || iso > latest)) latest = iso;
		}
		return latest ? parseIso(latest) : null;
	}

	// ---------- period bounds ----------
	const period = $derived.by(() => {
		const a = anchor ?? startOfDay(new Date());
		if (viewMode === 'month') {
			const start = new Date(a.getFullYear(), a.getMonth(), 1);
			const end = new Date(a.getFullYear(), a.getMonth() + 1, 0);
			return { start, end };
		}
		const start = new Date(a);
		start.setDate(start.getDate() - start.getDay());
		start.setHours(0, 0, 0, 0);
		const end = new Date(start);
		end.setDate(end.getDate() + 6);
		return { start, end };
	});

	const periodLabel = $derived.by(() => {
		const a = anchor ?? startOfDay(new Date());
		if (viewMode === 'month') {
			return a.toLocaleString(undefined, { month: 'long', year: 'numeric' });
		}
		const s = period.start;
		const e = period.end;
		const sameMonth = s.getMonth() === e.getMonth();
		const sOpts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
		const eOpts: Intl.DateTimeFormatOptions = sameMonth
			? { day: 'numeric', year: 'numeric' }
			: { month: 'short', day: 'numeric', year: 'numeric' };
		return `${s.toLocaleDateString(undefined, sOpts)} – ${e.toLocaleDateString(undefined, eOpts)}`;
	});

	const byDay = $derived.by(() => {
		const map = new Map<string, LedgerEntry[]>();
		for (const e of entries) {
			const iso = entryIso(e);
			if (!iso) continue;
			if (!map.has(iso)) map.set(iso, []);
			map.get(iso)!.push(e);
		}
		return map;
	});

	interface Cell {
		iso: string;
		date: Date;
		inPeriod: boolean;
		spend: number;
		count: number;
	}
	const cells = $derived.by(() => {
		const out: Cell[] = [];
		const a = anchor ?? startOfDay(new Date());
		if (viewMode === 'month') {
			const start = new Date(period.start);
			start.setDate(start.getDate() - start.getDay());
			const end = new Date(period.end);
			end.setDate(end.getDate() + (6 - end.getDay()));
			const cur = new Date(start);
			while (cur <= end) {
				pushCell(out, cur, a);
				cur.setDate(cur.getDate() + 1);
			}
		} else {
			const cur = new Date(period.start);
			for (let i = 0; i < 7; i++) {
				pushCell(out, cur, a);
				cur.setDate(cur.getDate() + 1);
			}
		}
		return out;
	});
	function pushCell(out: Cell[], cur: Date, a: Date) {
		const iso = toIso(cur);
		const dayEntries = byDay.get(iso) ?? [];
		let spend = 0;
		for (const e of dayEntries) if (e.amount > 0) spend += e.amount;
		const inPeriod =
			viewMode === 'week'
				? true
				: cur.getMonth() === a.getMonth() && cur.getFullYear() === a.getFullYear();
		out.push({ iso, date: new Date(cur), inPeriod, spend, count: dayEntries.length });
	}

	const maxCellSpend = $derived(Math.max(0, ...cells.filter((c) => c.inPeriod).map((c) => c.spend)));

	function nudge(dir: -1 | 1) {
		const base = anchor ?? startOfDay(new Date());
		const next = new Date(base);
		if (viewMode === 'month') next.setMonth(next.getMonth() + dir);
		else next.setDate(next.getDate() + 7 * dir);
		anchor = next;
	}
	function setMode(m: ViewMode) {
		viewMode = m;
	}

	function openDay(iso: string) {
		selectedIso = iso;
	}
	function closeDay() {
		selectedIso = null;
	}
	function onDayKey(e: KeyboardEvent, iso: string) {
		if (e.key === 'Enter' || e.key === ' ') {
			e.preventDefault();
			openDay(iso);
		}
	}

	const selectedEntries = $derived.by(() => {
		if (!selectedIso) return [];
		return (byDay.get(selectedIso) ?? []).slice().sort((a, b) => b.amount - a.amount);
	});
	const selectedSpend = $derived(
		selectedEntries.reduce((sum, e) => sum + (e.amount > 0 ? e.amount : 0), 0)
	);

	// ---------- period list ----------
	const periodEntries = $derived.by(() => {
		const startIso = toIso(period.start);
		const endIso = toIso(period.end);
		return entries.filter((e) => {
			const iso = entryIso(e);
			if (!iso) return false;
			return iso >= startIso && iso <= endIso;
		});
	});
	const categories = $derived(
		Array.from(new Set(periodEntries.map((e) => shortCategory(e.category)).filter(Boolean))).sort()
	);
	const periodSummary = $derived.by(() => {
		let spend = 0;
		let refunds = 0;
		for (const e of periodEntries) {
			if (e.amount > 0) spend += e.amount;
			else refunds += -e.amount;
		}
		return { spend, refunds, net: spend - refunds, count: periodEntries.length };
	});
	const filteredSorted = $derived.by(() => {
		let arr = periodEntries;
		if (selectedCategory !== 'all') arr = arr.filter((e) => shortCategory(e.category) === selectedCategory);
		const copy = [...arr];
		const dir = sortDir === 'asc' ? 1 : -1;
		copy.sort((a, b) => {
			if (sortKey === 'amount') return (a.amount - b.amount) * dir;
			const ai = entryIso(a) ?? '';
			const bi = entryIso(b) ?? '';
			if (ai < bi) return -1 * dir;
			if (ai > bi) return 1 * dir;
			return 0;
		});
		return copy;
	});

	// Group the filtered+sorted entries by category for the expandable list.
	// Refunds (amount < 0) are excluded here and shown in their own section.
	interface CatGroup {
		name: string;
		color: string;
		total: number;
		count: number;
		entries: LedgerEntry[];
	}
	const groupedByCat = $derived.by(() => {
		const map = new Map<string, CatGroup>();
		for (const e of filteredSorted) {
			if (e.amount <= 0) continue;
			const name = shortCategory(e.category);
			const cur = map.get(name) ?? {
				name,
				color: categoryColor(name),
				total: 0,
				count: 0,
				entries: []
			};
			cur.total += e.amount;
			cur.count += 1;
			cur.entries.push(e);
			map.set(name, cur);
		}
		return Array.from(map.values()).sort((a, b) => b.total - a.total);
	});

	// Refunds get their own separate section, largest-first.
	const refundEntries = $derived.by(() => {
		return filteredSorted
			.filter((e) => e.amount < 0)
			.slice()
			.sort((a, b) => a.amount - b.amount); // most negative (largest refund) first
	});
	const refundTotal = $derived(
		refundEntries.reduce((sum, e) => sum + -e.amount, 0)
	);
	let refundsOpen = $state(false);
	function setSort(k: SortKey) {
		if (sortKey === k) sortDir = sortDir === 'asc' ? 'desc' : 'asc';
		else {
			sortKey = k;
			sortDir = 'desc';
		}
	}

	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}
	function fmt0(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
	}
	function dayOfMonth(d: Date) {
		return d.getDate();
	}
	function weekdayShort(d: Date) {
		return d.toLocaleDateString(undefined, { weekday: 'short' });
	}
	function fullDayLabel(iso: string) {
		const d = parseIso(iso);
		return d.toLocaleDateString(undefined, {
			weekday: 'long',
			month: 'long',
			day: 'numeric',
			year: 'numeric'
		});
	}

	const todayIso = toIso(startOfDay(new Date()));
</script>

<main class="container" in:fade={{ duration: 300 }}>
	<div class="page-header" in:fly={{ y: 10, duration: 300 }}>
		<h1>📒 Ledger</h1>
		<p class="muted">Your spending, by the day.</p>
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
		<section class="card mode-card">
			<div class="seg" role="tablist" aria-label="View mode">
				<button
					class="seg-btn"
					class:active={viewMode === 'month'}
					onclick={() => setMode('month')}
					role="tab"
					aria-selected={viewMode === 'month'}
				>Month</button>
				<button
					class="seg-btn"
					class:active={viewMode === 'week'}
					onclick={() => setMode('week')}
					role="tab"
					aria-selected={viewMode === 'week'}
				>Week</button>
			</div>
		</section>

		<section class="card calendar-card" in:fly={{ y: 20, duration: 300 }}>
			<div class="cal-header">
				<button class="nav-btn" onclick={() => nudge(-1)} aria-label="Previous">‹</button>
				<div class="cal-title">{periodLabel}</div>
				<button class="nav-btn" onclick={() => nudge(1)} aria-label="Next">›</button>
			</div>

			<div class="weekday-row">
				{#each ['S', 'M', 'T', 'W', 'T', 'F', 'S'] as w}
					<div class="wd">{w}</div>
				{/each}
			</div>

			<div class="cal-grid" class:week-mode={viewMode === 'week'}>
				{#each cells as c (c.iso)}
					<button
						type="button"
						class="cell"
						class:outside={!c.inPeriod}
						class:today={c.iso === todayIso}
						class:has-spend={c.spend > 0}
						class:no-spend={c.spend === 0}
						style="--heat: {heatBucket(c.spend, maxCellSpend)};"
						disabled={c.spend === 0}
						onclick={() => c.spend > 0 && openDay(c.iso)}
						onkeydown={(e) => c.spend > 0 && onDayKey(e, c.iso)}
						aria-label={`${fullDayLabel(c.iso)}${c.spend > 0 ? `, spent $${fmt(c.spend)}` : ', no spending'}`}
					>
						{#if viewMode === 'week'}
							<div class="cell-weekday">{weekdayShort(c.date)}</div>
						{/if}
						<div class="cell-day">{dayOfMonth(c.date)}</div>
						{#if c.spend > 0}
							<div class="cell-amt" class:big={c.spend >= 1000} class:xbig={c.spend >= 10000}>${fmt0(c.spend)}</div>
						{:else}
							<div class="cell-amt muted">—</div>
						{/if}
					</button>
				{/each}
			</div>

			<!-- Period net — compact bottom-right pill with emerald tint -->
			<div class="net-row">
				<div class="net-badge" aria-label="Period net spending">
					<span class="net-label">Net</span>
					<span class="net-value">${fmt0(periodSummary.net)}</span>
				</div>
			</div>
		</section>

		<section class="card list-card" in:fly={{ y: 20, duration: 300, delay: 60 }}>
			<button
				class="list-toggle"
				onclick={() => (listOpen = !listOpen)}
				aria-expanded={listOpen}
			>
				<span>Transactions</span>
				<span class="chev" class:open={listOpen}>▾</span>
			</button>

			{#if listOpen}
				<div transition:slide={{ duration: 180 }}>
					<div class="list-controls">
						<div class="sort-group" role="group" aria-label="Sort">
							<button
								class="sort-btn"
								class:active={sortKey === 'date'}
								onclick={() => setSort('date')}
							>
								Date {sortKey === 'date' ? (sortDir === 'asc' ? '▲' : '▼') : ''}
							</button>
							<button
								class="sort-btn"
								class:active={sortKey === 'amount'}
								onclick={() => setSort('amount')}
							>
								Amount {sortKey === 'amount' ? (sortDir === 'asc' ? '▲' : '▼') : ''}
							</button>
						</div>
					</div>

					<fieldset class="cat-radios">
						<legend class="sr-only">Filter by category</legend>
						<label class="radio-chip" class:active={selectedCategory === 'all'}>
							<input
								type="radio"
								name="category"
								value="all"
								bind:group={selectedCategory}
							/>
							<span class="chip-dot" style="background: #cbd5e1"></span>
							All
						</label>
						{#each categories as c}
							<label class="radio-chip" class:active={selectedCategory === c}>
								<input
									type="radio"
									name="category"
									value={c}
									bind:group={selectedCategory}
								/>
								<span class="chip-dot" style="background: {categoryColor(c)}"></span>
								{c}
							</label>
						{/each}
					</fieldset>

					{#if filteredSorted.length === 0}
						<p class="muted empty-note">No transactions in this period{selectedCategory !== 'all' ? ` for “${selectedCategory}”` : ''}.</p>
					{:else}
						<ul class="cat-groups">
							{#each groupedByCat as g (g.name)}
								{@const isOpen = expandedCats.has(g.name)}
								<li class="cat-group" style="--cat-color: {g.color};">
									<button
										type="button"
										class="cat-group-header"
										onclick={() => toggleCat(g.name)}
										aria-expanded={isOpen}
									>
										<span class="cat-stripe"></span>
										<span class="cat-group-name">{g.name}</span>
										<span class="cat-group-count">{g.count}</span>
										<span class="cat-group-total">${fmt0(g.total)}</span>
										<span class="chev" class:open={isOpen}>▾</span>
									</button>
									{#if isOpen}
										<ul class="txn-list txn-list-inset" transition:slide={{ duration: 160 }}>
											{#each g.entries as e (e.id)}
												<li class="txn">
													<div class="txn-row">
														<div class="txn-left">
															<div class="txn-merchant">{e.merchant || e.description || '—'}</div>
															<div class="txn-meta">
																<span>{e.date}</span>
																{#if e.bank}<span class="dot">•</span><span>{e.bank}</span>{/if}
															</div>
														</div>
														<div class="txn-amt">${fmt(e.amount)}</div>
													</div>
												</li>
											{/each}
										</ul>
									{/if}
								</li>
							{/each}

							{#if refundEntries.length > 0}
								<li class="cat-group refund-group">
									<button
										type="button"
										class="cat-group-header refund-header"
										onclick={() => (refundsOpen = !refundsOpen)}
										aria-expanded={refundsOpen}
									>
										<span class="cat-stripe refund-stripe"></span>
										<span class="cat-group-name">↩ Refunds &amp; credits</span>
										<span class="cat-group-count refund-count">{refundEntries.length}</span>
										<span class="cat-group-total refund-total">+${fmt0(refundTotal)}</span>
										<span class="chev" class:open={refundsOpen}>▾</span>
									</button>
									{#if refundsOpen}
										<ul class="txn-list txn-list-inset" transition:slide={{ duration: 160 }}>
											{#each refundEntries as e (e.id)}
												<li class="txn refund">
													<div class="txn-row">
														<div class="txn-left">
															<div class="txn-merchant">{e.merchant || e.description || '—'}</div>
															<div class="txn-meta">
																<span>{e.date}</span>
																<span class="dot">•</span>
																<span
																	class="cat-pill"
																	style="background: {categoryColor(e.category)}1F; color: {categoryColor(e.category)}"
																>{shortCategory(e.category)}</span>
																{#if e.bank}<span class="dot">•</span><span>{e.bank}</span>{/if}
															</div>
														</div>
														<div class="txn-amt">+${fmt(Math.abs(e.amount))}</div>
													</div>
												</li>
											{/each}
										</ul>
									{/if}
								</li>
							{/if}
						</ul>
					{/if}
				</div>
			{/if}
		</section>
	{/if}
</main>

<!-- Day detail overlay -->
{#if selectedIso}
	<div
		class="modal-backdrop"
		in:fade={{ duration: 150 }}
		out:fade={{ duration: 120 }}
		onclick={closeDay}
		onkeydown={(e) => { if (e.key === 'Escape') closeDay(); }}
		role="button"
		tabindex="-1"
		aria-label="Close day details"
	>
		<div
			class="modal"
			in:fly={{ y: 30, duration: 220 }}
			out:fade={{ duration: 120 }}
			onclick={(e) => e.stopPropagation()}
			onkeydown={(e) => e.stopPropagation()}
			role="dialog"
			aria-modal="true"
			aria-label="Day spending"
			tabindex="-1"
		>
			<div class="modal-header">
				<div>
					<div class="modal-day">{fullDayLabel(selectedIso)}</div>
					<div class="modal-total">
						${fmt(selectedSpend)}
					</div>
				</div>
				<button class="modal-close" onclick={closeDay} aria-label="Close">×</button>
			</div>

			{#if selectedEntries.length === 0}
				<p class="muted" style="padding: 20px 4px;">No transactions on this day.</p>
			{:else}
				<ul class="txn-list">
					{#each selectedEntries as e (e.id)}
						<li class="txn" class:refund={e.amount < 0}>
							<div class="txn-row">
								<div class="txn-left">
									<div class="txn-merchant">{e.merchant || e.description || '—'}</div>
									<div class="txn-meta">
										<span
											class="cat-pill"
											style="background: {categoryColor(e.category)}1F; color: {categoryColor(e.category)}"
										>{shortCategory(e.category)}</span>
										{#if e.bank}<span class="dot">•</span><span>{e.bank}</span>{/if}
									</div>
								</div>
								<div class="txn-amt">{e.amount < 0 ? '+' : ''}${fmt(Math.abs(e.amount))}</div>
							</div>
						</li>
					{/each}
				</ul>
			{/if}
		</div>
	</div>
{/if}

<style>
	.container {
		max-width: 760px;
		margin: 16px auto;
		padding: 0 14px 40px;
		display: flex;
		flex-direction: column;
		gap: 14px;
	}

	.page-header h1 {
		font-size: 1.5rem;
	}
	.page-header p {
		margin-top: 2px;
		font-size: 0.9rem;
	}
	.muted {
		color: #667085;
	}

	.card {
		background: #ffffff;
		border: 1px solid rgba(148, 163, 184, 0.18);
		border-radius: 16px;
		box-shadow: 0 4px 14px rgba(15, 23, 42, 0.04);
		padding: 14px;
	}

	.mode-card {
		padding: 8px;
	}
	.seg {
		display: grid;
		grid-template-columns: 1fr 1fr;
		background: #f1f5f9;
		border-radius: 12px;
		padding: 4px;
		gap: 4px;
	}
	.seg-btn {
		border: none;
		background: transparent;
		padding: 10px 12px;
		font-size: 0.95rem;
		font-weight: 600;
		color: #475569;
		border-radius: 10px;
		cursor: pointer;
		transition: background 0.15s, color 0.15s, box-shadow 0.15s;
	}
	.seg-btn.active {
		background: #ffffff;
		color: #065f46;
		box-shadow: 0 2px 6px rgba(15, 23, 42, 0.08);
	}

	.cal-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 8px;
		margin-bottom: 10px;
	}
	.cal-title {
		font-weight: 700;
		font-size: 1.05rem;
		text-align: center;
		flex: 1;
	}
	.nav-btn {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		border: 1px solid #e2e8f0;
		background: #ffffff;
		font-size: 1.3rem;
		line-height: 1;
		color: #334155;
		cursor: pointer;
		flex-shrink: 0;
	}
	.nav-btn:active {
		background: #f1f5f9;
	}

	.net-row {
		display: flex;
		justify-content: flex-end;
		margin-top: 10px;
	}
	.net-badge {
		display: inline-flex;
		align-items: baseline;
		gap: 6px;
		padding: 0;
		background: transparent;
		border: none;
		box-shadow: none;
		font-variant-numeric: tabular-nums;
	}
	.net-label {
		font-size: 0.72rem;
		color: #64748b;
		text-transform: uppercase;
		letter-spacing: 0.08em;
		font-weight: 600;
	}
	.net-value {
		font-size: 0.95rem;
		font-weight: 700;
		color: #1f2937;
	}

	.weekday-row {
		display: grid;
		grid-template-columns: repeat(7, 1fr);
		gap: 4px;
		margin-bottom: 4px;
	}
	.wd {
		text-align: center;
		font-size: 0.7rem;
		font-weight: 600;
		color: #94a3b8;
		text-transform: uppercase;
		letter-spacing: 0.04em;
	}

	.cal-grid {
		display: grid;
		grid-template-columns: repeat(7, minmax(0, 1fr));
		gap: 4px;
	}
	.cal-grid.week-mode {
		gap: 6px;
	}

	.cell {
		position: relative;
		aspect-ratio: 1 / 1;
		border-radius: 10px;
		border: 1px solid rgba(148, 163, 184, 0.2);
		background: var(--heat, transparent);
		cursor: pointer;
		padding: 4px;
		display: flex;
		flex-direction: column;
		align-items: flex-start;
		justify-content: space-between;
		font-family: inherit;
		color: #1f2937;
		transition: transform 0.08s;
	}
	.cell:active {
		transform: scale(0.97);
	}
	.cell.no-spend {
		cursor: default;
	}
	.cell.no-spend:active {
		transform: none;
	}
	.cell.outside {
		opacity: 0.35;
	}
	.cell.today {
		border-color: #059669;
		box-shadow: 0 0 0 2px rgba(5, 150, 105, 0.2);
	}
	.cell-day {
		font-size: 0.8rem;
		font-weight: 600;
	}
	.cell-amt {
		font-size: 0.7rem;
		font-weight: 700;
		font-variant-numeric: tabular-nums;
		align-self: flex-end;
		color: #1e293b;
		max-width: 100%;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}
	.cell-amt.big {
		font-size: 0.6rem;
	}
	.cell-amt.xbig {
		font-size: 0.52rem;
	}
	.cell-amt.muted {
		color: #cbd5e1;
		font-weight: 400;
	}
	.cell-weekday {
		font-size: 0.6rem;
		font-weight: 600;
		color: #64748b;
		text-transform: uppercase;
	}
	.cal-grid.week-mode .cell {
		aspect-ratio: auto;
		min-height: 80px;
	}

	.list-toggle {
		width: 100%;
		display: flex;
		align-items: center;
		justify-content: space-between;
		background: transparent;
		border: none;
		padding: 6px 2px;
		font-size: 1rem;
		font-weight: 600;
		color: #1f2937;
		cursor: pointer;
	}
	.chev {
		transition: transform 0.18s ease;
	}
	.chev.open {
		transform: rotate(180deg);
	}

	.list-controls {
		display: flex;
		justify-content: flex-end;
		margin: 10px 0 8px;
	}
	.sort-group {
		display: inline-flex;
		gap: 4px;
		background: #f1f5f9;
		padding: 4px;
		border-radius: 10px;
	}
	.sort-btn {
		border: none;
		background: transparent;
		padding: 6px 12px;
		font-size: 0.82rem;
		font-weight: 600;
		color: #475569;
		cursor: pointer;
		border-radius: 8px;
	}
	.sort-btn.active {
		background: #ffffff;
		color: #065f46;
		box-shadow: 0 1px 3px rgba(15, 23, 42, 0.08);
	}

	.cat-radios {
		border: none;
		padding: 0;
		margin: 0 0 10px;
		display: flex;
		flex-wrap: wrap;
		gap: 6px;
	}
	.sr-only {
		position: absolute;
		width: 1px;
		height: 1px;
		padding: 0;
		margin: -1px;
		overflow: hidden;
		clip: rect(0, 0, 0, 0);
		white-space: nowrap;
		border: 0;
	}
	.radio-chip {
		display: inline-flex;
		align-items: center;
		gap: 6px;
		padding: 6px 12px;
		border-radius: 999px;
		border: 1px solid #e2e8f0;
		background: #ffffff;
		font-size: 0.82rem;
		font-weight: 600;
		color: #475569;
		cursor: pointer;
		user-select: none;
	}
	.radio-chip input {
		position: absolute;
		opacity: 0;
		pointer-events: none;
	}
	.radio-chip.active {
		background: #ecfdf5;
		border-color: #059669;
		color: #065f46;
	}
	.chip-dot {
		width: 10px;
		height: 10px;
		border-radius: 3px;
		flex-shrink: 0;
	}

	.txn-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
		gap: 6px;
	}
	.txn-list-inset {
		padding: 8px 10px 10px;
		background: #f8fafc;
	}
	.cat-groups {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
		gap: 8px;
	}
	.cat-group {
		border: 1px solid color-mix(in srgb, var(--cat-color, #cbd5e1) 35%, #e2e8f0);
		border-radius: 12px;
		overflow: hidden;
		background: #ffffff;
	}
	.cat-group-header {
		width: 100%;
		display: grid;
		grid-template-columns: 8px 1fr auto auto 20px;
		align-items: center;
		gap: 10px;
		padding: 10px 12px;
		background: linear-gradient(
			90deg,
			color-mix(in srgb, var(--cat-color, #cbd5e1) 18%, white) 0%,
			color-mix(in srgb, var(--cat-color, #cbd5e1) 6%, white) 100%
		);
		border: none;
		cursor: pointer;
		font-family: inherit;
		text-align: left;
		transition: background 0.18s;
	}
	.cat-group-header:hover {
		background: linear-gradient(
			90deg,
			color-mix(in srgb, var(--cat-color, #cbd5e1) 28%, white) 0%,
			color-mix(in srgb, var(--cat-color, #cbd5e1) 12%, white) 100%
		);
	}
	.cat-stripe {
		width: 8px;
		height: 28px;
		border-radius: 4px;
		background: var(--cat-color, #cbd5e1);
		box-shadow: 0 1px 4px color-mix(in srgb, var(--cat-color, #cbd5e1) 40%, transparent);
	}
	.cat-group-name {
		font-size: 0.95rem;
		font-weight: 700;
		color: #1f2937;
	}
	.cat-group-count {
		font-size: 0.72rem;
		font-weight: 700;
		color: white;
		background: var(--cat-color, #94a3b8);
		padding: 2px 8px;
		border-radius: 999px;
		min-width: 26px;
		text-align: center;
	}
	.cat-group-total {
		font-size: 0.95rem;
		font-weight: 700;
		color: #1f2937;
		font-variant-numeric: tabular-nums;
	}

	/* Refund group — distinct emerald styling so it reads as "money back". */
	.cat-group.refund-group {
		border-color: #6ee7b7;
	}
	.refund-header {
		background: linear-gradient(90deg, #ecfdf5 0%, #f0fdf4 100%) !important;
	}
	.refund-header:hover {
		background: linear-gradient(90deg, #d1fae5 0%, #ecfdf5 100%) !important;
	}
	.refund-stripe {
		background: #10b981 !important;
		box-shadow: 0 1px 4px rgba(16, 185, 129, 0.4);
	}
	.refund-count {
		background: #10b981 !important;
	}
	.refund-total {
		color: #059669 !important;
	}
	.txn.refund .txn-amt {
		color: #059669;
	}
	.txn {
		border: 1px solid rgba(148, 163, 184, 0.2);
		border-radius: 12px;
		overflow: hidden;
		background: #ffffff;
	}
	.txn-row {
		width: 100%;
		display: flex;
		align-items: center;
		gap: 10px;
		padding: 10px 12px;
	}
	.txn-left {
		flex: 1;
		min-width: 0;
	}
	.txn-merchant {
		font-weight: 600;
		font-size: 0.95rem;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
	.txn-meta {
		display: flex;
		align-items: center;
		gap: 6px;
		font-size: 0.78rem;
		color: #64748b;
		margin-top: 2px;
		flex-wrap: wrap;
	}
	.dot {
		color: #cbd5e1;
	}
	.cat-pill {
		display: inline-block;
		padding: 2px 8px;
		border-radius: 999px;
		font-size: 0.72rem;
		font-weight: 600;
	}
	.txn-amt {
		font-weight: 700;
		font-variant-numeric: tabular-nums;
		font-size: 0.95rem;
	}
	.txn.refund .txn-amt {
		color: #059669;
	}
	.empty-note {
		padding: 14px 4px;
		text-align: center;
	}

	.modal-backdrop {
		position: fixed;
		inset: 0;
		background: rgba(15, 23, 42, 0.55);
		backdrop-filter: blur(4px);
		display: flex;
		align-items: flex-end;
		justify-content: center;
		z-index: 100;
		overscroll-behavior: contain;
		touch-action: none;
	}
	.modal {
		width: 100%;
		max-width: 560px;
		max-height: 85vh;
		overflow-y: auto;
		background: #ffffff;
		border-radius: 18px 18px 0 0;
		padding: 16px 14px 28px;
		box-shadow: 0 -10px 40px rgba(0, 0, 0, 0.25);
		overscroll-behavior: contain;
		touch-action: pan-y;
	}
	.modal-header {
		display: flex;
		align-items: flex-start;
		justify-content: space-between;
		gap: 8px;
		margin-bottom: 12px;
	}
	.modal-day {
		font-weight: 700;
		font-size: 1rem;
	}
	.modal-total {
		font-size: 1.1rem;
		font-weight: 700;
		margin-top: 2px;
		font-variant-numeric: tabular-nums;
	}
	.modal-close {
		background: transparent;
		border: none;
		font-size: 1.6rem;
		line-height: 1;
		cursor: pointer;
		color: #64748b;
		padding: 4px 10px;
		border-radius: 8px;
	}
	.modal-close:hover {
		background: rgba(0, 0, 0, 0.05);
	}

	@media (min-width: 768px) {
		.modal-backdrop {
			align-items: center;
			padding: 24px;
		}
		.modal {
			border-radius: 18px;
		}
	}
</style>
