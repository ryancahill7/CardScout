// EXPO_PUBLIC_* values are inlined into the JS bundle at build time, so the API key
// here is a convenience for a single-user dev setup, not a real secret. Real users sign in
// (see lib/auth.ts); when EXPO_PUBLIC_API_KEY is set the app also offers "Use dev API key".
export const API_URL = (process.env.EXPO_PUBLIC_API_URL ?? '').replace(/\/+$/, '');
export const API_KEY = process.env.EXPO_PUBLIC_API_KEY ?? '';

export const isConfigured = API_URL.length > 0;
