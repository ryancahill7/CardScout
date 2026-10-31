import { useState } from 'react';
import { Text } from 'react-native';

import { AuthInput, AuthScreen, authStyles, BackToSignIn } from '@/components/auth-ui';
import { Button, ErrorBanner } from '@/components/ui';
import { signInWithBrowser } from '@/lib/social-sign-in';

// Company single sign-on: the API checks the email's domain and sends you to your company's login
export default function SsoScreen() {
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if (!email.includes('@')) return setError('Enter your work email.');
    setError(null);
    setBusy(true);
    try {
      await signInWithBrowser('oidc', email.trim());
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthScreen>
      <Text style={authStyles.subtitle}>Enter your work email to continue with your company’s sign-in.</Text>
      <AuthInput
        label="Work email"
        value={email}
        onChangeText={setEmail}
        placeholder="you@company.com"
        keyboardType="email-address"
        autoCapitalize="none"
        autoComplete="email"
        textContentType="username"
        returnKeyType="go"
        onSubmitEditing={() => void submit()}
      />
      {error && <ErrorBanner message={error} />}
      <Button title="Continue" icon="business-outline" onPress={() => void submit()} loading={busy} />
      <BackToSignIn />
    </AuthScreen>
  );
}
