import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Text } from 'react-native';

import { AuthInput, AuthScreen, authStyles, BackToSignIn, Notice, TextLink } from '@/components/auth-ui';
import { Button, ErrorBanner } from '@/components/ui';
import { requestPasswordReset } from '@/lib/auth';

export default function ForgotPasswordScreen() {
  const params = useLocalSearchParams<{ email?: string }>();
  const [email, setEmail] = useState(params.email ?? '');
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if (!email.trim()) return setError('Enter the email you signed up with.');
    setError(null);
    setBusy(true);
    try {
      await requestPasswordReset(email.trim());
      setSent(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthScreen>
      <Text style={authStyles.subtitle}>
        Enter your account email and we’ll send a link to choose a new password. It expires in 30 minutes.
      </Text>
      <AuthInput
        label="Email"
        value={email}
        onChangeText={(v) => (setEmail(v), setSent(false))}
        placeholder="you@example.com"
        keyboardType="email-address"
        autoCapitalize="none"
        autoComplete="email"
        textContentType="username"
        returnKeyType="send"
        onSubmitEditing={() => void submit()}
      />
      {error && <ErrorBanner message={error} />}
      {sent && (
        <Notice icon="mail-outline">
          If an account uses that email, a reset link is on its way. Open it on this phone, or paste the code from the email.
        </Notice>
      )}
      <Button title={sent ? 'Send again' : 'Send reset link'} icon="paper-plane-outline" onPress={() => void submit()} loading={busy} variant={sent ? 'secondary' : 'primary'} />
      <TextLink title="I have a reset code" onPress={() => router.push('/reset-password')} />
      <BackToSignIn />
    </AuthScreen>
  );
}
