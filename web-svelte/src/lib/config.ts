// Toggle between local dev and production
const dev = typeof window !== 'undefined' && (window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1');

export const API_BASE = dev
	? 'http://localhost:7071/api'
	: 'https://func-spendingsuggestion-dev.azurewebsites.net/api';
