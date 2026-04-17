import { writable } from 'svelte/store';

// Global flag set while an analysis request is in flight.
// Used by the layout to prevent navigation away from the Analyze page mid-run.
export const isAnalyzing = writable(false);
