import { writable, get, derived } from 'svelte/store';

export const userEmail = writable<string | null>(null);

// Per-email display-name overrides, persisted in localStorage.
const NAMES_KEY = 'spendingSuggestion.displayNames';

function loadNames(): Record<string, string> {
	if (typeof localStorage === 'undefined') return {};
	try {
		return JSON.parse(localStorage.getItem(NAMES_KEY) || '{}');
	} catch {
		return {};
	}
}

function saveNames(map: Record<string, string>) {
	if (typeof localStorage === 'undefined') return;
	try {
		localStorage.setItem(NAMES_KEY, JSON.stringify(map));
	} catch {
		/* ignore */
	}
}

export const displayNames = writable<Record<string, string>>(loadNames());

function defaultNameFor(email: string): string {
	if (!email) return 'there';
	const local = email.split('@')[0] ?? '';
	const bare = local.split(/[._-]/)[0] ?? '';
	if (!bare) return 'there';
	return bare.charAt(0).toUpperCase() + bare.slice(1).toLowerCase();
}

export const displayName = derived(
	[userEmail, displayNames],
	([$email, $names]) => {
		if (!$email) return 'there';
		const override = $names[$email]?.trim();
		if (override) return override;
		return defaultNameFor($email);
	}
);

export function setDisplayName(name: string) {
	const email = get(userEmail);
	if (!email) return;
	const clean = name.trim();
	displayNames.update((m) => {
		const next = { ...m };
		if (!clean) delete next[email];
		else next[email] = clean;
		saveNames(next);
		return next;
	});
}

export function getDefaultName(): string {
	const email = get(userEmail) ?? '';
	return defaultNameFor(email);
}

export async function initAuth() {
	// Local dev shortcut - skip /.auth/me (not available outside Azure SWA)
	if (
		typeof window !== 'undefined' &&
		(window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1')
	) {
		userEmail.set('local-dev@test.com');
		return;
	}

	try {
		const res = await fetch('/.auth/me');
		if (!res.ok) return;
		const data = await res.json();
		const principal = data.clientPrincipal;
		if (principal?.userDetails) {
			userEmail.set(principal.userDetails);
		}
	} catch {
		// Auth endpoint not available
	}
}
