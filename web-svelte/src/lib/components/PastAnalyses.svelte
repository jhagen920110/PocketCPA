<script lang="ts">
	import type { AnalysisSummary, Analysis } from '$lib/api';
	import { getAnalysis, deleteAnalysis, deleteAllAnalyses } from '$lib/api';

	let {
		analyses,
		onView,
		onRefresh
	}: {
		analyses: AnalysisSummary[];
		onView: (analysis: Analysis) => void;
		onRefresh: () => void | Promise<void>;
	} = $props();

	let busyId = $state<string | null>(null);
	let clearing = $state(false);

	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}

	async function view(id: string) {
		const analysis = await getAnalysis(id);
		onView(analysis);
	}

	async function remove(id: string) {
		if (!confirm('Delete this analysis?')) return;
		busyId = id;
		try {
			await deleteAnalysis(id);
			await onRefresh();
		} finally {
			busyId = null;
		}
	}

	async function clearAll() {
		if (!confirm(`Delete all ${analyses.length} past analyses?`)) return;
		clearing = true;
		try {
			await deleteAllAnalyses();
			await onRefresh();
		} finally {
			clearing = false;
		}
	}
</script>

<section class="card">
	<div class="header">
		<h2>Past Analyses</h2>
		{#if analyses.length > 0}
			<button class="btn-clear" onclick={clearAll} disabled={clearing}>
				{clearing ? 'Clearing…' : 'Clear all'}
			</button>
		{/if}
	</div>

	{#if analyses.length === 0}
		<p class="empty-state">No analyses yet.</p>
	{:else}
		<div class="analyses-list">
			{#each analyses as a}
				<div class="analysis-item">
					<div class="info">
						<div class="line1">
							<strong>{a.month}</strong>
							{#if a.bank}
								<span class="bank-pill">🏦 {a.bank}</span>
							{/if}
						</div>
						<span class="muted">
							${fmt(a.totalSpent)} &middot; {new Date(a.analyzedAt).toLocaleDateString()}
						</span>
					</div>
					<div class="actions">
						<button class="btn btn-view" onclick={() => view(a.id)}>View</button>
						<button
							class="btn btn-delete"
							onclick={() => remove(a.id)}
							disabled={busyId === a.id}
							aria-label="Delete analysis"
						>
							{busyId === a.id ? '…' : '✕'}
						</button>
					</div>
				</div>
			{/each}
		</div>
	{/if}
</section>

<style>
	.header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 12px;
	}

	.header h2 {
		margin: 0;
	}

	.btn-clear {
		background: transparent;
		color: #b42318;
		border: 1px solid #fecdca;
		padding: 6px 12px;
		border-radius: 6px;
		font-size: 0.85rem;
		cursor: pointer;
	}

	.btn-clear:hover:not(:disabled) {
		background: #fef3f2;
	}

	.btn-clear:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.empty-state {
		color: #667085;
		font-size: 0.95rem;
		text-align: center;
		padding: 12px;
	}

	.analyses-list {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.analysis-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 10px 14px;
		background: #f4f7fb;
		border-radius: 8px;
		gap: 12px;
	}

	.info {
		min-width: 0;
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.line1 {
		display: flex;
		align-items: center;
		gap: 8px;
		flex-wrap: wrap;
	}

	.bank-pill {
		display: inline-flex;
		align-items: center;
		gap: 3px;
		padding: 2px 8px;
		background: #d1fae5;
		color: #065f46;
		border-radius: 999px;
		font-size: 0.72rem;
		font-weight: 600;
	}

	.actions {
		display: flex;
		gap: 6px;
	}

	.btn {
		border: none;
		padding: 6px 12px;
		border-radius: 6px;
		font-size: 0.85rem;
		cursor: pointer;
	}

	.btn-view {
		background: #059669;
		color: #fff;
	}

	.btn-view:hover {
		background: #047857;
	}

	.btn-delete {
		background: #fff;
		color: #b42318;
		border: 1px solid #fecdca;
		min-width: 32px;
	}

	.btn-delete:hover:not(:disabled) {
		background: #fef3f2;
	}

	.btn-delete:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}
</style>
