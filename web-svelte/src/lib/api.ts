import { get } from 'svelte/store';
import { userEmail } from './auth';
import { API_BASE } from './config';

export async function apiFetch(path: string, options: RequestInit = {}): Promise<Response> {
	const email = get(userEmail);
	const headers = new Headers(options.headers);
	if (email) {
		headers.set('X-User-Email', email);
	}
	return fetch(`${API_BASE}${path}`, { ...options, headers });
}

export interface StatementSummary {
	id: string;
	fileName: string;
	month: string | null;
	uploadedAt: string;
}

export interface AnalysisSummary {
	id: string;
	month: string;
	bank?: string;
	analyzedAt: string;
	totalSpent: number;
}

export interface Transaction {
	date: string;
	merchant?: string;
	description: string;
	amount: number;
}

export interface SpendingCategory {
	name: string;
	total: number;
	percentage: number;
	transactions: Transaction[];
}

export interface Analysis {
	id: string;
	month: string;
	bank?: string;
	analyzedAt: string;
	totalSpent: number;
	categories: SpendingCategory[];
	insights: string[];
	suggestions: string[];
	funStats?: FunStat[];
}

export interface FunStat {
	emoji: string;
	label: string;
	value: string;
}

// Statements
export async function uploadStatement(fileName: string, content: string, month: string | null) {
	return apiFetch('/statements', {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ fileName, content, month })
	});
}

export async function listStatements(): Promise<StatementSummary[]> {
	const res = await apiFetch('/statements');
	return res.json();
}

export async function deleteStatement(id: string) {
	return apiFetch(`/statements/${id}`, { method: 'DELETE' });
}

// Analysis
export async function analyzeStatements(statementIds: string[], month: string | null): Promise<Analysis> {
	const res = await apiFetch('/analyze', {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ statementIds, month })
	});
	if (!res.ok) {
		const err = await res.json().catch(() => ({}));
		const e: any = new Error(err.error || 'Analysis failed');
		if (err.needsMonth) e.needsMonth = true;
		throw e;
	}
	return res.json();
}

export async function listAnalyses(): Promise<AnalysisSummary[]> {
	const res = await apiFetch('/analyses');
	return res.json();
}

export async function getAnalysis(id: string): Promise<Analysis> {
	const res = await apiFetch(`/analyses/${id}`);
	return res.json();
}

export async function deleteAnalysis(id: string) {
	return apiFetch(`/analyses/${id}`, { method: 'DELETE' });
}

export async function deleteAllAnalyses() {
	return apiFetch(`/analyses`, { method: 'DELETE' });
}

// Ledger
export interface LedgerEntry {
	id: string;
	analysisId: string;
	month: string;
	bank?: string;
	date: string;
	category: string;
	merchant: string;
	description: string;
	amount: number;
}

export async function getLedger(): Promise<LedgerEntry[]> {
	const res = await apiFetch('/ledger');
	if (!res.ok) return [];
	return res.json();
}
