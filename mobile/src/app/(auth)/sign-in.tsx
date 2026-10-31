import * as AppleAuthentication from 'expo-apple-authentication';
import { useQuery } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { Platform, StyleSheet, Text, View } from 'react-native';

import { AuthInput, TextInputHandle, AuthScreen, authStyles, Divider, ProviderButton, TextLink } from '@/components/auth-ui';
import { BrandMark } from '@/components/brand-mark';
import { Button, ErrorBanner } from '@/components/ui';
import { getProviders, hasDevApiKey, signIn, signInWithDevKey } from '@/lib/auth';
import { isConfigured } from '@/lib/config';
import { appleSignInAvailable, BrowserProvider, signInWithAppleNative, signInWithBrowser } from '@/lib/social-sign-in';
import { colors, fonts, radius, space } from '@/lib/theme';

export default function SignInScreen() {
  const providers = useQuery({ queryKey: ['auth-providers'], queryFn: getProviders, enabled: isConfigured, staleTime: 5 * 60_000 });
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [appleReady, setAppleReady] = useState(false);
  const passwordRef = useRef<TextInputHandle>(null);

  useEffect(() => {
    if (Platform.OS === 'ios') void appleSignInAvailable().then(setAppleReady);
  }, []);

  // Runs one sign-in attempt; a successful one flips the auth gate and leaves this screen
  async function attempt(key: string, action: () => Promise<unknown>) {
    setError(null);
    setBusy(key);
    try {
      await action();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(null);
    }
  }

  function submit() {
    if (!email.trim() || !password) return setError('Enter your email and password.');
    void attempt('password', () => signIn(email.trim(), password));
  }

  const browser = (provider: BrowserProvider) => attempt(provider, () => signInWithBrowser(provider));
  const p = providers.data;
  const showApple = Platform.OS === 'ios' && appleReady && p?.apple;
  const anyProvider = showApple || p?.google || p?.ebay || p?.sso.enabled;

  return (
    <AuthScreen edges={['top', 'bottom']}>
      <View style={styles.brand}>
        <BrandMark />
        <Text style={authStyles.title} accessibilityRole="header">
          Sign in
        </Text>
        <Text style={authStyles.subtitle}>Your collection, watchlists and deal alerts, on any device.</Text>
      </View>

      {!isConfigured && <ErrorBanner message="API not configured. Set EXPO_PUBLIC_API_URL in mobile/.env, then restart Expo." />}
      {providers.isError && (
        <ErrorBanner message={`Can't load sign-in options: ${providers.error.message}`} onRetry={() => void providers.refetch()} />
      )}

      {anyProvider && (
        <View style={styles.providers}>
          {showApple && (
            <AppleAuthentication.AppleAuthenticationButton
              buttonType={AppleAuthentication.AppleAuthenticationButtonType.CONTINUE}
              buttonStyle={AppleAuthentication.AppleAuthenticationButtonStyle.WHITE}
              cornerRadius={radius.lg}
              style={styles.appleButton}
              onPress={() => void attempt('apple', signInWithAppleNative)}
            />
          )}
          {p?.google && <ProviderButton title="Continue with Google" icon="logo-google" onPress={() => void browser('google')} disabled={!!busy} />}
          {p?.ebay && <ProviderButton title="Continue with eBay" icon="pricetags-outline" onPress={() => void browser('ebay')} disabled={!!busy} />}
          {p?.sso.enabled && (
            <ProviderButton
              title={p.sso.displayName && p.sso.displayName !== 'SSO' ? `Continue with ${p.sso.displayName}` : 'Sign in with SSO'}
              icon="business-outline"
              disabled={!!busy}
              onPress={() => (p.sso.requiresEmail ? router.push('/sso') : void browser('oidc'))}
            />
          )}
          <Divider label="or use email" />
        </View>
      )}

      <AuthInput
        label="Email"
        value={email}
        onChangeText={setEmail}
        placeholder="you@example.com"
        keyboardType="email-address"
        autoCapitalize="none"
        autoComplete="email"
        textContentType="username"
        returnKeyType="next"
        submitBehavior="submit"
        onSubmitEditing={() => passwordRef.current?.focus()}
      />
      <AuthInput
        ref={passwordRef}
        label="Password"
        value={password}
        onChangeText={setPassword}
        secure
        autoCapitalize="none"
        autoComplete="current-password"
        textContentType="password"
        returnKeyType="go"
        onSubmitEditing={submit}
      />
      <TextLink title="Forgot password?" align="left" onPress={() => router.push({ pathname: '/forgot-password', params: { email } })} />

      {error && <ErrorBanner message={error} />}
      <Button title="Sign in" icon="log-in-outline" onPress={submit} loading={busy === 'password'} disabled={!!busy && busy !== 'password'} />

      <View style={styles.footer}>
        <Text style={styles.footerText}>New to CardScout?</Text>
        <TextLink title="Create an account" onPress={() => router.push({ pathname: '/sign-up', params: { email } })} />
      </View>

      {hasDevApiKey && (
        <Button title="Use dev API key" variant="ghost" icon="key-outline" compact onPress={() => void signInWithDevKey()} />
      )}
    </AuthScreen>
  );
}

const styles = StyleSheet.create({
  brand: { gap: space.md, marginTop: space.xl, marginBottom: space.sm },
  providers: { gap: space.md },
  appleButton: { height: 52, width: '100%' },
  footer: { alignItems: 'center', marginTop: space.sm },
  footerText: { color: colors.textSecondary, fontFamily: fonts.body, fontSize: 14 },
});
