import * as Crypto from 'expo-crypto';
import * as SecureStore from 'expo-secure-store';
import { AppState } from 'react-native';
import { useSyncExternalStore } from 'react';

import { API_KEY, API_URL } from './config';

// Session state for the app. Signed-in users hold a short-lived access token (sent as
// Authorization: Bearer) and a rotating refresh token, both in the iOS Keychain via expo-secure-store.
// A dev API key from mobile/.env still works as a fallback session ("Use dev API key").

export interface AuthUser {
  id: number;
  email: string | null;
  displayName: string | null;
  emailVerified: boolean;
  hasPassword: boolean;
  providers: string[];
}

interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser;
}

type Session =
  | { kind: 'tokens'; accessToken: string; accessExpiresAt: number; refreshToken: string; user: AuthUser | null }
  | { kind: 'apikey' };

export type AuthStatus = 'loading' | 'signedOut' | 'signedIn';

export interface AuthState {
  status: AuthStatus;
  method: 'tokens' | 'apikey' | null;
  user: AuthUser | null;
}

export interface AuthProviders {
  password: boolean;
  apple: boolean;
  google: boolean;
  ebay: boolean;
  sso: { enabled: boolean; displayName: string; requiresEmail: boolean };
}

export class AuthError extends Error {
  constructor(message: string, public status?: number) {
    super(message);
  }
}

const KEYS = {
  refresh: 'cardscout.refreshToken',
  access: 'cardscout.accessToken',
  accessExpires: 'cardscout.accessExpiresAt',
  user: 'cardscout.user',
  // Set after signing out of the dev-key session, so the sign-in screen shows instead
  devKeyOff: 'cardscout.devKeyOff',
} as const;

const storeOptions: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK };

export const hasDevApiKey = API_KEY.length > 0;

// ---- Store (useSyncExternalStore) ----

let session: Session | null = null;
let state: AuthState = { status: 'loading', method: null, user: null };
const listeners = new Set<() => void>();

function emit() {
  state = {
    status: session ? 'signedIn' : 'signedOut',
    method: session?.kind ?? null,
    user: session?.kind === 'tokens' ? session.user : null,
  };
  listeners.forEach((l) => l());
}

export function useAuth(): AuthState {
  return useSyncExternalStore(
    (l) => {
      listeners.add(l);
      return () => listeners.delete(l);
    },
    () => state,
  );
}

let loaded: Promise<void> | null = null;

// Reads the saved session once at startup
export function loadSession(): Promise<void> {
  loaded ??= (async () => {
    try {
      const [refresh, access, expires, user, devKeyOff] = await Promise.all([
        SecureStore.getItemAsync(KEYS.refresh),
        SecureStore.getItemAsync(KEYS.access),
        SecureStore.getItemAsync(KEYS.accessExpires),
        SecureStore.getItemAsync(KEYS.user),
        SecureStore.getItemAsync(KEYS.devKeyOff),
      ]);
      if (refresh) {
        session = {
          kind: 'tokens',
          refreshToken: refresh,
          accessToken: access ?? '',
          accessExpiresAt: Number(expires ?? 0),
          user: user ? (JSON.parse(user) as AuthUser) : null,
        };
      } else if (hasDevApiKey && devKeyOff !== '1') {
        session = { kind: 'apikey' };
      }
    } catch (e) {
      console.warn('Could not read the saved session', e);
    }
    emit();
    scheduleRefresh();
  })();
  return loaded;
}

async function saveTokens(r: AuthResponse) {
  session = {
    kind: 'tokens',
    accessToken: r.accessToken,
    accessExpiresAt: Date.parse(r.accessTokenExpiresAt),
    refreshToken: r.refreshToken,
    user: r.user,
  };
  await Promise.all([
    SecureStore.setItemAsync(KEYS.refresh, r.refreshToken, storeOptions),
    SecureStore.setItemAsync(KEYS.access, r.accessToken, storeOptions),
    SecureStore.setItemAsync(KEYS.accessExpires, String(session.accessExpiresAt), storeOptions),
    SecureStore.setItemAsync(KEYS.user, JSON.stringify(r.user), storeOptions),
  ]);
  emit();
  scheduleRefresh();
}

