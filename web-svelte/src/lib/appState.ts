import { writable, get } from 'svelte/store';
import {
	listAnalyses,
	getAnalysis,
	getLedger,
	type AnalysisSummary,
	type Analysis,
	type LedgerEntry
} from './api';

// Global flag set while an analysis request is in flight.
// Used by the layout to prevent navigation away from the Analyze page mid-run.
export const isAnalyzing = writable(false);

// ---------- Shared analyses cache (so tab switches are instant) ----------
interface AnalysesState {
	summaries: AnalysisSummary[];
	analyses: Analysis[];
	loaded: boolean;
	loading: boolean;
	error: string | null;
}
export const analysesStore = writable<AnalysesState>({
	summaries: [],
	analyses: [],
	loaded: false,
	loading: false,
	error: null
});

let analysesInFlight: Promise<void> | null = null;

export function loadAnalyses(force = false): Promise<void> {
	const cur = get(analysesStore);
	if (!force && (cur.loaded || cur.loading) && analysesInFlight) return analysesInFlight;
	if (!force && cur.loaded && !analysesInFlight) return Promise.resolve();

	analysesStore.update((s) => ({ ...s, loading: true, error: null }));
	analysesInFlight = (async () => {
		try {
			const summaries = await listAnalyses();
			const loaded = await Promise.all(
				summaries.map((s) => getAnalysis(s.id).catch(() => null as unknown as Analysis))
			);
			const analyses = loaded.filter((a): a is Analysis => !!a);
			analysesStore.set({
				summaries,
				analyses,
				loaded: true,
				loading: false,
				error: null
			});
		} catch (e: any) {
			analysesStore.update((s) => ({
				...s,
				loading: false,
				error: e?.message ?? 'Failed to load'
			}));
		} finally {
			analysesInFlight = null;
		}
	})();
	return analysesInFlight;
}

// Merge a newly-created analysis into the cache without re-fetching everything.
export function upsertAnalysis(a: Analysis) {
	analysesStore.update((s) => {
		const analyses = [a, ...s.analyses.filter((x) => x.id !== a.id)];
		const summary: AnalysisSummary = {
			id: a.id,
			month: a.month,
			bank: a.bank,
			analyzedAt: a.analyzedAt,
			totalSpent: a.totalSpent
		} as AnalysisSummary;
		const summaries = [summary, ...s.summaries.filter((x) => x.id !== a.id)];
		return { ...s, analyses, summaries, loaded: true };
	});
}

export function removeAnalysis(id: string) {
	analysesStore.update((s) => ({
		...s,
		analyses: s.analyses.filter((a) => a.id !== id),
		summaries: s.summaries.filter((a) => a.id !== id)
	}));
}

export function clearAnalyses() {
	analysesStore.set({ summaries: [], analyses: [], loaded: true, loading: false, error: null });
}

// ---------- Shared ledger cache ----------
interface LedgerState {
	entries: LedgerEntry[];
	loaded: boolean;
	loading: boolean;
	error: string | null;
}
export const ledgerStore = writable<LedgerState>({
	entries: [],
	loaded: false,
	loading: false,
	error: null
});

let ledgerInFlight: Promise<void> | null = null;

export function loadLedger(force = false): Promise<void> {
	const cur = get(ledgerStore);
	if (!force && (cur.loaded || cur.loading) && ledgerInFlight) return ledgerInFlight;
	if (!force && cur.loaded && !ledgerInFlight) return Promise.resolve();

	ledgerStore.update((s) => ({ ...s, loading: true, error: null }));
	ledgerInFlight = (async () => {
		try {
			const entries = await getLedger();
			ledgerStore.set({ entries, loaded: true, loading: false, error: null });
		} catch (e: any) {
			ledgerStore.update((s) => ({
				...s,
				loading: false,
				error: e?.message ?? 'Failed to load ledger'
			}));
		} finally {
			ledgerInFlight = null;
		}
	})();
	return ledgerInFlight;
}

export function invalidateLedger() {
	ledgerStore.set({ entries: [], loaded: false, loading: false, error: null });
}

// ---------- Shared selected month (YYYY-MM) ----------
// In-memory only — on fresh load, each page defaults to its own latest month.
// While the app is open, changes sync across Dashboard / Analyze / Ledger.
export const selectedMonth = writable<string | null>(null);

// One-time cleanup: remove any legacy persisted value so we always default to latest.
if (typeof localStorage !== 'undefined') {
	try { localStorage.removeItem('ss.selectedMonth'); } catch {}
}

export function setSelectedMonth(m: string | null) {
	selectedMonth.set(m);
}
