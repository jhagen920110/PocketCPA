<script lang="ts">
	import { fade, fly } from 'svelte/transition';
	import { uploadStatement, analyzeStatements } from '$lib/api';
	import type { Analysis } from '$lib/api';
	import { isAnalyzing } from '$lib/appState';

	let { onAnalyzed, onAllDone }: {
		onAnalyzed: (analysis: Analysis) => void;
		onAllDone?: () => void;
	} = $props();

	type FileStatus = 'queued' | 'reading' | 'uploading' | 'analyzing' | 'done' | 'error';
	interface Job {
		file: File;
		status: FileStatus;
		message?: string;
		analysis?: Analysis;
	}

	let jobs = $state<Job[]>([]);
	let working = $state(false);
	let fileInput: HTMLInputElement | null = $state(null);

	function readFileAsContent(file: File): Promise<string> {
		return new Promise((resolve, reject) => {
			const reader = new FileReader();
			if (file.name.toLowerCase().endsWith('.pdf')) {
				reader.onload = () => {
					const base64 = (reader.result as string).split(',')[1];
					resolve(`[PDF:base64]${base64}`);
				};
				reader.onerror = reject;
				reader.readAsDataURL(file);
			} else {
				reader.onload = () => resolve(reader.result as string);
				reader.onerror = reject;
				reader.readAsText(file);
			}
		});
	}

	function setStatus(i: number, status: FileStatus, message?: string, analysis?: Analysis) {
		jobs = jobs.map((j, idx) =>
			idx === i ? { ...j, status, message, analysis: analysis ?? j.analysis } : j
		);
	}

	async function processAll() {
		working = true;
		isAnalyzing.set(true);
		try {
			let last: Analysis | undefined;
			for (let i = 0; i < jobs.length; i++) {
				try {
					setStatus(i, 'reading');
					const content = await readFileAsContent(jobs[i].file);

					setStatus(i, 'uploading');
					const res = await uploadStatement(jobs[i].file.name, content, null);
					if (!res.ok) {
						setStatus(i, 'error', `Upload failed (${res.status})`);
						continue;
					}
					const stmt = await res.json();

					setStatus(i, 'analyzing');
					const analysis = await analyzeStatements([stmt.id], null);
					setStatus(i, 'done', `${analysis.month}${analysis.bank ? ' · ' + analysis.bank : ''}`, analysis);
					last = analysis;
					// Show each completion immediately so the UI reacts
					onAnalyzed(analysis);
				} catch (e: any) {
					const msg = e?.needsMonth
						? 'Could not detect month — please use manual flow.'
						: (e?.message ?? 'Failed');
					setStatus(i, 'error', msg);
				}
			}
			if (last) onAnalyzed(last);
		} finally {
			working = false;
			isAnalyzing.set(false);
			// Notify parent if every job ended (success or error) so it can close the modal
			const allFinished = jobs.every((j) => j.status === 'done' || j.status === 'error');
			const anySuccess = jobs.some((j) => j.status === 'done');
			if (allFinished && anySuccess) {
				onAllDone?.();
			}
		}
	}

	function pickFiles() {
		fileInput?.click();
	}

	async function onFilesChosen(e: Event) {
		const input = e.currentTarget as HTMLInputElement;
		if (!input.files || input.files.length === 0) return;
		const ACCEPTED = ['.csv', '.txt', '.pdf'];
		const newJobs: Job[] = Array.from(input.files)
			.filter((f) => ACCEPTED.some((ext) => f.name.toLowerCase().endsWith(ext)))
			.map((file) => ({ file, status: 'queued' as FileStatus }));
		if (newJobs.length === 0) return;
		jobs = newJobs;
		input.value = '';
		await processAll();
	}

	function reset() {
		jobs = [];
	}

	function statusEmoji(s: FileStatus) {
		switch (s) {
			case 'queued': return '⏳';
			case 'reading': return '📖';
			case 'uploading': return '⬆️';
			case 'analyzing': return '🤖';
			case 'done': return '✅';
			case 'error': return '⚠️';
		}
	}

	function statusLabel(s: FileStatus) {
		switch (s) {
			case 'queued': return 'Waiting…';
			case 'reading': return 'Reading PDF…';
			case 'uploading': return 'Uploading…';
			case 'analyzing': return 'Analyzing with AI…';
			case 'done': return 'Done';
			case 'error': return 'Error';
		}
	}

	function fileIcon(name: string) {
		return name.toLowerCase().endsWith('.pdf') ? '📕' : '📄';
	}
