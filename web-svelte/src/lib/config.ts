// In production we go through the SWA reverse proxy at /api, so SWA can
// inject the signed x-ms-client-principal header. In local dev we hit the
// Functions host directly on :7071.
const dev = typeof window !== 'undefined' && (window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1');

export const API_BASE = dev
	? 'http://localhost:7071/api'
	: '/api';
