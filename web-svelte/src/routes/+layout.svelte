<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { beforeNavigate, goto } from '$app/navigation';
	import { initAuth, userEmail } from '$lib/auth';
	import { isAnalyzing } from '$lib/appState';

	let { children } = $props();

	onMount(() => {
		initAuth();
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

{#if $userEmail}
	<header class="top-bar">
		<h1 class="top-bar-title">💰 Spending Suggestion</h1>
		<div class="top-bar-right">
			<span class="user-email">{$userEmail}</span>
			<a href="/.auth/logout" class="sign-out-link">Sign out</a>
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
			<span class="nav-icon">📊</span>
			<span class="nav-label">Analyze</span>
		</a>
		<a
			href="/ledger"
			class:active={page.url.pathname === '/ledger'}
			class:disabled={$isAnalyzing}
			aria-disabled={$isAnalyzing}
			onclick={(e) => navTo('/ledger', e)}
		>
			<span class="nav-icon">📒</span>
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

	:global(body) {
		font-family: 'Segoe UI', 'Apple SD Gothic Neo', sans-serif;
		background: #f4f7fb;
		color: #1f2937;
		min-height: 100vh;
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
		background: #4f46e5;
		color: #fff;
		border-radius: 8px;
		transition: background 0.15s;
	}

	.google-btn:hover {
		background: #4338ca;
	}

	.top-bar {
		background: #4f46e5;
		color: #fff;
		padding: 16px 20px;
		display: flex;
		align-items: center;
		justify-content: space-between;
		position: sticky;
		top: 0;
		z-index: 50;
	}

	.top-bar-title {
		font-size: 1.4rem;
		font-weight: 700;
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
		grid-template-columns: repeat(2, 1fr);
		z-index: 60;
		padding-bottom: env(safe-area-inset-bottom, 0px);
		box-shadow: 0 -4px 20px rgba(15, 23, 42, 0.06);
	}

	.bottom-nav a {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		gap: 2px;
		padding: 10px 6px;
		text-decoration: none;
		color: #6b7280;
		font-size: 0.72rem;
		font-weight: 600;
		transition: color 0.15s, background 0.15s;
	}

	.bottom-nav a .nav-icon {
		font-size: 1.4rem;
		line-height: 1;
	}

	.bottom-nav a:hover:not(.disabled) {
		color: #4f46e5;
		background: #f5f3ff;
	}

	.bottom-nav a.active {
		color: #4f46e5;
	}

	.bottom-nav a.active .nav-icon {
		transform: scale(1.05);
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

	.top-bar-right {
		display: flex;
		align-items: center;
		gap: 12px;
		font-size: 0.9rem;
	}

	.user-email {
		opacity: 0.85;
	}

	.sign-out-link {
		color: #fff;
		opacity: 0.75;
		text-decoration: underline;
	}

	.sign-out-link:hover {
		opacity: 1;
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
		background: #4f46e5;
		color: #fff;
	}

	:global(.btn-primary:hover:not(:disabled)) {
		background: #4338ca;
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
</style>
