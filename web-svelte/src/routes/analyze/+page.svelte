<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { fade, fly, slide } from 'svelte/transition';
	import type { AnalysisSummary, Analysis, SpendingCategory, Transaction, FunStat } from '$lib/api';
	import UploadAndAnalyze from '$lib/components/UploadAndAnalyze.svelte';
	import AnalysisResults from '$lib/components/AnalysisResults.svelte';
	import PastAnalyses from '$lib/components/PastAnalyses.svelte';
	import { isAnalyzing, analysesStore, loadAnalyses, upsertAnalysis, invalidateLedger } from '$lib/appState';
	import { shortCategory } from '$lib/colors';

	let summaries = $state<AnalysisSummary[]>([]);
	let ready = $state(false);
	let uploadOpen = $state(false);
	let pastOpen = $state(false);

	// Cache of loaded full analyses.
	const cache = new Map<string, Analysis>();
	let loadedAnalyses = $state<Analysis[]>([]);
	let loadingMonth = $state(false);

	const unsub = analysesStore.subscribe((s) => {
		summaries = s.summaries;
		loadedAnalyses = s.analyses;
		for (const a of s.analyses) cache.set(a.id, a);
		if (s.loaded) ready = true;
	});

	// ---------- Date parsing (re-used, matches Dashboard/Ledger) ----------
	function parseTxnIso(dateStr: string, fallbackMonth: string): string | null {
		const s = (dateStr ?? '').trim();
		if (!s) return null;
		const pad = (n: string | number) => String(n).padStart(2, '0');
		let m = s.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/);
		if (m) return `${m[1]}-${pad(m[2])}-${pad(m[3])}`;
		m = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{2,4})$/);
		if (m) {
			let y = m[3];
			if (y.length === 2) y = (parseInt(y, 10) >= 70 ? '19' : '20') + y;
			return `${y}-${pad(m[1])}-${pad(m[2])}`;
		}
		m = s.match(/^(\d{1,2})\/(\d{1,2})$/);
		if (m && fallbackMonth) {
			const [fy, fm] = fallbackMonth.split('-');
			let year = parseInt(fy, 10);
			const parsedMonth = parseInt(m[1], 10);
			if (parsedMonth > parseInt(fm, 10)) year -= 1;
			return `${year}-${pad(m[1])}-${pad(m[2])}`;
		}
		const mon = s.match(/^([A-Za-z]{3,})\s+(\d{1,2})$/);
		if (mon && fallbackMonth) {
			const monMap: Record<string, number> = {
				jan: 1, feb: 2, mar: 3, apr: 4, may: 5, jun: 6,
				jul: 7, aug: 8, sep: 9, oct: 10, nov: 11, dec: 12
			};
			const pm = monMap[mon[1].slice(0, 3).toLowerCase()];
			if (pm) {
				const [fy, fm] = fallbackMonth.split('-');
				let year = parseInt(fy, 10);
				if (pm > parseInt(fm, 10)) year -= 1;
				return `${year}-${pad(pm)}-${pad(mon[2])}`;
			}
		}
		return null;
	}

	// Months from actual transaction dates (not statement cycles).
	const months = $derived.by(() => {
		const set = new Set<string>();
		for (const a of loadedAnalyses) {
			for (const c of a.categories) {
				for (const t of c.transactions) {
					if (t.amount <= 0) continue;
					const iso = parseTxnIso(t.date, a.month);
					if (iso) set.add(iso.slice(0, 7));
				}
			}
		}
		return Array.from(set).sort().reverse();
	});

	let selectedMonth = $state<string | null>(null);

	// Build a synthesized Analysis per (month, bank) by re-bucketing
	// transactions into their actual calendar month.
	const monthAnalyses = $derived.by<Analysis[]>(() => {
		if (!selectedMonth) return [];
		const target = selectedMonth;

		// Group by bank (or "" if no bank) within this month.
		interface PerBank {
			bank: string | undefined;
			analyzedAt: string;
			// cat short-name -> { orig category name (first seen), txns, total }
			catMap: Map<string, { origName: string; total: number; txns: Transaction[] }>;
			contributors: Analysis[];
			totalSpent: number;
		}
		const banks = new Map<string, PerBank>();

		for (const a of loadedAnalyses) {
			// Does this analysis have any txn that falls in the target month?
			let hit = false;
			const key = a.bank ?? '';
			for (const c of a.categories) {
				for (const t of c.transactions) {
					if (t.amount <= 0) continue;
					const iso = parseTxnIso(t.date, a.month);
					if (!iso || !iso.startsWith(target)) continue;
					hit = true;
					let pb = banks.get(key);
					if (!pb) {
						pb = {
							bank: a.bank,
							analyzedAt: a.analyzedAt,
							catMap: new Map(),
							contributors: [],
							totalSpent: 0
						};
						banks.set(key, pb);
					}
					const catShort = shortCategory(c.name);
					const bucket = pb.catMap.get(catShort) ?? { origName: catShort, total: 0, txns: [] };
					bucket.total += t.amount;
					bucket.txns.push({ ...t, date: iso });
					pb.catMap.set(catShort, bucket);
					pb.totalSpent += t.amount;
				}
			}
			if (hit) {
				const pb = banks.get(key)!;
				if (!pb.contributors.includes(a)) pb.contributors.push(a);
				// Prefer the latest analyzedAt among contributors.
				if (a.analyzedAt > pb.analyzedAt) pb.analyzedAt = a.analyzedAt;
			}
		}

		const out: Analysis[] = [];
		for (const [, pb] of banks) {
			const categories: SpendingCategory[] = Array.from(pb.catMap.entries())
				.map(([name, v]) => ({
					name,
					total: v.total,
					percentage: pb.totalSpent > 0 ? (v.total / pb.totalSpent) * 100 : 0,
					transactions: v.txns
				}))
				.sort((a, b) => b.total - a.total);

			// Merge insights/suggestions/funStats from contributing analyses (de-duped).
			const insights: string[] = [];
			const suggestions: string[] = [];
			const funStats: FunStat[] = [];
			const seenS = new Set<string>();
			for (const c of pb.contributors) {
				for (const ins of c.insights ?? []) if (!seenS.has('i:' + ins)) { seenS.add('i:' + ins); insights.push(ins); }
				for (const sg of c.suggestions ?? []) if (!seenS.has('s:' + sg)) { seenS.add('s:' + sg); suggestions.push(sg); }
				for (const fs of c.funStats ?? []) {
					const k = `f:${fs.label}|${fs.value}`;
					if (!seenS.has(k)) { seenS.add(k); funStats.push(fs); }
				}
			}

			const syntheticId = `${target}__${pb.bank ?? 'all'}`;
			out.push({
				id: syntheticId,
				month: target,
				bank: pb.bank,
				analyzedAt: pb.analyzedAt,
				totalSpent: pb.totalSpent,
				categories,
				insights,
				suggestions,
				funStats
			});
		}
		// Sort by bank name for stable order.
		out.sort((a, b) => (a.bank ?? '').localeCompare(b.bank ?? ''));
		return out;
	});

	async function refreshAnalyses() {
		try {
			loadingMonth = true;
			await loadAnalyses(true);
			if (selectedMonth && !months.includes(selectedMonth)) {
				selectedMonth = months[0] ?? null;
			}
			if (!selectedMonth && months.length > 0) {
				selectedMonth = months[0];
			}
			loadingMonth = false;
		} catch {
			loadingMonth = false;
		}
	}

	function navMonth(dir: -1 | 1) {
		if (!selectedMonth || months.length === 0) return;
		const idx = months.indexOf(selectedMonth);
		// months[] is desc; "previous calendar month" = idx + 1
		const nextIdx = dir === -1 ? idx + 1 : idx - 1;
		if (nextIdx < 0 || nextIdx >= months.length) return;
		selectedMonth = months[nextIdx];
	}

	const hasPrev = $derived.by(() => {
		if (!selectedMonth) return false;
		return months.indexOf(selectedMonth) < months.length - 1;
	});
	const hasNext = $derived.by(() => {
		if (!selectedMonth) return false;
		return months.indexOf(selectedMonth) > 0;
	});

	function openUpload() {
		uploadOpen = true;
	}
	function closeUpload() {
		if ($isAnalyzing) return;
		uploadOpen = false;
	}
	function onModalKey(e: KeyboardEvent) {
		if (e.key === 'Escape') closeUpload();
	}
	function handleAnalyzed(analysis: Analysis) {
		cache.set(analysis.id, analysis);
		upsertAnalysis(analysis);
		invalidateLedger();
		// Jump to the month that contains the *most* of this analysis's txns.
		const counts = new Map<string, number>();
		for (const c of analysis.categories) {
			for (const t of c.transactions) {
				if (t.amount <= 0) continue;
				const iso = parseTxnIso(t.date, analysis.month);
				if (!iso) continue;
				const ym = iso.slice(0, 7);
				counts.set(ym, (counts.get(ym) ?? 0) + 1);
			}
		}
		const best = Array.from(counts.entries()).sort((a, b) => b[1] - a[1])[0];
		selectedMonth = best ? best[0] : analysis.month;
		refreshAnalyses();
	}
	function handleAllDone() {
		uploadOpen = false;
	}
	function handleViewAnalysis(analysis: Analysis) {
		cache.set(analysis.id, analysis);
		selectedMonth = analysis.month;
		if (typeof window !== 'undefined') {
			window.scrollTo({ top: 0, behavior: 'smooth' });
		}
	}

	function formatMonth(m: string | null): string {
		if (!m) return '';
		const parts = m.split('-');
		if (parts.length !== 2) return m;
		const d = new Date(parseInt(parts[0]), parseInt(parts[1]) - 1, 1);
		return d.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
	}

	onMount(async () => {
		await loadAnalyses();
		if (!selectedMonth && months.length > 0) selectedMonth = months[0];
		ready = true;
	});
	onDestroy(() => unsub());
