import { useLocalSearchParams } from 'expo-router';
import { useRef, useState } from 'react';
import { Text } from 'react-native';

import { AuthInput, TextInputHandle, AuthScreen, authStyles, BackToSignIn } from '@/components/auth-ui';
import { Button, ErrorBanner } from '@/components/ui';
import { signUp } from '@/lib/auth';

// Mirrors the API's PasswordPolicy minimum; the API has the final say (common-password list etc.)
const MIN_PASSWORD = 10;

export default function SignUpScreen() {
  const params = useLocalSearchParams<{ email?: string }>();
  const [name, setName] = useState('');
  const [email, setEmail] = useState(params.email ?? '');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const emailRef = useRef<TextInputHandle>(null);
  const passwordRef = useRef<TextInputHandle>(null);

  async function submit() {
    if (!email.trim()) return setError('Enter your email address.');
    if (password.length < MIN_PASSWORD) return setError(`Use at least ${MIN_PASSWORD} characters for your password.`);
    setError(null);
    setBusy(true);
    try {
      await signUp(email.trim(), password, name.trim() || undefined);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthScreen>
      <Text style={authStyles.subtitle}>Track your collection, get deal alerts and price cards at shows.</Text>
      <AuthInput
        label="Name (optional)"
        value={name}
        onChangeText={setName}
        autoCapitalize="words"
        autoComplete="name"
        textContentType="name"
        returnKeyType="next"
        submitBehavior="submit"
        onSubmitEditing={() => emailRef.current?.focus()}
      />
      <AuthInput
        ref={emailRef}
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
        hint={`At least ${MIN_PASSWORD} characters. A short phrase works well.`}
        value={password}
        onChangeText={setPassword}
        secure
        autoCapitalize="none"
        autoComplete="new-password"
        textContentType="newPassword"
        passwordRules={`minlength: ${MIN_PASSWORD};`}
        returnKeyType="go"
        onSubmitEditing={() => void submit()}
      />
      {error && <ErrorBanner message={error} />}
      <Button title="Create account" icon="person-add-outline" onPress={() => void submit()} loading={busy} />
      <BackToSignIn />
    </AuthScreen>
  );
}
