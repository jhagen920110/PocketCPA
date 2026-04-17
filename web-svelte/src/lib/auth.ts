import { writable } from 'svelte/store';

export const userEmail = writable<string | null>(null);

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
