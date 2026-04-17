<script lang="ts">
	import { onMount } from 'svelte';
	import { fade, fly } from 'svelte/transition';
	import { listAnalyses, getAnalysis } from '$lib/api';
	import type { AnalysisSummary, Analysis } from '$lib/api';
	import UploadAndAnalyze from '$lib/components/UploadAndAnalyze.svelte';
	import AnalysisResults from '$lib/components/AnalysisResults.svelte';
	import PastAnalyses from '$lib/components/PastAnalyses.svelte';

	let analyses = $state<AnalysisSummary[]>([]);
	let currentAnalysis = $state<Analysis | null>(null);
	let ready = $state(false);

	async function refreshAnalyses() {
		try {
			analyses = await listAnalyses();
			if (currentAnalysis && !analyses.find((a) => a.id === currentAnalysis!.id)) {
				currentAnalysis = null;
			}
			// If no current selection, default to the latest by month then analyzed time
			if (!currentAnalysis && analyses.length > 0) {
				const latest = pickLatest(analyses);
				if (latest) {
					try {
						currentAnalysis = await getAnalysis(latest.id);
					} catch {
						/* ignore */
					}
				}
			}
		} catch {
			analyses = [];
		}
	}

	function pickLatest(list: AnalysisSummary[]): AnalysisSummary | null {
		if (list.length === 0) return null;
		return [...list].sort((a, b) => {
			if (a.month !== b.month) return a.month < b.month ? 1 : -1;
			return a.analyzedAt < b.analyzedAt ? 1 : -1;
		})[0];
	}

	function handleAnalyzed(analysis: Analysis) {
		currentAnalysis = analysis;
		refreshAnalyses();
	}

	function handleViewAnalysis(analysis: Analysis) {
		currentAnalysis = analysis;
		// Scroll to top so the user sees the result
		if (typeof window !== 'undefined') {
			window.scrollTo({ top: 0, behavior: 'smooth' });
		}
	}

	onMount(async () => {
		await refreshAnalyses();
		ready = true;
	});
</script>

{#if ready}
<main class="container" in:fade={{ duration: 300 }}>
	{#if currentAnalysis}
		<div in:fly={{ y: 20, duration: 400 }}>
			<AnalysisResults analysis={currentAnalysis} />
		</div>
	{:else}
		<div class="empty-hero card" in:fly={{ y: 20, duration: 400 }}>
			<div class="hero-emoji">📊</div>
			<h2>No analyses yet</h2>
			<p class="muted">Upload your first statement below to get started.</p>
		</div>
	{/if}

	<div in:fly={{ y: 20, duration: 400, delay: 80 }}>
		<PastAnalyses {analyses} onView={handleViewAnalysis} onRefresh={refreshAnalyses} />
	</div>

	<div in:fly={{ y: 20, duration: 400, delay: 160 }}>
		<UploadAndAnalyze onAnalyzed={handleAnalyzed} />
	</div>
</main>
{/if}

<style>
	.container {
		max-width: 720px;
		margin: 24px auto;
		padding: 0 20px 60px;
		display: flex;
		flex-direction: column;
		gap: 20px;
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
</style>
