<script lang="ts">
	import type { StatementSummary, Analysis } from '$lib/api';
	import { analyzeStatements } from '$lib/api';
	import { isAnalyzing } from '$lib/appState';

	let {
		statements,
		onAnalyzed
	}: { statements: StatementSummary[]; onAnalyzed: (analysis: Analysis) => void } = $props();

	let selectedIds = $state<Set<string>>(new Set());
	let analyzing = $state(false);
	let error = $state<string | null>(null);
	let needsMonth = $state(false);
	let monthInput = $state(getCurrentMonth());

	function getCurrentMonth() {
		const now = new Date();
		return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
	}

	// Auto-select all on mount / statement changes
	$effect(() => {
		selectedIds = new Set(statements.map((s) => s.id));
	});

	function toggleId(id: string) {
		const next = new Set(selectedIds);
		if (next.has(id)) next.delete(id);
		else next.add(id);
		selectedIds = next;
	}

	async function runAnalyze(month: string | null) {
		if (selectedIds.size === 0) return;
		analyzing = true;
		isAnalyzing.set(true);
		error = null;

		try {
			const result = await analyzeStatements([...selectedIds], month);
			needsMonth = false;
			onAnalyzed(result);
		} catch (e: any) {
			if (e?.needsMonth) {
				needsMonth = true;
			} else {
				error = e.message;
			}
		} finally {
			analyzing = false;
			isAnalyzing.set(false);
		}
	}

	const analyze = () => runAnalyze(null);
	const analyzeWithMonth = () => runAnalyze(monthInput);
</script>

<section class="card">
	<h2>Analyze Spending</h2>
	<p class="card-desc">Select statements and let AI categorize your spending.</p>

	{#if statements.length === 0}
		<p class="muted">Upload statements first.</p>
	{:else}
		<div class="analyze-checkboxes">
			{#each statements as s}
				<label class="checkbox-label">
					<input
						type="checkbox"
						checked={selectedIds.has(s.id)}
						onchange={() => toggleId(s.id)}
					/>
					{s.fileName}
				</label>
			{/each}
		</div>

		{#if needsMonth}
			<div class="month-prompt">
				<p>AI couldn't determine the month from the statement. Please specify:</p>
				<div class="month-row">
					<input type="month" bind:value={monthInput} />
					<button class="btn btn-primary" disabled={analyzing} onclick={analyzeWithMonth}>
						{analyzing ? 'Analyzing...' : 'Continue'}
					</button>
				</div>
			</div>
		{:else}
			<button
				class="btn btn-primary"
				disabled={selectedIds.size === 0 || analyzing}
				onclick={analyze}
			>
				{analyzing ? 'Analyzing...' : 'Analyze with AI'}
			</button>
		{/if}
	{/if}

	{#if analyzing}
		<div class="loading">
			<div class="spinner"></div>
			<p>Analyzing your spending patterns...</p>
		</div>
	{/if}

	{#if error}
		<p class="error">Error: {error}</p>
	{/if}
</section>

<style>
	.card-desc {
		color: #667085;
		margin-bottom: 16px;
		font-size: 0.95rem;
	}

	.analyze-checkboxes {
		display: flex;
		flex-direction: column;
		gap: 6px;
		margin-bottom: 12px;
	}

	.checkbox-label {
		display: flex;
		align-items: center;
		gap: 8px;
		font-size: 0.95rem;
		cursor: pointer;
	}

	.loading {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 12px;
		padding: 24px;
		color: #667085;
	}

	.spinner {
		width: 36px;
		height: 36px;
		border: 3px solid rgba(148, 163, 184, 0.18);
		border-top-color: #4f46e5;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}

	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}

	.error {
		color: #ef4444;
		margin-top: 12px;
	}

	.month-prompt {
		padding: 14px;
		background: #fef9c3;
		border: 1px solid #fde68a;
		border-radius: 10px;
		margin-bottom: 12px;
	}

	.month-prompt p {
		margin-bottom: 10px;
		color: #713f12;
		font-size: 0.9rem;
	}

	.month-row {
		display: flex;
		gap: 10px;
		align-items: center;
	}

	.month-row input[type='month'] {
		padding: 8px 12px;
		border: 1px solid rgba(148, 163, 184, 0.3);
		border-radius: 8px;
		font-size: 0.95rem;
		flex: 1;
	}
</style>