</script>

<section class="card upload-card">
	<input
		type="file"
		accept=".csv,.txt,.pdf"
		multiple
		bind:this={fileInput}
		hidden
		onchange={onFilesChosen}
	/>

	{#if jobs.length === 0}
		<div class="upload-empty" in:fade={{ duration: 200 }}>
			<div class="upload-icon">📤</div>
			<h2>Upload statements</h2>
			<p class="muted">Pick one or more PDFs/CSVs. We'll analyze each one automatically.</p>
			<button class="btn btn-primary upload-btn" onclick={pickFiles}>
				Choose Files
			</button>
		</div>
	{:else}
		<div class="job-list">
			{#each jobs as j, i (j.file.name + i)}
				<div class="job" class:done={j.status === 'done'} class:error={j.status === 'error'} in:fly={{ y: 8, duration: 200, delay: i * 40 }}>
					<div class="job-icon">{fileIcon(j.file.name)}</div>
					<div class="job-body">
						<div class="job-name">{j.file.name}</div>
						<div class="job-status">
							<span class="job-emoji" class:spin={j.status === 'analyzing' || j.status === 'uploading' || j.status === 'reading'}>
								{statusEmoji(j.status)}
							</span>
							<span>{j.message || statusLabel(j.status)}</span>
						</div>
						{#if j.status !== 'done' && j.status !== 'error'}
							<div class="progress-track">
								<div class="progress-fill" class:indeterminate={true}></div>
							</div>
						{/if}
					</div>
				</div>
			{/each}
		</div>

		{#if !working}
			<div class="job-actions">
				<button class="btn btn-primary" onclick={pickFiles}>Add more</button>
				<button class="btn" onclick={reset}>Clear</button>
			</div>
		{/if}
	{/if}
</section>

<style>
	.upload-card {
		display: flex;
		flex-direction: column;
		gap: 14px;
	}

	.upload-empty {
		text-align: center;
		padding: 18px 0;
	}

	.upload-icon {
		font-size: 2.6rem;
		margin-bottom: 6px;
	}

	.upload-empty h2 {
		font-size: 1.1rem;
		margin-bottom: 4px;
	}

	.upload-empty .muted {
		font-size: 0.9rem;
		margin-bottom: 14px;
	}

	.upload-btn {
		padding: 12px 28px;
		font-size: 1rem;
	}

	.job-list {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.job {
		display: grid;
		grid-template-columns: auto 1fr;
		gap: 10px;
		align-items: flex-start;
		padding: 12px 14px;
		background: linear-gradient(135deg, #f5f3ff 0%, #eff6ff 100%);
		border: 1px solid #e0e7ff;
		border-radius: 12px;
		transition: background 0.3s, border-color 0.3s;
	}

	.job.done {
		background: linear-gradient(135deg, #ecfdf5 0%, #f0fdf4 100%);
		border-color: #a7f3d0;
	}

	.job.error {
		background: #fef2f2;
		border-color: #fecaca;
	}

	.job-icon {
		font-size: 1.4rem;
	}

	.job-name {
		font-weight: 600;
		font-size: 0.92rem;
		word-break: break-word;
	}

	.job-status {
		display: flex;
		align-items: center;
		gap: 6px;
		font-size: 0.82rem;
		color: #4b5563;
		margin-top: 2px;
	}

	.job-emoji {
		display: inline-block;
	}

	.job-emoji.spin {
		animation: bounce 1s ease-in-out infinite;
	}

	@keyframes bounce {
		0%, 100% { transform: translateY(0); }
		50% { transform: translateY(-3px); }
	}

	.progress-track {
		margin-top: 8px;
		height: 4px;
		background: rgba(148, 163, 184, 0.25);
		border-radius: 999px;
		overflow: hidden;
	}

	.progress-fill {
		height: 100%;
		width: 40%;
		background: linear-gradient(90deg, #6366f1, #8b5cf6, #6366f1);
		background-size: 200% 100%;
	}

	.progress-fill.indeterminate {
		animation: slide 1.4s ease-in-out infinite;
	}

	@keyframes slide {
		0%   { transform: translateX(-100%); }
		100% { transform: translateX(350%); }
	}

	.job-actions {
		display: flex;
		gap: 10px;
	}

	.job-actions .btn {
		flex: 1;
	}
</style>
