import { router, useLocalSearchParams } from 'expo-router';
import { useRef, useState } from 'react';
import { Text } from 'react-native';

import { AuthInput, TextInputHandle, AuthScreen, authStyles, BackToSignIn, Notice } from '@/components/auth-ui';
import { Button, ErrorBanner } from '@/components/ui';
import { resetPassword } from '@/lib/auth';

// Opened from the reset email (cardscout://reset-password?token=...) or from "I have a reset code"
export default function ResetPasswordScreen() {
  const params = useLocalSearchParams<{ token?: string }>();
  const [token, setToken] = useState(params.token ?? '');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const passwordRef = useRef<TextInputHandle>(null);

  async function submit() {
    if (!token.trim()) return setError('Paste the reset code from the email.');
    if (password.length < 10) return setError('Use at least 10 characters for your new password.');
    setError(null);
    setBusy(true);
    try {
      await resetPassword(token.trim(), password);
      setDone(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return (
      <AuthScreen>
        <Notice icon="checkmark-circle-outline">Password updated. You’ve been signed out everywhere; sign in with your new password.</Notice>
        <Button title="Sign in" icon="log-in-outline" onPress={() => router.replace('/sign-in')} />
      </AuthScreen>
    );
  }

  return (
    <AuthScreen>
      <Text style={authStyles.subtitle}>Choose a new password for your account.</Text>
      {!params.token && (
        <AuthInput
          label="Reset code"
          value={token}
          onChangeText={setToken}
          autoCapitalize="none"
          textContentType="oneTimeCode"
          returnKeyType="next"
          submitBehavior="submit"
          onSubmitEditing={() => passwordRef.current?.focus()}
        />
      )}
      <AuthInput
        ref={passwordRef}
        label="New password"
        hint="At least 10 characters."
        value={password}
        onChangeText={setPassword}
        secure
        autoCapitalize="none"
        autoComplete="new-password"
        textContentType="newPassword"
        passwordRules="minlength: 10;"
        returnKeyType="go"
        onSubmitEditing={() => void submit()}
      />
      {error && <ErrorBanner message={error} />}
      <Button title="Set new password" icon="key-outline" onPress={() => void submit()} loading={busy} />
      <BackToSignIn />
    </AuthScreen>
  );
}
