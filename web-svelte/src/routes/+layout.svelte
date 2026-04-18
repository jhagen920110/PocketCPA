<script lang="ts">
	import { onMount } from 'svelte';
	import { fade, scale, fly } from 'svelte/transition';
	import { page } from '$app/state';
	import { beforeNavigate, goto } from '$app/navigation';
	import { initAuth, userEmail, displayName, setDisplayName, getDefaultName } from '$lib/auth';
	import { isAnalyzing, analysesStore, loadAnalyses, ledgerStore, loadLedger } from '$lib/appState';

	let { children } = $props();

	let showSplash = $state(true);
	let accountOpen = $state(false);
	let nameDraft = $state('');

	const SPLASH_MESSAGES = [
		'🪙 Checking under the couch cushions…',
		'🐷 Waking up the piggy bank…',
		'💸 Counting every last penny…',
		'🔎 Hunting for coins under the bed…',
		'🧾 Dusting off old receipts…',
		'☕ Skipping one latte, just in case…',
		'🛒 Negotiating with your shopping cart…',
		'📊 Teaching numbers to dance…',
		'🍜 Pretending ramen is a vibe, not a budget…',
		'🧠 Convincing your wallet to breathe…',
		'✨ Sprinkling financial fairy dust…',
		'🐿️ Stashing nuts for winter…'
	];
	let splashIdx = $state(0);
	let minSplashDone = $state(false);
	let dataReady = $state(false);

	$effect(() => {
		if (!$userEmail) return;
		if ($analysesStore.loaded && $ledgerStore.loaded) dataReady = true;
	});

	$effect(() => {
		if (minSplashDone && (dataReady || !$userEmail)) {
			showSplash = false;
		}
	});

	$effect(() => {
		if (accountOpen) {
			nameDraft = $displayName === 'there' ? '' : $displayName;
		}
	});

	onMount(() => {
		initAuth();

		// Cycle through cute messages.
		const msgTimer = setInterval(() => {
			splashIdx = (splashIdx + 1) % SPLASH_MESSAGES.length;
		}, 1800);

		// Minimum splash time so it doesn't flicker away instantly.
		const minT = setTimeout(() => {
			minSplashDone = true;
		}, 1500);

		// Hard cap so the splash never traps the user if the API is slow.
		const hardCap = setTimeout(() => {
			showSplash = false;
		}, 12000);

		// Kick off cache warmers (safe even if not signed in — they'll just fail silently).
		(async () => {
			try { await loadAnalyses(); } catch {}
			try { await loadLedger(); } catch {}
		})();

		// Prevent pinch-zoom and double-tap zoom globally.
		const blockGesture = (e: Event) => e.preventDefault();
		document.addEventListener('gesturestart', blockGesture);
		document.addEventListener('gesturechange', blockGesture);
		document.addEventListener('gestureend', blockGesture);

		let lastTouchEnd = 0;
		const blockDoubleTap = (e: TouchEvent) => {
			const now = Date.now();
			if (now - lastTouchEnd <= 300) {
				e.preventDefault();
			}
			lastTouchEnd = now;
		};
		document.addEventListener('touchend', blockDoubleTap, { passive: false });

		const blockMultiTouch = (e: TouchEvent) => {
			if (e.touches.length > 1) e.preventDefault();
		};
		document.addEventListener('touchmove', blockMultiTouch, { passive: false });

		// Close account popover when clicking outside.
		const onDocClick = (e: MouseEvent) => {
			const target = e.target as HTMLElement | null;
			if (!target) return;
			if (!target.closest('.account-wrap')) accountOpen = false;
		};
		document.addEventListener('click', onDocClick);

		return () => {
			clearTimeout(minT);
			clearTimeout(hardCap);
			clearInterval(msgTimer);
			document.removeEventListener('gesturestart', blockGesture);
			document.removeEventListener('gesturechange', blockGesture);
			document.removeEventListener('gestureend', blockGesture);
			document.removeEventListener('touchend', blockDoubleTap);
			document.removeEventListener('touchmove', blockMultiTouch);
			document.removeEventListener('click', onDocClick);
		};
	});

	beforeNavigate((nav) => {
		if ($isAnalyzing && nav.to && nav.to.url.pathname !== page.url.pathname) {
			nav.cancel();
		}
	});

	function navTo(path: string, e: MouseEvent) {
		if ($isAnalyzing) {
			e.preventDefault();
			return;
		}
		// let normal anchor behavior handle it
	}