</script>

{#if ready}
	<main class="container" in:fade={{ duration: 300 }}>
		<div class="page-header" in:fly={{ y: 10, duration: 300 }}>
			<h1>✨ Analyze</h1>
			<p class="muted">Upload a statement and see what the AI finds.</p>
		</div>

		{#if summaries.length === 0}
			<div class="empty-hero card" in:fly={{ y: 20, duration: 400 }}>
				<div class="hero-emoji">📊</div>
				<h2>No analyses yet</h2>
				<p class="muted">Tap “Upload statement” below to get started.</p>
			</div>
		{:else}
			<section class="month-nav card" in:fly={{ y: 10, duration: 300 }}>
				<button class="nav-btn" onclick={() => navMonth(-1)} disabled={!hasPrev} aria-label="Previous month">‹</button>
				<div class="month-title">{formatMonth(selectedMonth)}</div>
				<button class="nav-btn" onclick={() => navMonth(1)} disabled={!hasNext} aria-label="Next month">›</button>
			</section>

			{#if loadingMonth && monthAnalyses.length === 0}
				<div class="card"><p>Loading…</p></div>
			{:else if monthAnalyses.length === 0}
				<div class="card"><p class="muted">No analysis for this month.</p></div>
			{:else}
				{#each monthAnalyses as a (a.id)}
					<div in:fly={{ y: 20, duration: 400 }}>
						<AnalysisResults analysis={a} />
					</div>
				{/each}
			{/if}
		{/if}

		{#if summaries.length > 0}
			<section class="card past-header-card">
				<button
					class="past-toggle"
					onclick={() => (pastOpen = !pastOpen)}
					aria-expanded={pastOpen}
				>
					<span>Past Analyses <span class="muted">({summaries.length})</span></span>
					<span class="chev" class:open={pastOpen}>▾</span>
				</button>
				{#if pastOpen}
					<div transition:slide={{ duration: 180 }}>
						<PastAnalyses analyses={summaries} onView={handleViewAnalysis} onRefresh={refreshAnalyses} />
					</div>
				{/if}
			</section>
		{/if}
	</main>

	<button class="fab" onclick={openUpload} aria-label="Upload statement" title="Upload statement">
		<span class="fab-icon" aria-hidden="true">+</span>
	</button>

	{#if uploadOpen}
		<div
			class="modal-backdrop"
			in:fade={{ duration: 150 }}
			out:fade={{ duration: 120 }}
			onclick={closeUpload}
			onkeydown={onModalKey}
			role="button"
			tabindex="-1"
		>
			<div
				class="modal"
				in:fly={{ y: 30, duration: 220 }}
				out:fade={{ duration: 120 }}
				onclick={(e) => e.stopPropagation()}
				onkeydown={(e) => e.stopPropagation()}
				role="dialog"
				aria-modal="true"
				aria-label="Upload statement"
				tabindex="-1"
			>
				<div class="modal-header">
					<h2>Upload statement</h2>
					<button class="modal-close" onclick={closeUpload} disabled={$isAnalyzing} aria-label="Close">×</button>
				</div>
				<UploadAndAnalyze onAnalyzed={handleAnalyzed} onAllDone={handleAllDone} />
			</div>
		</div>
	{/if}
{:else}
	<div class="page-loading" in:fade={{ duration: 150 }}>
		<div class="spinner" aria-hidden="true"></div>
		<div class="page-loading-text">Loading…</div>
	</div>
{/if}

<style>
	.container {
		max-width: 720px;
		margin: 16px auto;
		padding: 0 16px 60px;
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
	.empty-hero {
		text-align: center;
		padding: 36px 20px;
	}
	.hero-emoji {
		font-size: 2.8rem;
		margin-bottom: 6px;
	}
	.empty-hero h2 {
		font-size: 1.2rem;
		margin-bottom: 4px;
	}

	.month-nav {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 10px 12px;
	}
	.month-title {
		font-weight: 700;
		font-size: 1.05rem;
	}
	.nav-btn {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		border: 1px solid #e2e8f0;
		background: #fff;
		font-size: 1.3rem;
		line-height: 1;
		color: #334155;
		cursor: pointer;
	}
	.nav-btn:disabled {
		opacity: 0.35;
		cursor: default;
	}

	.past-header-card {
		padding: 8px 12px;
	}
	.past-toggle {
		width: 100%;
		display: flex;
		align-items: center;
		justify-content: space-between;
		background: transparent;
		border: none;
		padding: 12px 6px;
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

	.fab {
		position: fixed;
		right: 20px;
		bottom: calc(84px + env(safe-area-inset-bottom, 0px));
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 56px;
		height: 56px;
		padding: 0;
		border: none;
		border-radius: 50%;
		background: linear-gradient(135deg, #10b981, #0d9488);
		color: white;
		cursor: pointer;
		box-shadow: 0 10px 24px rgba(5, 150, 105, 0.35);
		transition: transform 0.15s ease, box-shadow 0.15s ease;
		z-index: 80;
	}
	.fab:hover {
		transform: translateY(-2px);
		box-shadow: 0 14px 28px rgba(5, 150, 105, 0.4);
	}
	.fab:active {
		transform: translateY(0);
	}
	.fab-icon {
		font-size: 1.9rem;
		line-height: 1;
		font-weight: 300;
	}

	@media (min-width: 768px) {
		.fab {
			bottom: 28px;
			right: 28px;
		}
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
	}
	.modal {
		width: 100%;
		max-width: 560px;
		max-height: 90vh;
		overflow-y: auto;
		background: #ffffff;
		border-radius: 18px 18px 0 0;
		padding: 18px;
		box-shadow: 0 -10px 40px rgba(0, 0, 0, 0.25);
		overscroll-behavior: contain;
	}
	.modal-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 12px;
	}
	.modal-header h2 {
		font-size: 1.1rem;
		margin: 0;
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
	.modal-close:hover:not(:disabled) {
		background: rgba(0, 0, 0, 0.05);
	}
	.modal-close:disabled {
		opacity: 0.4;
		cursor: not-allowed;
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
