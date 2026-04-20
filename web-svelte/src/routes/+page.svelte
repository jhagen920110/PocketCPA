<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { fade, fly } from 'svelte/transition';
	import { goto } from '$app/navigation';
	import type { AnalysisSummary, Analysis } from '$lib/api';
	import CategoryBubbles from '$lib/components/CategoryBubbles.svelte';
	import { categoryColor, shortCategory } from '$lib/colors';
	import { userEmail, displayName } from '$lib/auth';
	import { analysesStore, loadAnalyses, selectedMonth as selectedMonthStore } from '$lib/appState';
	import { get } from 'svelte/store';

	let summaries = $state<AnalysisSummary[]>([]);
	let loading = $state(false);

	// Once loaded, every analysis sits here keyed by id.
	let loadedAnalyses = $state<Analysis[]>([]);

	// Subscribe to shared cache so dashboard renders instantly on tab switches.
	const unsub = analysesStore.subscribe((s) => {
		summaries = s.summaries;
		loadedAnalyses = s.analyses;
		loading = s.loading;
	});

	const ready = $derived($analysesStore.loaded);

	// Build a global flat list of transactions (with their TRUE calendar ISO date)
	// the very first time we see each analysis. Then the dashboard groups by the
	// transaction's actual month, not the statement's closing month — so a Jan
	// purchase shown on a Feb statement lands in January.
	interface TxLite {
		iso: string; // YYYY-MM-DD
		ym: string; // YYYY-MM
		amount: number;
		merchant: string;
		category: string; // shortCategory already applied
		bank?: string;
	}
	const allTxs = $derived.by(() => {
		const out: TxLite[] = [];
		for (const a of loadedAnalyses) {
			for (const c of a.categories) {
				const cat = shortCategory(c.name);
				for (const t of c.transactions) {
					if (t.amount <= 0) continue;
					const iso = parseTxnIso(t.date, a.month);
					if (!iso) continue;
					out.push({
						iso,
						ym: iso.slice(0, 7),
						amount: t.amount,
						merchant: (t.merchant || t.description || '—').trim(),
						category: cat,
						bank: a.bank
					});
				}
			}
		}
		return out;
	});

	// Months with actual spending, newest first.
	const months = $derived.by(() => {
		const set = new Set(allTxs.map((t) => t.ym));
		return Array.from(set).sort().reverse();
	});

	let selectedMonth = $state<string | null>(get(selectedMonthStore));

	// Sync local state <-> shared store so switching pages preserves the month.
	const unsubMonth = selectedMonthStore.subscribe((v) => {
		if (v !== selectedMonth) selectedMonth = v;
	});
	$effect(() => {
		if (selectedMonth !== get(selectedMonthStore)) selectedMonthStore.set(selectedMonth);
	});

	// Convenience: all insights/suggestions/fun-stats from analyses that
	// *contributed any transaction* to the selected month. This keeps AI
	// narrative close to the actual data being shown.
	const contributingAnalyses = $derived.by(() => {
		if (!selectedMonth) return [] as Analysis[];
		const ids = new Set<string>();
		for (const a of loadedAnalyses) {
			// If any of a's txns land in the selected month, include it.
			let hit = false;
			for (const c of a.categories) {
				if (hit) break;
				for (const t of c.transactions) {
					if (t.amount <= 0) continue;
					const iso = parseTxnIso(t.date, a.month);
					if (iso && iso.startsWith(selectedMonth)) {
						hit = true;
						break;
					}
				}
			}
			if (hit) ids.add(a.id);
		}
		return loadedAnalyses.filter((a) => ids.has(a.id));
	});

	interface AggCategory { name: string; total: number; percentage: number; }
	interface Agg {
		totalSpent: number;
		categories: AggCategory[];
		dailyTotals: { iso: string; day: number; amount: number; count: number }[];
		txs: TxLite[];
		insights: string[];
		suggestions: string[];
	}

	function aggregate(txs: TxLite[], analyses: Analysis[], month: string | null): Agg {
		const byCat = new Map<string, number>();
		const byDay = new Map<string, { amount: number; count: number }>();
		let total = 0;
		for (const t of txs) {
			byCat.set(t.category, (byCat.get(t.category) ?? 0) + t.amount);
			const cell = byDay.get(t.iso) ?? { amount: 0, count: 0 };
			cell.amount += t.amount;
			cell.count += 1;
			byDay.set(t.iso, cell);
			total += t.amount;
		}

		const categories: AggCategory[] = Array.from(byCat.entries())
			.map(([name, amt]) => ({
				name,
				total: amt,
				percentage: total > 0 ? (amt / total) * 100 : 0
			}))
			.sort((a, b) => b.total - a.total);

		const dailyTotals: { iso: string; day: number; amount: number; count: number }[] = [];
		if (month) {
			const [y, m] = month.split('-').map((n) => parseInt(n, 10));
			if (!isNaN(y) && !isNaN(m)) {
				const last = new Date(y, m, 0).getDate();
				for (let d = 1; d <= last; d++) {
					const iso = `${y}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
					const cell = byDay.get(iso) ?? { amount: 0, count: 0 };
					dailyTotals.push({ iso, day: d, amount: cell.amount, count: cell.count });
				}
			}
		}

		const insights: string[] = [];
		const suggestions: string[] = [];
		for (const a of analyses) {
			if (a.insights) insights.push(...a.insights);
			if (a.suggestions) suggestions.push(...a.suggestions);
		}
		const dedupe = (arr: string[]) => Array.from(new Set(arr));

		return {
			totalSpent: total,
			categories,
			dailyTotals,
			txs,
			insights: dedupe(insights),
			suggestions: dedupe(suggestions)
		};
	}

	// Parse a transaction date. Handles M/D with a "year-cross" fix: if the
	// statement closes in Jan 2026 but a transaction says 12/15, that's 2025-12-15,
	// NOT 2026-12-15. Heuristic: if the parsed month is strictly greater than the
	// fallback month, subtract 1 from the year.
	function parseTxnIso(dateStr: string, fallbackMonth: string): string | null {
		const s = (dateStr ?? '').trim();
		if (!s) return null;
		const pad = (n: string | number) => String(n).padStart(2, '0');
		let m = s.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/);
		if (m) return `${m[1]}-${pad(m[2])}-${pad(m[3])}`;
		m = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{2,4})$/);
		if (m) {
			let y = m[3];
			if (y.length === 2) y = (parseInt(y, 10) >= 70 ? '19' : '20') + y;
			return `${y}-${pad(m[1])}-${pad(m[2])}`;
		}
		m = s.match(/^(\d{1,2})\/(\d{1,2})$/);
		if (m && fallbackMonth) {
			const [fy, fm] = fallbackMonth.split('-');
			let year = parseInt(fy, 10);
			const parsedMonth = parseInt(m[1], 10);
			if (parsedMonth > parseInt(fm, 10)) year -= 1;
			return `${year}-${pad(m[1])}-${pad(m[2])}`;
		}
		const mon = s.match(/^([A-Za-z]{3,})\s+(\d{1,2})$/);
		if (mon && fallbackMonth) {
			const monMap: Record<string, number> = {
				jan: 1, feb: 2, mar: 3, apr: 4, may: 5, jun: 6,
				jul: 7, aug: 8, sep: 9, oct: 10, nov: 11, dec: 12
			};
			const pm = monMap[mon[1].slice(0, 3).toLowerCase()];
			if (pm) {
				const [fy, fm] = fallbackMonth.split('-');
				let year = parseInt(fy, 10);
				if (pm > parseInt(fm, 10)) year -= 1;
				return `${year}-${pad(pm)}-${pad(mon[2])}`;
			}
		}
		return null;
	}

	const currentTxs = $derived(
		selectedMonth ? allTxs.filter((t) => t.ym === selectedMonth) : []
	);
	const prevMonthKey = $derived.by(() => {
		const idx = selectedMonth ? months.indexOf(selectedMonth) : -1;
		return idx >= 0 && idx + 1 < months.length ? months[idx + 1] : null;
	});
	const prevTxs = $derived(
		prevMonthKey ? allTxs.filter((t) => t.ym === prevMonthKey) : []
	);

	const current = $derived(aggregate(currentTxs, contributingAnalyses, selectedMonth));
	const previous = $derived(aggregate(prevTxs, [], prevMonthKey));

	const deltaPct = $derived.by(() => {
		if (previous.totalSpent <= 0) return null;
		return ((current.totalSpent - previous.totalSpent) / previous.totalSpent) * 100;
	});

	const activeDays = $derived(current.dailyTotals.filter((d) => d.amount > 0).length);
	const maxDay = $derived(Math.max(0, ...current.dailyTotals.map((d) => d.amount)));
	const avgPerActiveDay = $derived(activeDays > 0 ? current.totalSpent / activeDays : 0);

	function navMonth(dir: -1 | 1) {
		if (!selectedMonth || months.length === 0) return;
		const idx = months.indexOf(selectedMonth);
		const nextIdx = dir === -1 ? idx + 1 : idx - 1;
		if (nextIdx < 0 || nextIdx >= months.length) return;
		selectedMonth = months[nextIdx];
		hoveredDay = null;
		activeWeekday = null;
	}	const hasPrev = $derived.by(() =>
		selectedMonth ? months.indexOf(selectedMonth) < months.length - 1 : false
	);
	const hasNext = $derived.by(() => (selectedMonth ? months.indexOf(selectedMonth) > 0 : false));

	function formatMonth(m: string | null): string {
		if (!m) return '';
		const parts = m.split('-');
		if (parts.length !== 2) return m;
		const d = new Date(parseInt(parts[0]), parseInt(parts[1]) - 1, 1);
		return d.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
	}
	function fmt(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	}
	function fmt0(n: number) {
		return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
	}

	onMount(async () => {
		await loadAnalyses();
		// If the stored month isn't present in this user's data, fall back to latest.
		if (months.length > 0 && (!selectedMonth || !months.includes(selectedMonth))) {
			selectedMonth = months[0];
		}
	});
	onDestroy(() => { unsub(); unsubMonth(); });

	// ---------- Daily trend chart ----------
	const CHART_W = 320;
	const CHART_H = 140;
	const CHART_PAD_L = 6;
	const CHART_PAD_R = 6;
	const CHART_PAD_T = 12;
	const CHART_PAD_B = 20;
	const plotW = CHART_W - CHART_PAD_L - CHART_PAD_R;
	const plotH = CHART_H - CHART_PAD_T - CHART_PAD_B;

	let hoveredDay = $state<number | null>(null);
	let chartEl: SVGSVGElement | null = $state(null);

	function xAt(i: number, count: number) {
		if (count <= 1) return CHART_PAD_L + plotW / 2;
		return CHART_PAD_L + (i * plotW) / (count - 1);
	}
	function yAt(amount: number) {
		if (maxDay <= 0) return CHART_PAD_T + plotH;
		return CHART_PAD_T + plotH - (amount / maxDay) * plotH;
	}

	const trendPath = $derived.by(() => {
		const days = current.dailyTotals;
		if (days.length === 0) return '';
		return days
			.map((d, i) => `${i === 0 ? 'M' : 'L'} ${xAt(i, days.length).toFixed(1)} ${yAt(d.amount).toFixed(1)}`)
			.join(' ');
	});
	const trendArea = $derived.by(() => {
		if (!trendPath) return '';
		const days = current.dailyTotals;
		const last = xAt(days.length - 1, days.length);
		const first = xAt(0, days.length);
		return `${trendPath} L ${last.toFixed(1)} ${(CHART_PAD_T + plotH).toFixed(1)} L ${first.toFixed(1)} ${(CHART_PAD_T + plotH).toFixed(1)} Z`;
	});

	function onChartMove(ev: MouseEvent | TouchEvent) {
		if (!chartEl) return;
		const rect = chartEl.getBoundingClientRect();
		const clientX = 'touches' in ev ? ev.touches[0]?.clientX : ev.clientX;
		if (clientX == null) return;
		const xRatio = (clientX - rect.left) / rect.width;
		const svgX = xRatio * CHART_W;
		const days = current.dailyTotals;
		if (days.length === 0) return;
		let best = 0;
		let bestDist = Infinity;
		for (let i = 0; i < days.length; i++) {
			const dist = Math.abs(xAt(i, days.length) - svgX);
			if (dist < bestDist) {
				bestDist = dist;
				best = i;
			}
		}
		hoveredDay = best;
	}
	function onChartLeave() {
		hoveredDay = null;
	}

	const hoveredInfo = $derived.by(() => {
		if (hoveredDay === null) return null;
		const d = current.dailyTotals[hoveredDay];
		if (!d) return null;
		const dt = new Date(d.iso + 'T00:00:00');
		return {
			d,
			label: dt.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' })
		};
	});

	// ---------- Day-of-week card (interactive) ----------
	let activeWeekday = $state<number | null>(null);
	const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
	const dayOfWeekStats = $derived.by(() => {
		const totals = [0, 0, 0, 0, 0, 0, 0];
		const counts = [0, 0, 0, 0, 0, 0, 0];
		for (const t of current.txs) {
			if (!t.iso) continue;
			const dt = new Date(t.iso + 'T00:00:00');
			const w = dt.getDay();
			totals[w] += t.amount;
			counts[w] += 1;
		}
		const max = Math.max(1, ...totals);
		return totals.map((total, i) => ({
			idx: i,
			name: WEEKDAYS[i],
			total,
			count: counts[i],
			pct: total / max
		}));
	});
	const selectedWeekdayTxs = $derived.by(() => {
		if (activeWeekday === null) return [] as TxLite[];
		return current.txs
			.filter((t) => {
				if (!t.iso) return false;
				return new Date(t.iso + 'T00:00:00').getDay() === activeWeekday;
			})
			.sort((a, b) => b.amount - a.amount)
			.slice(0, 6);
	});
	const activeWeekdayInfo = $derived.by(() => {
		if (activeWeekday === null) return null;
		const s = dayOfWeekStats[activeWeekday];
		return s && s.count > 0 ? s : null;
	});

	// ---------- Insight card: pick one AI suggestion to feature ----------
	const summaryLine = $derived.by(() => {
		if (!selectedMonth) return '';
		const n = current.txs.length;
		const cats = current.categories.length;
		if (n === 0) return '';
		return `${n} purchases across ${cats} ${cats === 1 ? 'category' : 'categories'}.`;
	});

	// ---------- Local fun facts (computed from THIS month's actual txns,
	// so they match the calendar view regardless of statement cycles). ----------
	interface FunFact { emoji: string; label: string; value: string; }
	const localFunStats = $derived.by(() => {
		const txs = current.txs;
		const out: FunFact[] = [];
		if (txs.length === 0) return out;

		// Busiest day
		let busiest = current.dailyTotals[0];
		for (const d of current.dailyTotals) if (d.amount > (busiest?.amount ?? 0)) busiest = d;
		if (busiest && busiest.amount > 0) {
			const dt = new Date(busiest.iso + 'T00:00:00');
			out.push({
				emoji: '📅',
				label: 'Busiest day',
				value: `${dt.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} — $${fmt0(busiest.amount)} (${busiest.count} ${busiest.count === 1 ? 'transaction' : 'transactions'})`
			});
		}

		// Top merchant
		const byMerch = new Map<string, { amount: number; count: number }>();
		for (const t of txs) {
			const cur = byMerch.get(t.merchant) ?? { amount: 0, count: 0 };
			cur.amount += t.amount;
			cur.count += 1;
			byMerch.set(t.merchant, cur);
		}
		const topMerch = Array.from(byMerch.entries()).sort((a, b) => b[1].amount - a[1].amount)[0];
		if (topMerch) {
			out.push({
				emoji: '🏆',
				label: 'Top merchant',
				value: `${topMerch[0]} — $${fmt0(topMerch[1].amount)} across ${topMerch[1].count} visit${topMerch[1].count === 1 ? '' : 's'}`
			});
		}

		// Total transactions & avg per txn
		out.push({
			emoji: '🧾',
			label: 'Transactions',
			value: `${txs.length} — avg $${fmt0(current.totalSpent / txs.length)} each`
		});

		// Longest no-spend streak (only count days up to latest spending day,
		// so a month-in-progress doesn't look like a giant streak).
		let lastActive = -1;
		for (let i = current.dailyTotals.length - 1; i >= 0; i--) {
			if (current.dailyTotals[i].amount > 0) {
				lastActive = i;
				break;
			}
		}
		if (lastActive > 0) {
			let streak = 0;
			let maxStreak = 0;
			for (let i = 0; i <= lastActive; i++) {
				if (current.dailyTotals[i].amount === 0) {
					streak++;
					if (streak > maxStreak) maxStreak = streak;
				} else streak = 0;
			}
			if (maxStreak > 0) {
				out.push({
					emoji: '🧘',
					label: 'Longest no-spend streak',
					value: `${maxStreak} day${maxStreak === 1 ? '' : 's'} in a row`
				});
			}
		}

		// Eat out stat — categorical & relatable
		const eatOut = txs.filter((t) => t.category === 'Eat Out');
		if (eatOut.length > 0) {
			const total = eatOut.reduce((s, t) => s + t.amount, 0);
			out.push({
				emoji: '🍔',
				label: 'Eating out',
				value: `${eatOut.length} meal${eatOut.length === 1 ? '' : 's'} — $${fmt0(total)} total`
			});
		}

		// Weekend vs weekday
		let weekend = 0;
		let weekday = 0;
		for (const t of txs) {
			const w = new Date(t.iso + 'T00:00:00').getDay();
			if (w === 0 || w === 6) weekend += t.amount;
			else weekday += t.amount;
		}
		if (weekend + weekday > 0) {
			const pct = Math.round((weekend / (weekend + weekday)) * 100);
			out.push({
				emoji: '🎉',
				label: 'Weekend share',
				value: `${pct}% of spending happened on weekends`
			});
		}

		// Biggest single purchase
		const biggest = [...txs].sort((a, b) => b.amount - a.amount)[0];
		if (biggest) {
			out.push({
				emoji: '💥',
				label: 'Biggest purchase',
				value: `$${fmt0(biggest.amount)} at ${biggest.merchant}`
			});
		}

		return out;
	});

	// ---------- Conversational AI section. Builds a few short "messages"
	// that feel like a friend reading your month over your shoulder, and
	// includes any real AI insights/suggestions at the end. ----------
	const firstName = $derived($displayName);

	function catTotal(agg: Agg, name: string): number {
		for (const c of agg.categories) if (c.name === name) return c.total;
		return 0;
	}

	interface AiMsg { kind: 'hello' | 'insight' | 'suggest' | 'news'; text: string; }
	const aiMessages = $derived.by<AiMsg[]>(() => {
		const msgs: AiMsg[] = [];
		if (!selectedMonth || current.txs.length === 0) return msgs;

		const monthName = new Date(selectedMonth + '-01').toLocaleDateString(undefined, { month: 'long' });

		// Opener
		if (deltaPct !== null && Math.abs(deltaPct) >= 5) {
			const dir = deltaPct > 0 ? 'up' : 'down';
			msgs.push({
				kind: 'hello',
				text: `Hey ${firstName}! In ${monthName} you spent $${fmt0(current.totalSpent)} — that's ${dir} ${Math.abs(deltaPct).toFixed(0)}% vs. last month's $${fmt0(previous.totalSpent)}.`
			});
		} else if (deltaPct !== null) {
			msgs.push({
				kind: 'hello',
				text: `Hey ${firstName}! ${monthName} came in at $${fmt0(current.totalSpent)} — basically flat vs. last month ($${fmt0(previous.totalSpent)}). Nice consistency.`
			});
		} else {
			msgs.push({
				kind: 'hello',
				text: `Hey ${firstName}! Here's ${monthName} at a glance: $${fmt0(current.totalSpent)} across ${current.txs.length} purchases.`
			});
		}

		// Top category narrative
		if (current.categories.length > 0) {
			const top = current.categories[0];
			const prevTop = catTotal(previous, top.name);
			if (prevTop > 0) {
				const d = ((top.total - prevTop) / prevTop) * 100;
				if (Math.abs(d) >= 10) {
					const verb = d > 0 ? 'up' : 'down';
					msgs.push({
						kind: 'insight',
						text: `Your biggest category was ${top.name} at $${fmt0(top.total)} (${top.percentage.toFixed(0)}% of the month) — that's ${verb} ${Math.abs(d).toFixed(0)}% compared to last month.`
					});
				} else {
					msgs.push({
						kind: 'insight',
						text: `${top.name} led the month at $${fmt0(top.total)} (${top.percentage.toFixed(0)}% of spending), roughly on par with last month.`
					});
				}
			} else {
				msgs.push({
					kind: 'insight',
					text: `${top.name} topped the list at $${fmt0(top.total)} — that's ${top.percentage.toFixed(0)}% of everything you spent.`
				});
			}
		}

		// Movers: find the category with the largest % change (either direction).
		if (previous.totalSpent > 0 && current.categories.length > 1) {
			interface Mover { name: string; cur: number; prev: number; pct: number; }
			const movers: Mover[] = [];
			const allNames = new Set<string>([
				...current.categories.map((c) => c.name),
				...previous.categories.map((c) => c.name)
			]);
			for (const n of allNames) {
				const cur = catTotal(current, n);
				const prev = catTotal(previous, n);
				if (prev < 20 && cur < 20) continue; // ignore trivia
				const pct = prev > 0 ? ((cur - prev) / prev) * 100 : cur > 0 ? 100 : 0;
				movers.push({ name: n, cur, prev, pct });
			}
			movers.sort((a, b) => Math.abs(b.pct) - Math.abs(a.pct));
			const big = movers.find((m) => m.name !== current.categories[0].name && Math.abs(m.pct) >= 15);
			if (big) {
				if (big.pct > 0 && big.prev > 0) {
					msgs.push({
						kind: 'news',
						text: `Heads-up: ${big.name} jumped ${big.pct.toFixed(0)}% — $${fmt0(big.cur)} this month vs. $${fmt0(big.prev)} last month.`
					});
				} else if (big.pct < 0 && big.prev > 0) {
					msgs.push({
						kind: 'news',
						text: `Nice: ${big.name} dropped ${Math.abs(big.pct).toFixed(0)}% — $${fmt0(big.cur)} this month (vs. $${fmt0(big.prev)}).`
					});
				} else if (big.prev === 0 && big.cur > 0) {
					msgs.push({
						kind: 'news',
						text: `New this month: ${big.name} showed up at $${fmt0(big.cur)} — didn't exist in last month's data.`
					});
				}
			}
		}

		// Weekend vs weekday
		let weekend = 0, weekday = 0;
		for (const t of current.txs) {
			const w = new Date(t.iso + 'T00:00:00').getDay();
			if (w === 0 || w === 6) weekend += t.amount;
			else weekday += t.amount;
		}
		if (weekend + weekday > 0) {
			const pct = Math.round((weekend / (weekend + weekday)) * 100);
			if (pct >= 45) {
				msgs.push({
					kind: 'news',
					text: `Weekend-heavy month: ${pct}% of your spending happened Saturday or Sunday.`
				});
			} else if (pct <= 20) {
				msgs.push({
					kind: 'news',
					text: `Weekday-focused month — only ${pct}% of spending was on weekends.`
				});
			}
		}

		// Eat-out specific call-out
		const eat = catTotal(current, 'Eat Out');
		const eatPrev = catTotal(previous, 'Eat Out');
		if (eat > 0 && eatPrev > 0) {
			const d = ((eat - eatPrev) / eatPrev) * 100;
			if (Math.abs(d) >= 20) {
				const verb = d > 0 ? 'up' : 'down';
				msgs.push({
					kind: 'news',
					text: `You spent $${fmt0(eat)} eating out — ${verb} ${Math.abs(d).toFixed(0)}% vs. last month's $${fmt0(eatPrev)}.`
				});
			}
		}

		// Append real AI insights & suggestions (keep as their own kind).
		for (const t of current.insights) msgs.push({ kind: 'insight', text: t });
		for (const t of current.suggestions) msgs.push({ kind: 'suggest', text: t });

		return msgs;
	});
