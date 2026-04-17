<script lang="ts">
	import { fly } from 'svelte/transition';
	import { uploadStatement, deleteStatement } from '$lib/api';
	import type { StatementSummary } from '$lib/api';

	let { statements, onRefresh }: { statements: StatementSummary[]; onRefresh: () => void } =
		$props();

	let files = $state<File[]>([]);
	let uploading = $state(false);

	const ACCEPTED = ['.csv', '.txt', '.pdf'];

	function handleDrop(e: DragEvent) {
		e.preventDefault();
		if (e.dataTransfer?.files) {
			addFiles(e.dataTransfer.files);
		}
	}

	function addFiles(fileList: FileList) {
		const newFiles = Array.from(fileList).filter((f) =>
			ACCEPTED.some((ext) => f.name.toLowerCase().endsWith(ext))
		);
		files = [...files, ...newFiles];
	}

	function removeFile(index: number) {
		files = files.filter((_, i) => i !== index);
	}

	function readFileAsText(file: File): Promise<string> {
		return new Promise((resolve, reject) => {
			const reader = new FileReader();
			if (file.name.toLowerCase().endsWith('.pdf')) {
				// Send PDFs as base64 so the backend can process them
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

	async function upload() {
		uploading = true;
		for (const file of files) {
			const content = await readFileAsText(file);
			await uploadStatement(file.name, content, null);
		}
		files = [];
		uploading = false;
		onRefresh();
	}

	async function handleDelete(id: string) {
		if (!confirm('Delete this statement?')) return;
		await deleteStatement(id);
		onRefresh();
	}

	function fileIcon(name: string) {
		if (name.toLowerCase().endsWith('.pdf')) return '📕';
		return '📄';
	}
</script>

<section class="card">
	<h2>Bank Statements</h2>
	<p class="card-desc">Upload CSV or PDF exports from your bank or credit card.</p>

	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div
		class="upload-area"
		ondragover={(e) => e.preventDefault()}
		ondrop={handleDrop}
	>
		<input
			type="file"
			accept=".csv,.txt,.pdf"
			multiple
			hidden
			id="file-input"
			onchange={(e) => {
				const input = e.currentTarget as HTMLInputElement;
				if (input.files) addFiles(input.files);
			}}
		/>
		{#if files.length === 0}
			<div class="upload-placeholder">
				<span class="upload-icon">📄</span>
				<p>
					Drag & drop files here, or
					<button class="link-btn" onclick={() => document.getElementById('file-input')?.click()}>
						browse
					</button>
				</p>
				<p class="upload-hint">Supports CSV, TXT, and PDF</p>
			</div>
		{:else}
			<div class="file-list">
				{#each files as file, i}
					<div class="file-item" in:fly={{ x: -20, duration: 200, delay: i * 50 }}>
						<span>{fileIcon(file.name)} {file.name} ({(file.size / 1024).toFixed(1)} KB)</span>
						<button class="btn-icon" onclick={() => removeFile(i)}>&times;</button>
					</div>
				{/each}
			</div>
		{/if}
	</div>

	<div class="upload-controls">
		<button class="btn btn-primary" disabled={files.length === 0 || uploading} onclick={upload}>
			{uploading ? 'Uploading...' : `Upload ${files.length ? `(${files.length})` : ''}`}
		</button>
	</div>

	<!-- Existing uploaded statements -->
	{#if statements.length > 0}
		<hr class="divider" />
		<h3 class="sub-heading">Uploaded ({statements.length})</h3>
		<div class="statements-list">
			{#each statements as s, i}
				<div class="statement-item" in:fly={{ x: -20, duration: 250, delay: i * 60 }}>
					<div class="statement-info">
						<strong>{fileIcon(s.fileName)} {s.fileName}</strong>
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
	.card-desc {
		color: #667085;
		margin-bottom: 16px;
		font-size: 0.95rem;
	}

	.upload-area {
		border: 2px dashed rgba(148, 163, 184, 0.3);
		border-radius: 12px;
		padding: 24px;
		text-align: center;
		transition: border-color 0.2s, background 0.2s;
		margin-bottom: 16px;
	}

	.upload-area:hover {
		border-color: rgba(79, 70, 229, 0.3);
	}

	.upload-placeholder {
		color: #667085;
	}

	.upload-icon {
		font-size: 2rem;
		display: block;
		margin-bottom: 8px;
	}

	.upload-hint {
		font-size: 0.8rem;
		margin-top: 6px;
		opacity: 0.6;
	}

	.link-btn {
		background: none;
		border: none;
		color: #4f46e5;
		text-decoration: underline;
		cursor: pointer;
		font-size: inherit;
	}

	.file-list {
		display: flex;
		flex-direction: column;
		gap: 6px;
	}

	.file-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 6px 10px;
		background: #f4f7fb;
		border-radius: 8px;
		font-size: 0.9rem;
	}

	.btn-icon {
		background: none;
		border: none;
		font-size: 1.2rem;
		cursor: pointer;
		color: #667085;
		padding: 2px 6px;
	}

	.btn-icon:hover {
		color: #ef4444;
	}

	.upload-controls {
		display: flex;
		justify-content: flex-end;
	}

	.divider {
		border: none;
		border-top: 1px solid rgba(148, 163, 184, 0.18);
		margin: 20px 0 12px;
	}

	.sub-heading {
		font-size: 0.95rem;
		color: #667085;
		margin-bottom: 10px;
		font-weight: 600;
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