async function clearTokens() {
  session = null;
  await Promise.all(Object.values(KEYS).filter((k) => k !== KEYS.devKeyOff).map((k) => SecureStore.deleteItemAsync(k)));
}

// ---- Token refresh ----

let refreshing: Promise<boolean> | null = null;

// Swaps the refresh token for a new pair. Single-flight: concurrent 401s share one refresh, because
// presenting the same refresh token twice looks like token theft to the API and ends the session.
// Resolves false (and signs out) when the API rejects the session; throws on network errors.
export function refreshSession(): Promise<boolean> {
  if (session?.kind !== 'tokens') return Promise.resolve(false);
  const refreshToken = session.refreshToken;
  refreshing ??= (async () => {
    try {
      const response = await rawFetch('/api/auth/refresh', { refreshToken });
      if (response.status === 401) {
        await signOutLocal();
        return false;
      }
      if (!response.ok) throw new AuthError(`Couldn't refresh the session (${response.status})`, response.status);
      await saveTokens((await response.json()) as AuthResponse);
      return true;
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}

let refreshTimer: ReturnType<typeof setTimeout> | null = null;

// Refresh a minute before the access token expires, so image requests (which can't retry) keep working
function scheduleRefresh() {
  if (refreshTimer) clearTimeout(refreshTimer);
  refreshTimer = null;
  if (session?.kind !== 'tokens') return;
  const delay = Math.max(session.accessExpiresAt - Date.now() - 60_000, 0);
  refreshTimer = setTimeout(() => {
    if (AppState.currentState === 'active') void refreshSession().catch(() => {});
  }, delay);
}

AppState.addEventListener('change', (s) => {
  if (s === 'active' && session?.kind === 'tokens' && session.accessExpiresAt - Date.now() < 60_000) {
    void refreshSession().catch(() => {});
  }
});

// Headers for an API call, refreshing first if the access token is (nearly) expired
export async function getAuthHeaders(): Promise<Record<string, string>> {
  await loadSession();
  if (session?.kind === 'tokens') {
    if (!session.accessToken || session.accessExpiresAt - Date.now() < 30_000) {
      const ok = await refreshSession();
      if (!ok) throw new AuthError('Your session ended. Sign in again.', 401);
    }
    return session?.kind === 'tokens' ? { Authorization: `Bearer ${session.accessToken}` } : {};
  }
  if (session?.kind === 'apikey') return { 'X-Api-Key': API_KEY };
  throw new AuthError('Sign in to continue.', 401);
}

// Synchronous version for <Image source>, which can't await
export function currentAuthHeaders(): Record<string, string> {
  if (session?.kind === 'tokens') return { Authorization: `Bearer ${session.accessToken}` };
  if (session?.kind === 'apikey') return { 'X-Api-Key': API_KEY };
  return {};
}

export const usingBearer = () => session?.kind === 'tokens';

// ---- Sign in / out ----

async function rawFetch(path: string, body?: unknown, init: RequestInit = {}): Promise<Response> {
  try {
    return await fetch(`${API_URL}${path}`, {
      method: body === undefined ? 'GET' : 'POST',
      ...init,
      headers: { Accept: 'application/json', 'Content-Type': 'application/json', ...init.headers },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (e) {
    const detail = e instanceof Error ? e.message : String(e);
    throw new AuthError(`Can't reach the API at ${API_URL} (${detail}). Is it running, and is your phone on the same Wi-Fi?`);
  }
}

async function errorFrom(response: Response): Promise<AuthError> {
  let message = `Something went wrong (${response.status}). Try again.`;
  try {
    const body = await response.json();
    if (body?.error) message = body.error;
  } catch {
    // keep the generic message
  }
  return new AuthError(message, response.status);
}

async function signInWith(path: string, body: unknown) {
  const response = await rawFetch(path, body);
  if (!response.ok) throw await errorFrom(response);
  await SecureStore.deleteItemAsync(KEYS.devKeyOff);
  await saveTokens((await response.json()) as AuthResponse);
}

export async function getProviders(): Promise<AuthProviders> {
  const response = await rawFetch('/api/auth/providers');
  if (!response.ok) throw await errorFrom(response);
  return (await response.json()) as AuthProviders;
}

export const signIn = (email: string, password: string) => signInWith('/api/auth/signin', { email, password });

export const signUp = (email: string, password: string, displayName?: string) =>
  signInWith('/api/auth/signup', { email, password, displayName: displayName || null });

export const signInWithApple = (identityToken: string, nonce: string, givenName?: string | null, familyName?: string | null) =>
  signInWith('/api/auth/apple', { identityToken, nonce, givenName, familyName });

export async function requestPasswordReset(email: string) {
  const response = await rawFetch('/api/auth/password/forgot', { email });
  if (!response.ok && response.status !== 202) throw await errorFrom(response);
}

export async function resetPassword(token: string, newPassword: string) {
  const response = await rawFetch('/api/auth/password/reset', { token, newPassword });
  if (!response.ok) throw await errorFrom(response);
}

export async function signInWithDevKey() {
  if (!hasDevApiKey) return;
  await SecureStore.deleteItemAsync(KEYS.devKeyOff);
  session = { kind: 'apikey' };
  emit();
}

async function signOutLocal() {
  await clearTokens();
  if (refreshTimer) clearTimeout(refreshTimer);
  // Without this the dev key would sign the user straight back in
  if (hasDevApiKey) await SecureStore.setItemAsync(KEYS.devKeyOff, '1', storeOptions);
  emit();
}

// Revokes the refresh token on the API (best effort) and forgets the session on this device
export async function signOut() {
  const current = session;
  if (current?.kind === 'tokens') {
    try {
      await rawFetch('/api/auth/logout', { refreshToken: current.refreshToken });
    } catch {
      // offline: the local session is still cleared; the token expires on its own
    }
  }
  await signOutLocal();
}

// After DELETE /api/auth/account succeeded
export async function forgetAccount() {
  await signOutLocal();
}

export async function updateCachedUser(user: AuthUser) {
  if (session?.kind !== 'tokens') return;
  session = { ...session, user };
  await SecureStore.setItemAsync(KEYS.user, JSON.stringify(user), storeOptions);
  emit();
}

// ---- Browser sign-in (Google, eBay, SSO) ----
// The API runs the provider flow and sends the browser back to the app with a one-time code,
// which only works together with this PKCE verifier. Tokens never travel in a URL.

function base64Url(bytes: Uint8Array): string {
  let binary = '';
  bytes.forEach((b) => (binary += String.fromCharCode(b)));
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function randomToken(bytes = 32): string {
  return base64Url(Crypto.getRandomBytes(bytes));
}

export async function pkcePair() {
  const verifier = randomToken(32);
  const digest = await Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, verifier, {
    encoding: Crypto.CryptoEncoding.BASE64,
  });
  const challenge = digest.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return { verifier, challenge };
}

export function sha256Hex(value: string) {
  return Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, value, { encoding: Crypto.CryptoEncoding.HEX });
}

export function browserStartUrl(provider: 'google' | 'ebay' | 'oidc', redirectUri: string, challenge: string, email?: string) {
  const params = new URLSearchParams({ redirectUri, codeChallenge: challenge });
  if (email) params.set('email', email);
  return `${API_URL}/api/auth/${provider}/start?${params.toString()}`;
}

export async function completeBrowserSignIn(provider: 'google' | 'ebay' | 'oidc', code: string, codeVerifier: string) {
  await signInWith(`/api/auth/${provider}/complete`, { code, codeVerifier });
}

// ---- Account ----

export async function deleteAccount() {
  const headers = await getAuthHeaders();
  const response = await rawFetch('/api/auth/account', undefined, { method: 'DELETE', headers });
  if (!response.ok && response.status !== 204) throw await errorFrom(response);
  await forgetAccount();
}

export async function fetchMe(): Promise<{ user: AuthUser; authMethod: string }> {
  const headers = await getAuthHeaders();
  const response = await rawFetch('/api/auth/me', undefined, { headers });
  if (!response.ok) throw await errorFrom(response);
  const me = (await response.json()) as { user: AuthUser; authMethod: string };
  await updateCachedUser(me.user);
  return me;
}
