import { Inter_400Regular, Inter_500Medium, Inter_600SemiBold, Inter_700Bold } from '@expo-google-fonts/inter';
import { SpaceGrotesk_500Medium, SpaceGrotesk_700Bold, useFonts } from '@expo-google-fonts/space-grotesk';
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query';
import { DarkTheme, Stack, ThemeProvider } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useState } from 'react';
import { View } from 'react-native';

import { KeyboardDoneBar } from '@/components/ui';
import { loadSession, useAuth } from '@/lib/auth';
import { isConfigured } from '@/lib/config';
import { refreshPushRegistration, useNotificationRouting } from '@/lib/notifications';
import { colors, fonts } from '@/lib/theme';

void SplashScreen.preventAutoHideAsync();

const navTheme = {
  ...DarkTheme,
  colors: { ...DarkTheme.colors, background: colors.background, card: colors.background, primary: colors.primary },
};

// Root stack: the tab bar, plus screens that can open from any tab (Settings) as modals
// over it, so closing them always returns to wherever the user came from.
export default function RootLayout() {
  const [queryClient] = useState(
    () => new QueryClient({ defaultOptions: { queries: { retry: 1, staleTime: 30_000 } } }),
  );
  const [fontsLoaded, fontError] = useFonts({
    Inter_400Regular,
    Inter_500Medium,
    Inter_600SemiBold,
    Inter_700Bold,
    SpaceGrotesk_500Medium,
    SpaceGrotesk_700Bold,
  });
  const ready = fontsLoaded || !!fontError;

  useEffect(() => {
    if (ready) void SplashScreen.hideAsync();
  }, [ready]);

  // Dark placeholder while fonts load; returning null shows the white native root view in Expo Go
  if (!ready) return <View style={{ flex: 1, backgroundColor: colors.background }} />;

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider value={navTheme}>
        <StatusBar style="light" />
        <AppStack />
        <KeyboardDoneBar />
      </ThemeProvider>
    </QueryClientProvider>
  );
}

// Auth gate: signed-out users only see the (auth) screens; signed-in users only the app.
// Stack.Protected moves between them when the session changes (sign in, sign out, refresh failure).
function AppStack() {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const signedIn = auth.status === 'signedIn';
  const sessionKey = auth.method === 'apikey' ? 'apikey' : (auth.user?.id ?? null);

  useEffect(() => {
    void loadSession();
  }, []);

  // A different user (or none) must never see the previous user's cached data
  useEffect(() => {
    queryClient.clear();
  }, [sessionKey, queryClient]);

  useNotificationRouting();
  useEffect(() => {
    if (isConfigured && signedIn) void refreshPushRegistration();
  }, [signedIn, sessionKey]);

  if (auth.status === 'loading') return <View style={{ flex: 1, backgroundColor: colors.background }} />;

  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.background },
        headerShadowVisible: false,
        headerTintColor: colors.primary,
        headerTitleStyle: { fontFamily: fonts.bodySemiBold, color: colors.text },
        contentStyle: { backgroundColor: colors.background },
      }}>
      <Stack.Protected guard={signedIn}>
        <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
        <Stack.Screen name="settings" options={{ presentation: 'modal', title: 'Settings' }} />
      </Stack.Protected>
      <Stack.Protected guard={!signedIn}>
        <Stack.Screen name="(auth)" options={{ headerShown: false }} />
      </Stack.Protected>
      <Stack.Screen name="auth-callback" options={{ headerShown: false }} />
    </Stack>
  );
}
