import * as AppleAuthentication from 'expo-apple-authentication';
import * as Linking from 'expo-linking';
import * as WebBrowser from 'expo-web-browser';

import { AuthError, browserStartUrl, completeBrowserSignIn, pkcePair, randomToken, sha256Hex, signInWithApple } from './auth';

// Lets the auth popup close itself on web; a no-op on native
WebBrowser.maybeCompleteAuthSession();

export type BrowserProvider = 'google' | 'ebay' | 'oidc';

const callbackErrors: Record<string, string> = {
  cancelled: '',
  provider_error: "Sign-in didn't finish. Try again.",
  account_disabled: "This account can't sign in.",
};

// Google, eBay and SSO run in the system browser via the API (works in Expo Go). Returns false if
// the user closed the browser; throws AuthError with a message to show otherwise.
export async function signInWithBrowser(provider: BrowserProvider, email?: string): Promise<boolean> {
  // exp://<host>/--/auth-callback in Expo Go, cardscout://auth-callback in a build
  const redirectUri = Linking.createURL('auth-callback');
  const { verifier, challenge } = await pkcePair();
  const result = await WebBrowser.openAuthSessionAsync(browserStartUrl(provider, redirectUri, challenge, email), redirectUri);
  if (result.type !== 'success') return false;

  const { queryParams } = Linking.parse(result.url);
  const error = typeof queryParams?.error === 'string' ? queryParams.error : null;
  if (error) {
    const message = callbackErrors[error] ?? error;
    if (!message) return false;
    throw new AuthError(message);
  }
  const code = typeof queryParams?.code === 'string' ? queryParams.code : null;
  if (!code) throw new AuthError("Sign-in didn't finish. Try again.");
  await completeBrowserSignIn(provider, code, verifier);
  return true;
}

export const appleSignInAvailable = () => AppleAuthentication.isAvailableAsync().catch(() => false);

// Sign in with Apple (native sheet; works in Expo Go on iOS). Apple shares the name and email only
// the first time, so they're forwarded to the API right away. Returns false if cancelled.
export async function signInWithAppleNative(): Promise<boolean> {
  const rawNonce = randomToken(16);
  try {
    const credential = await AppleAuthentication.signInAsync({
      requestedScopes: [AppleAuthentication.AppleAuthenticationScope.FULL_NAME, AppleAuthentication.AppleAuthenticationScope.EMAIL],
      nonce: await sha256Hex(rawNonce),
    });
    if (!credential.identityToken) throw new AuthError("Apple didn't return a sign-in token. Try again.");
    await signInWithApple(credential.identityToken, rawNonce, credential.fullName?.givenName, credential.fullName?.familyName);
    return true;
  } catch (e) {
    if (e && typeof e === 'object' && 'code' in e && e.code === 'ERR_REQUEST_CANCELED') return false;
    throw e;
  }
}