</script>

{#if ready}
	<main class="container" in:fade={{ duration: 300 }}>
		<div class="page-header" in:fly={{ y: 10, duration: 300 }}>
			<h1>📊 Dashboard</h1>
			<p class="muted">Your month at a glance.</p>
		</div>

		{#if summaries.length === 0}
			<div class="empty-hero card" in:fly={{ y: 20, duration: 400 }}>
				<div class="hero-emoji">📊</div>
				<h2>Welcome to your dashboard</h2>
				<p class="muted">Upload your first statement from the Analyze tab to see your numbers here.</p>
				<button class="btn btn-primary" onclick={() => goto('/analyze')}>Go to Analyze</button>
			</div>
		{:else}
			<section class="month-nav card" in:fly={{ y: 10, duration: 300 }}>
				<button class="nav-btn" onclick={() => navMonth(-1)} disabled={!hasPrev} aria-label="Previous month">‹</button>
				<div class="month-title-wrap">
					<div class="month-title">{formatMonth(selectedMonth)}</div>
					<div class="month-total">
						${fmt0(current.totalSpent)}
						{#if deltaPct !== null}
							<span class="delta" class:up={deltaPct > 0} class:down={deltaPct < 0}>
								{deltaPct > 0 ? '▲' : deltaPct < 0 ? '▼' : '·'} {Math.abs(deltaPct).toFixed(0)}%
							</span>
						{/if}
					</div>
				</div>
				<button class="nav-btn" onclick={() => navMonth(1)} disabled={!hasNext} aria-label="Next month">›</button>
			</section>

			{#if loading && currentTxs.length === 0}
				<div class="card"><p>Loading…</p></div>
			{:else if currentTxs.length === 0}
				<div class="card"><p class="muted">No transactions in this month.</p></div>
			{:else}
				<!-- Box 1: Category bubbles -->
				<section class="card bubbles-card" in:fly={{ y: 16, duration: 320, delay: 40 }}>
					<CategoryBubbles
						categories={current.categories}
						totalSpent={current.totalSpent}
					/>
				</section>

				<!-- Box 2: Daily trend (minimal, interactive) -->
				<section class="card" in:fly={{ y: 16, duration: 320, delay: 80 }}>
					<h3 class="box-title">Daily trend</h3>
					<div class="trend-stats">
						<div>
							<div class="stat-label">Active days</div>
							<div class="stat-val">{activeDays}</div>
						</div>
						<div>
							<div class="stat-label">Avg / day</div>
							<div class="stat-val">${fmt0(avgPerActiveDay)}</div>
						</div>
						<div>
							<div class="stat-label">Biggest day</div>
							<div class="stat-val">${fmt0(maxDay)}</div>
						</div>
					</div>

					<div class="chart-wrap">
						<!-- svelte-ignore a11y_no_static_element_interactions -->
						<svg
							bind:this={chartEl}
							viewBox="0 0 {CHART_W} {CHART_H}"
							preserveAspectRatio="none"
							class="chart"
							onmousemove={onChartMove}
							onmouseleave={onChartLeave}
							ontouchmove={onChartMove}
							ontouchend={onChartLeave}
							role="img"
							aria-label="Daily spending trend"
						>
							<defs>
								<linearGradient id="trend-fill" x1="0" x2="0" y1="0" y2="1">
									<stop offset="0%" stop-color="#6366f1" stop-opacity="0.22" />
									<stop offset="100%" stop-color="#6366f1" stop-opacity="0" />
								</linearGradient>
							</defs>

							<!-- grid: horizontal lines at 0/50/100% -->
							{#each [0, 0.5, 1] as frac}
								<line
									x1={CHART_PAD_L}
									x2={CHART_W - CHART_PAD_R}
									y1={CHART_PAD_T + plotH - frac * plotH}
									y2={CHART_PAD_T + plotH - frac * plotH}
									stroke="#eef2f7"
									stroke-width="0.5"
									stroke-dasharray={frac === 0 ? '' : '2 3'}
								/>
							{/each}

							{#if trendPath}
								<path d={trendArea} fill="url(#trend-fill)" />
								<path
									d={trendPath}
									fill="none"
									stroke="#6366f1"
									stroke-width="1.25"
									stroke-linecap="round"
									stroke-linejoin="round"
								/>

								<!-- Scrubber line only (no per-day dot markers, per design) -->
								{#if hoveredDay !== null}
									{@const hx = xAt(hoveredDay, current.dailyTotals.length)}
									{@const hy = yAt(current.dailyTotals[hoveredDay]?.amount ?? 0)}
									<line
										x1={hx}
										x2={hx}
										y1={CHART_PAD_T}
										y2={CHART_PAD_T + plotH}
										stroke="#6366f1"
										stroke-width="1"
										stroke-dasharray="3 2"
										opacity="0.55"
									/>
									{#if (current.dailyTotals[hoveredDay]?.amount ?? 0) > 0}
										<circle cx={hx} cy={hy} r="3" fill="#6366f1" stroke="#fff" stroke-width="1.5" />
									{/if}
								{/if}
							{/if}
						</svg>

						{#if hoveredInfo}
							<div class="trend-readout">
								<span class="trend-readout-label">{hoveredInfo.label}</span>
								<span class="trend-readout-amt">
									{#if hoveredInfo.d.amount > 0}
										${fmt(hoveredInfo.d.amount)}
									{:else}
										no spending
									{/if}
								</span>
							</div>
						{/if}
					</div>
					<div class="axis-row">
						<span>1</span>
						<span>15</span>
						<span>{current.dailyTotals.length || 31}</span>
					</div>
				</section>

				<!-- Box 3: AI — conversational, multi-sentence "talking to you" -->
				<section class="card ai-card" in:fly={{ y: 16, duration: 320, delay: 120 }}>
					<div class="ai-header">
						<div class="ai-avatar" aria-hidden="true">✨</div>
						<div>
							<div class="ai-name">Spending Coach</div>
							<div class="ai-sub">Powered by Azure AI Foundry</div>
						</div>
					</div>

					{#if aiMessages.length === 0}
						<p class="ai-empty muted">Not enough data yet — add a couple of statements and I'll have something to say.</p>
					{:else}
						<div class="ai-bubbles">
							{#each aiMessages as m, i (i)}
								<div
									class="ai-bubble"
									class:insight={m.kind === 'insight'}
									class:suggest={m.kind === 'suggest'}
									class:news={m.kind === 'news'}
									in:fly={{ y: 6, duration: 240, delay: i * 80 }}
								>
									<span class="ai-bubble-tag">
										{m.kind === 'hello' ? '👋' : m.kind === 'suggest' ? '💰' : m.kind === 'news' ? '📰' : '💡'}
									</span>
									<p class="ai-bubble-text">{m.text}</p>
								</div>
							{/each}
						</div>
					{/if}
				</section>

				<!-- Box 4: Fun facts (computed locally from this month's txns) -->
				{#if localFunStats.length > 0}
					<section class="card" in:fly={{ y: 16, duration: 320, delay: 160 }}>
						<h3 class="box-title">✨ Fun facts</h3>
						<div class="fun-grid">
							{#each localFunStats as fs}
								<div class="fun-card">
									<div class="fun-emoji">{fs.emoji}</div>
									<div class="fun-body">
										<div class="fun-label">{fs.label}</div>
										<div class="fun-value">{fs.value}</div>
									</div>
								</div>
							{/each}
						</div>
					</section>
				{/if}

				<!-- Box 5: Day-of-week (interactive) -->
				<section class="card" in:fly={{ y: 16, duration: 320, delay: 160 }}>
					<h3 class="box-title">Spending by weekday</h3>
					<p class="muted hint">Tap a bar to see your biggest purchases that day-of-week.</p>
					<div class="dow-bars" role="group" aria-label="Weekday spending">
						{#each dayOfWeekStats as s}
							<button
								type="button"
								class="dow-bar"
								class:active={activeWeekday === s.idx}
								disabled={s.count === 0}
								onclick={() => (activeWeekday = activeWeekday === s.idx ? null : s.idx)}
								aria-label={`${s.name}: $${fmt(s.total)} across ${s.count} transactions`}
							>
								<div class="dow-fill-wrap">
									<div class="dow-fill" style="height: {Math.max(s.pct * 100, s.total > 0 ? 6 : 0)}%"></div>
								</div>
								<div class="dow-amt">${fmt0(s.total)}</div>
								<div class="dow-name">{s.name}</div>
							</button>
						{/each}
					</div>

					{#if activeWeekdayInfo}
						<div class="dow-detail" in:fade={{ duration: 140 }}>
							<div class="dow-detail-hd">
								<strong>{activeWeekdayInfo.name}s</strong>
								<span>{activeWeekdayInfo.count} purchase{activeWeekdayInfo.count === 1 ? '' : 's'} · ${fmt(activeWeekdayInfo.total)} total</span>
							</div>
							{#if selectedWeekdayTxs.length > 0}
								<ul class="dow-txs">
									{#each selectedWeekdayTxs as t}
										<li>
											<span class="dow-tx-dot" style="background: {categoryColor(t.category)}"></span>
											<span class="dow-tx-name">{t.merchant}</span>
											<span class="dow-tx-amt">${fmt(t.amount)}</span>
										</li>
									{/each}
								</ul>
							{/if}
						</div>
					{/if}
				</section>
			{/if}
		{/if}
	</main>
{:else}
	<div class="page-loading" in:fade={{ duration: 150 }}>
		<div class="spinner" aria-hidden="true"></div>
		<div class="page-loading-text">Loading…</div>
	</div>
{/if}

<style>
	.container {
		max-width: 720px;
		margin: 16px auto;
		padding: 0 16px 60px;
		display: flex;
		flex-direction: column;
		gap: 14px;
	}
	.page-header h1 {
		font-size: 1.5rem;
	}
	.page-header p {
		margin-top: 2px;
		font-size: 0.9rem;
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
	.empty-hero .btn {
		margin-top: 12px;
	}
	.muted {
		color: #667085;
	}
	.hint {
		font-size: 0.78rem;
		margin-top: -2px;
		margin-bottom: 8px;
	}

	.month-nav {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 10px 12px;
		gap: 8px;
	}
	.month-title-wrap {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 2px;
		min-width: 0;
	}
	.month-title {
		font-weight: 700;
		font-size: 1.05rem;
	}
	.month-total {
		font-size: 0.85rem;
		color: #64748b;
		font-variant-numeric: tabular-nums;
	}
	.delta {
		font-weight: 700;
		margin-left: 4px;
	}
	.delta.up { color: #dc2626; }
	.delta.down { color: #059669; }
	.nav-btn {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		border: 1px solid #e2e8f0;
		background: #fff;
		font-size: 1.3rem;
		line-height: 1;
		color: #334155;
		cursor: pointer;
		flex-shrink: 0;
	}
	.nav-btn:disabled { opacity: 0.35; cursor: default; }

	.bubbles-card { padding: 14px 14px 16px; }

	.box-title {
		font-size: 0.95rem;
		margin-bottom: 8px;
		color: #334155;
	}

	.trend-stats {
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 8px;
		background: #f8fafc;
		border-radius: 12px;
		padding: 10px;
		margin-bottom: 10px;
	}
	.trend-stats > div { text-align: center; }
	.stat-label {
		font-size: 0.68rem;
		color: #64748b;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		font-weight: 600;
	}
	.stat-val {
		font-size: 1rem;
		font-weight: 700;
		margin-top: 2px;
		font-variant-numeric: tabular-nums;
	}
	.chart-wrap {
		width: 100%;
	}
	.chart {
		width: 100%;
		height: 160px;
		display: block;
		touch-action: pan-y;
	}
	.axis-row {
		display: flex;
		justify-content: space-between;
		color: #94a3b8;
		font-size: 0.7rem;
		margin-top: 4px;
		padding: 0 6px;
	}
	.trend-readout {
		display: flex;
		justify-content: space-between;
		align-items: baseline;
		margin-top: 6px;
		padding: 6px 10px;
		background: #eef2ff;
		border-radius: 8px;
		font-size: 0.85rem;
		font-variant-numeric: tabular-nums;
	}
	.trend-readout-label { color: #3730a3; font-weight: 600; }
	.trend-readout-amt { color: #3730a3; font-weight: 700; }

	/* Day of week bars */
	.dow-bars {
		display: grid;
		grid-template-columns: repeat(7, 1fr);
		gap: 6px;
		align-items: end;
		margin-top: 4px;
	}
	.dow-bar {
		border: none;
		background: transparent;
		padding: 6px 2px;
		border-radius: 10px;
		cursor: pointer;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 4px;
		color: #475569;
		transition: background 0.15s, transform 0.15s;
	}
	.dow-bar:not(:disabled):hover {
		background: #ecfdf5;
	}
	.dow-bar.active {
		background: #d1fae5;
		transform: translateY(-1px);
	}
	.dow-bar:disabled {
		opacity: 0.45;
		cursor: not-allowed;
	}
	.dow-fill-wrap {
		width: 100%;
		height: 80px;
		background: linear-gradient(to top, #f1f5f9 0%, #f8fafc 100%);
		border-radius: 6px;
		display: flex;
		align-items: flex-end;
		overflow: hidden;
	}
	.dow-fill {
		width: 100%;
		background: linear-gradient(to top, #059669, #34d399);
		border-radius: 6px 6px 0 0;
		transition: height 0.3s ease;
	}
	.dow-bar.active .dow-fill {
		background: linear-gradient(to top, #047857, #10b981);
	}
	.dow-amt {
		font-size: 0.65rem;
		font-weight: 700;
		color: #334155;
		font-variant-numeric: tabular-nums;
	}
	.dow-name {
		font-size: 0.68rem;
		font-weight: 600;
		color: #64748b;
	}
	.dow-bar.active .dow-name {
		color: #047857;
	}
	.dow-detail {
		margin-top: 12px;
		padding: 10px 12px;
		background: #f8fafc;
		border-radius: 10px;
	}
	.dow-detail-hd {
		display: flex;
		justify-content: space-between;
		align-items: baseline;
		margin-bottom: 8px;
		font-size: 0.9rem;
		color: #475569;
	}
	.dow-txs {
		list-style: none;
		padding: 0;
		display: flex;
		flex-direction: column;
		gap: 4px;
	}
	.dow-txs li {
		display: grid;
		grid-template-columns: 10px 1fr auto;
		gap: 8px;
		align-items: center;
		font-size: 0.85rem;
	}
	.dow-tx-dot {
		width: 8px;
		height: 8px;
		border-radius: 2px;
	}
	.dow-tx-name {
		color: #334155;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
	.dow-tx-amt {
		font-weight: 700;
		color: #1f2937;
		font-variant-numeric: tabular-nums;
	}

	.fun-card {
		display: flex;
		gap: 12px;
		align-items: center;
		padding: 14px;
		background: linear-gradient(135deg, #ecfdf5 0%, #f0fdfa 100%);
		border: 1px solid #a7f3d0;
		border-radius: 12px;
	}
	.fun-emoji {
		font-size: 2rem;
		line-height: 1;
		flex-shrink: 0;
	}
	.fun-body {
		min-width: 0;
	}
	.fun-label {
		font-size: 0.72rem;
		color: #047857;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.04em;
	}
	.fun-value {
		color: #065f46;
		font-weight: 600;
		font-size: 0.95rem;
		margin-top: 2px;
		line-height: 1.35;
	}

	/* ---- AI card ---- */
	.ai-card {
		background: linear-gradient(135deg, #faf5ff 0%, #f5f3ff 60%, #ede9fe 100%);
		border: 1px solid #ddd6fe;
	}
	.ai-header {
		display: flex;
		align-items: center;
		gap: 10px;
		margin-bottom: 10px;
	}
	.ai-avatar {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		display: flex;
		align-items: center;
		justify-content: center;
		background: linear-gradient(135deg, #8b5cf6, #6366f1);
		color: white;
		font-size: 1.1rem;
		flex-shrink: 0;
		box-shadow: 0 4px 12px rgba(139, 92, 246, 0.35);
	}
	.ai-name {
		font-size: 0.95rem;
		font-weight: 700;
		color: #4c1d95;
	}
	.ai-sub {
		font-size: 0.68rem;
		color: #7c3aed;
		letter-spacing: 0.02em;
	}
	.ai-empty {
		font-size: 0.88rem;
		padding: 4px 2px;
	}
	.ai-bubbles {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}
	.ai-bubble {
		display: flex;
		gap: 8px;
		align-items: flex-start;
		padding: 10px 12px;
		background: rgba(255, 255, 255, 0.75);
		border: 1px solid #e9d5ff;
		border-radius: 14px;
		border-top-left-radius: 4px;
	}
	.ai-bubble.suggest {
		background: rgba(236, 253, 245, 0.85);
		border-color: #a7f3d0;
	}
	.ai-bubble.news {
		background: rgba(224, 242, 254, 0.85);
		border-color: #bae6fd;
	}
	.ai-bubble-tag {
		font-size: 1rem;
		flex-shrink: 0;
		line-height: 1.4;
	}
	.ai-bubble-text {
		margin: 0;
		font-size: 0.9rem;
		line-height: 1.5;
		color: #312e81;
	}
	.ai-bubble.suggest .ai-bubble-text { color: #065f46; }
	.ai-bubble.news .ai-bubble-text { color: #0c4a6e; }

	/* Fun-facts grid (replaces single-carousel fun stats) */
	.fun-grid {
		display: grid;
		grid-template-columns: 1fr;
		gap: 8px;
	}
	@media (min-width: 520px) {
		.fun-grid { grid-template-columns: 1fr 1fr; }
	}
</style>
