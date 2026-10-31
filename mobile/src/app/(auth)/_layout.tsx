import { Stack } from 'expo-router';

import { colors, fonts } from '@/lib/theme';

export const unstable_settings = { initialRouteName: 'sign-in' };

// Signed-out stack. Every screen but sign-in has a header with a back button, plus a
// "Back to sign in" link for when it was opened from a deep link (password reset email).
export default function AuthLayout() {
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.background },
        headerShadowVisible: false,
        headerTintColor: colors.primary,
        headerTitleStyle: { fontFamily: fonts.bodySemiBold, color: colors.text },
        headerBackButtonDisplayMode: 'minimal',
        contentStyle: { backgroundColor: colors.background },
      }}>
      <Stack.Screen name="sign-in" options={{ headerShown: false }} />
      <Stack.Screen name="sign-up" options={{ title: 'Create account' }} />
      <Stack.Screen name="forgot-password" options={{ title: 'Reset password' }} />
      <Stack.Screen name="reset-password" options={{ title: 'New password' }} />
      <Stack.Screen name="sso" options={{ title: 'Single sign-on' }} />
    </Stack>
  );
}
