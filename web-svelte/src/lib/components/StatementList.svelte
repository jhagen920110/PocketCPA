<script lang="ts">
	import { fly } from 'svelte/transition';
	import type { StatementSummary } from '$lib/api';
	import { deleteStatement } from '$lib/api';

	let { statements, onRefresh }: { statements: StatementSummary[]; onRefresh: () => void } =
		$props();

	async function handleDelete(id: string) {
		if (!confirm('Delete this statement?')) return;
		await deleteStatement(id);
		onRefresh();
	}
</script>

<section class="card">
	<h2>Uploaded Statements</h2>

	{#if statements.length === 0}
		<p class="empty-state">No statements uploaded yet.</p>
	{:else}
		<div class="statements-list">
			{#each statements as s, i}
				<div class="statement-item" in:fly={{ x: -20, duration: 250, delay: i * 60 }}>
					<div class="statement-info">
						<strong>{s.fileName}</strong>
						<span class="muted"
							>{s.month || 'No month'} &middot; {new Date(s.uploadedAt).toLocaleDateString()}</span
						>
					</div>
					<button class="btn btn-danger btn-sm" onclick={() => handleDelete(s.id)}>Delete</button>
				</div>
			{/each}
		</div>
	{/if}
</section>

<style>
	.empty-state {
		color: #667085;
		font-size: 0.95rem;
		text-align: center;
		padding: 12px;
	}

	.statements-list {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.statement-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 10px 14px;
		background: #f4f7fb;
		border-radius: 8px;
	}

	.statement-info {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.statement-info .muted {
		font-size: 0.85rem;
	}
</style>