</script>

<svelte:head>
	<title>Spending Suggestion</title>
</svelte:head>

{#if showSplash}
	<div class="splash" out:fade={{ duration: 400 }}>
		<div class="splash-inner" in:scale={{ duration: 600, start: 0.85 }}>
			<div class="splash-logo">
				<div class="coin coin-1">💵</div>
				<div class="coin coin-2">💰</div>
				<div class="coin coin-3">✨</div>
			</div>
			<h1 class="splash-title">Spending Suggestion</h1>
			<div class="splash-msg-wrap">
				{#key splashIdx}
					<p class="splash-msg" in:fly={{ y: 8, duration: 280 }} out:fade={{ duration: 160 }}>
						{SPLASH_MESSAGES[splashIdx]}
					</p>
				{/key}
			</div>
		</div>
	</div>
{/if}

{#if $userEmail}
	<header class="top-bar">
		<h1 class="top-bar-title">💰 Spending Suggestion</h1>
		<div class="account-wrap">
			<button
				class="account-btn"
				type="button"
				aria-label="Account"
				aria-expanded={accountOpen}
				onclick={(e) => { e.stopPropagation(); accountOpen = !accountOpen; }}
			>
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
					<circle cx="12" cy="8" r="4" />
					<path d="M4 21c0-4 4-6 8-6s8 2 8 6" />
				</svg>
			</button>
			{#if accountOpen}
				<div class="account-pop" in:fade={{ duration: 120 }} onclick={(e) => e.stopPropagation()} role="presentation">
					<div class="account-label">Signed in as</div>
					<div class="account-email">{$userEmail}</div>

					<div class="account-label" style="margin-top: 12px;">Name</div>
					<div class="name-row">
						<input
							class="name-input"
							type="text"
							bind:value={nameDraft}
							placeholder={getDefaultName()}
							maxlength="40"
							onkeydown={(e) => {
								if (e.key === 'Enter') {
									setDisplayName(nameDraft);
									accountOpen = false;
								}
							}}
						/>
						<button
							type="button"
							class="name-save"
							onclick={() => { setDisplayName(nameDraft); accountOpen = false; }}
						>Save</button>
					</div>
					<div class="name-hint">We'll use this anywhere we greet you.</div>
				</div>
			{/if}
		</div>
	</header>
	<div class="page-body">
		{@render children()}
	</div>
	<nav class="bottom-nav" aria-label="Primary">
		<a
			href="/"
			class:active={page.url.pathname === '/'}
			class:disabled={$isAnalyzing}
			aria-disabled={$isAnalyzing}
			onclick={(e) => navTo('/', e)}
		>
			<span class="nav-icon" aria-hidden="true">
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
					<rect x="3" y="3" width="7" height="7" rx="1.5" />
					<rect x="14" y="3" width="7" height="7" rx="1.5" />
					<rect x="3" y="14" width="7" height="7" rx="1.5" />
					<rect x="14" y="14" width="7" height="7" rx="1.5" />
				</svg>
			</span>
			<span class="nav-label">Dashboard</span>
		</a>
		<a
			href="/analyze"
			class:active={page.url.pathname === '/analyze'}
			class:disabled={$isAnalyzing}
			aria-disabled={$isAnalyzing}
			onclick={(e) => navTo('/analyze', e)}
		>
			<span class="nav-icon" aria-hidden="true">
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
					<line x1="5" y1="20" x2="5" y2="12" />
					<line x1="12" y1="20" x2="12" y2="4" />
					<line x1="19" y1="20" x2="19" y2="9" />
				</svg>
			</span>
			<span class="nav-label">Analyze</span>
		</a>
		<a
			href="/ledger"
			class:active={page.url.pathname === '/ledger'}
			class:disabled={$isAnalyzing}
			aria-disabled={$isAnalyzing}
			onclick={(e) => navTo('/ledger', e)}
		>
			<span class="nav-icon" aria-hidden="true">
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
					<rect x="3" y="5" width="18" height="16" rx="2" />
					<line x1="3" y1="10" x2="21" y2="10" />
					<line x1="8" y1="3" x2="8" y2="7" />
					<line x1="16" y1="3" x2="16" y2="7" />
				</svg>
			</span>
			<span class="nav-label">Ledger</span>
		</a>
	</nav>
	{#if $isAnalyzing}
		<div class="analyzing-banner" role="status">
			🤖 Analyzing… please stay on this page.
		</div>
	{/if}
{:else}
	<div class="overlay">
		<div class="card auth-card">
			<h2>💰 Spending Suggestion</h2>
			<p>Upload your bank statements and get AI-powered spending analysis.</p>
			<a href="/.auth/login/google" class="btn btn-primary google-btn">Sign in with Google</a>
		</div>
	</div>
{/if}

<style>
	:global(*) {
		margin: 0;
		padding: 0;
		box-sizing: border-box;
	}

	:global(html) {
		touch-action: manipulation;
		-ms-content-zooming: none;
	}

	:global(body) {
		font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto,
			'Helvetica Neue', Arial, sans-serif;
		font-feature-settings: 'cv11', 'ss01', 'ss03';
		-webkit-font-smoothing: antialiased;
		-moz-osx-font-smoothing: grayscale;
		background: #f4f7fb;
		color: #1f2937;
		min-height: 100vh;
		letter-spacing: -0.01em;
	}

	:global(h1),
	:global(h2),
	:global(h3),
	:global(.display) {
		font-family: 'Plus Jakarta Sans', 'Inter', -apple-system, BlinkMacSystemFont,
			'Segoe UI', sans-serif;
		letter-spacing: -0.02em;
	}

	/* ---------- Splash screen ---------- */
	.splash {
		position: fixed;
		inset: 0;
		z-index: 9999;
		background: linear-gradient(135deg, #059669 0%, #0d9488 50%, #0891b2 100%);
		display: flex;
		align-items: center;
		justify-content: center;
		overflow: hidden;
	}
	.splash::before,
	.splash::after {
		content: '';
		position: absolute;
		border-radius: 50%;
		background: rgba(255, 255, 255, 0.08);
		filter: blur(40px);
		animation: floatBlob 6s ease-in-out infinite;
	}
	.splash::before {
		width: 320px;
		height: 320px;
		top: -80px;
		left: -80px;
	}
	.splash::after {
		width: 260px;
		height: 260px;
		bottom: -60px;
		right: -60px;
		animation-delay: -3s;
	}
	@keyframes floatBlob {
		0%, 100% { transform: translate(0, 0) scale(1); }
		50% { transform: translate(20px, 30px) scale(1.1); }
	}
	.splash-inner {
		position: relative;
		text-align: center;
		color: #fff;
		padding: 0 24px;
		max-width: 340px;
	}
	.splash-logo {
		position: relative;
		width: 120px;
		height: 120px;
		margin: 0 auto 24px;
	}
	.coin {
		position: absolute;
		top: 50%;
		left: 50%;
		font-size: 3.2rem;
		transform: translate(-50%, -50%);
		filter: drop-shadow(0 6px 16px rgba(0, 0, 0, 0.25));
	}
	.coin-1 {
		animation: coinSpin1 2.2s ease-in-out infinite;
	}
	.coin-2 {
		animation: coinSpin2 2.2s ease-in-out infinite 0.3s;
	}
	.coin-3 {
		font-size: 2rem;
		animation: sparkle 1.4s ease-in-out infinite;
	}
	@keyframes coinSpin1 {
		0%, 100% { transform: translate(-50%, -50%) translateY(0) rotate(0deg); }
		50% { transform: translate(-50%, -50%) translateY(-14px) rotate(-12deg); }
	}
	@keyframes coinSpin2 {
		0%, 100% { transform: translate(-50%, -50%) translateY(0) rotate(0deg); }
		50% { transform: translate(-50%, -50%) translateY(-10px) rotate(14deg); }
	}
	@keyframes sparkle {
		0%, 100% {
			transform: translate(-50%, -50%) translate(36px, -32px) scale(1);
			opacity: 0.9;
		}
		50% {
			transform: translate(-50%, -50%) translate(36px, -32px) scale(1.4);
			opacity: 1;
		}
	}
	.splash-title {
		font-family: 'Plus Jakarta Sans', 'Inter', sans-serif;
		font-size: 1.9rem;
		font-weight: 800;
		letter-spacing: -0.03em;
		margin-bottom: 8px;
		color: #fff;
	}
	.splash-msg-wrap {
		position: relative;
		height: 28px;
		margin-top: 14px;
		display: flex;
		align-items: center;
		justify-content: center;
	}
	.splash-msg {
		position: absolute;
		inset: 0;
		display: flex;
		align-items: center;
		justify-content: center;
		font-size: 0.92rem;
		font-weight: 500;
		color: rgba(255, 255, 255, 0.92);
		padding: 0 12px;
		text-align: center;
	}

	.overlay {
		position: fixed;
		inset: 0;
		background: #f4f7fb;
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 100;
	}

	.auth-card {
		background: rgba(255, 255, 255, 0.9);
		border: 1px solid rgba(148, 163, 184, 0.18);
		border-radius: 18px;
		box-shadow: 0 10px 30px rgba(15, 23, 42, 0.08);
		padding: 32px;
		text-align: center;
		max-width: 400px;
	}

	.auth-card h2 {
		margin-bottom: 8px;
		font-size: 1.5rem;
	}

	.auth-card p {
		margin-bottom: 24px;
		color: #667085;
	}

	.google-btn {
		display: inline-block;
		padding: 12px 32px;
		text-decoration: none;
		font-weight: 600;
		background: #059669;
		color: #fff;
		border-radius: 8px;
		transition: background 0.15s;
	}

	.google-btn:hover {
		background: #047857;
	}

	.top-bar {
		background: linear-gradient(135deg, #6ee7b7 0%, #34d399 100%);
		color: #065f46;
		padding: 10px 16px;
		padding-top: calc(10px + env(safe-area-inset-top, 0px));
		display: flex;
		align-items: center;
		justify-content: space-between;
		position: sticky;
		top: 0;
		z-index: 50;
		box-shadow: 0 2px 10px rgba(52, 211, 153, 0.18);
	}

	.top-bar-title {
		font-size: 1.05rem;
		font-weight: 600;
		letter-spacing: -0.01em;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
		color: #065f46;
	}

	.account-wrap {
		position: relative;
	}

	.account-btn {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 36px;
		height: 36px;
		border-radius: 50%;
		border: 1px solid rgba(6, 95, 70, 0.25);
		background: rgba(255, 255, 255, 0.45);
		color: #065f46;
		cursor: pointer;
		transition: background 0.15s;
	}
	.account-btn:hover {
		background: rgba(255, 255, 255, 0.7);
	}
	.account-btn svg {
		width: 18px;
		height: 18px;
	}

	.account-pop {
		position: absolute;
		top: calc(100% + 8px);
		right: 0;
		min-width: 220px;
		background: #ffffff;
		color: #1f2937;
		border: 1px solid rgba(148, 163, 184, 0.2);
		border-radius: 12px;
		box-shadow: 0 12px 28px rgba(15, 23, 42, 0.18);
		padding: 12px 14px;
		z-index: 80;
	}
	.account-label {
		font-size: 0.7rem;
		text-transform: uppercase;
		letter-spacing: 0.08em;
		color: #94a3b8;
		margin-bottom: 4px;
	}
	.account-email {
		font-size: 0.9rem;
		font-weight: 600;
		word-break: break-all;
	}
	.name-row {
		display: flex;
		gap: 6px;
		align-items: center;
	}
	.name-input {
		flex: 1;
		min-width: 0;
		padding: 8px 10px;
		font-size: 0.9rem;
		border: 1px solid #cbd5e1;
		border-radius: 8px;
		outline: none;
		background: #fff;
		color: #1f2937;
	}
	.name-input:focus {
		border-color: #34d399;
		box-shadow: 0 0 0 3px rgba(52, 211, 153, 0.18);
	}
	.name-save {
		padding: 8px 12px;
		font-size: 0.85rem;
		font-weight: 600;
		border: none;
		border-radius: 8px;
		background: #34d399;
		color: #065f46;
		cursor: pointer;
	}
	.name-save:hover {
		background: #6ee7b7;
	}
	.name-hint {
		margin-top: 6px;
		font-size: 0.72rem;
		color: #94a3b8;
	}

	.page-body {
		/* Make room for the fixed bottom nav */
		padding-bottom: 80px;
	}

	.bottom-nav {
		position: fixed;
		left: 0;
		right: 0;
		bottom: 0;
		background: #ffffff;
		border-top: 1px solid rgba(148, 163, 184, 0.25);
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		z-index: 60;
		padding-bottom: env(safe-area-inset-bottom, 0px);
		box-shadow: 0 -4px 20px rgba(15, 23, 42, 0.06);
	}

	.bottom-nav a {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		gap: 4px;
		padding: 10px 6px 8px;
		text-decoration: none;
		color: #94a3b8;
		font-size: 0.7rem;
		font-weight: 500;
		transition: color 0.15s;
		position: relative;
	}

	.bottom-nav a .nav-icon {
		display: inline-flex;
		width: 24px;
		height: 24px;
	}
	.bottom-nav a .nav-icon svg {
		width: 100%;
		height: 100%;
	}

	.bottom-nav a:hover:not(.disabled) {
		color: #059669;
	}

	.bottom-nav a.active {
		color: #059669;
		font-weight: 600;
	}

	.bottom-nav a.active::before {
		content: '';
		position: absolute;
		top: 0;
		left: 50%;
		transform: translateX(-50%);
		width: 28px;
		height: 3px;
		border-radius: 0 0 3px 3px;
		background: #059669;
	}

	.bottom-nav a.disabled {
		opacity: 0.4;
		cursor: not-allowed;
		pointer-events: none;
	}

	.analyzing-banner {
		position: fixed;
		left: 50%;
		bottom: 78px;
		transform: translateX(-50%);
		background: #1f2937;
		color: #fff;
		padding: 8px 16px;
		border-radius: 999px;
		font-size: 0.82rem;
		font-weight: 500;
		box-shadow: 0 4px 14px rgba(15, 23, 42, 0.25);
		z-index: 70;
		padding-bottom: calc(8px + env(safe-area-inset-bottom, 0px) * 0);
	}

	:global(.btn) {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		padding: 10px 20px;
		border: none;
		border-radius: 8px;
		font-size: 0.95rem;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.15s;
	}

	:global(.btn-primary) {
		background: #059669;
		color: #fff;
	}

	:global(.btn-primary:hover:not(:disabled)) {
		background: #047857;
	}

	:global(.btn-primary:disabled) {
		opacity: 0.5;
		cursor: not-allowed;
	}

	:global(.btn-danger) {
		background: #ef4444;
		color: #fff;
	}

	:global(.btn-danger:hover) {
		background: #dc2626;
	}

	:global(.btn-sm) {
		padding: 6px 14px;
		font-size: 0.85rem;
	}

	:global(.card) {
		background: rgba(255, 255, 255, 0.9);
		border: 1px solid rgba(148, 163, 184, 0.18);
		border-radius: 18px;
		box-shadow: 0 10px 30px rgba(15, 23, 42, 0.08);
		padding: 28px;
	}

	:global(.muted) {
		color: #667085;
	}

	:global(.page-loading) {
		min-height: 60vh;
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		gap: 14px;
		color: #667085;
	}
	:global(.page-loading-text) {
		font-size: 0.95rem;
		font-weight: 500;
	}
	:global(.spinner) {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		border: 3px solid rgba(5, 150, 105, 0.18);
		border-top-color: #059669;
		animation: spin 0.8s linear infinite;
	}
	@keyframes spin {
		to { transform: rotate(360deg); }
	}
</style>
