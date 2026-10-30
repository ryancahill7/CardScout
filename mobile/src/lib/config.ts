// EXPO_PUBLIC_* values are inlined into the JS bundle at build time, so the API key
// here is a convenience for a single-user setup, not a real secret.
export const API_URL = (process.env.EXPO_PUBLIC_API_URL ?? '').replace(/\/+$/, '');
export const API_KEY = process.env.EXPO_PUBLIC_API_KEY ?? '';

export const isConfigured = API_URL.length > 0 && API_KEY.length > 0;
