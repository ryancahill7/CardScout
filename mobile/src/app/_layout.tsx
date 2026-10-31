import { Inter_400Regular, Inter_500Medium, Inter_600SemiBold, Inter_700Bold } from '@expo-google-fonts/inter';
import { SpaceGrotesk_500Medium, SpaceGrotesk_700Bold, useFonts } from '@expo-google-fonts/space-grotesk';
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query';
import { DarkTheme, router, Stack, ThemeProvider, usePathname } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useRef, useState } from 'react';
import { View } from 'react-native';

import { KeyboardDoneBar } from '@/components/ui';
import { setApiListeners } from '@/lib/api';
import { isConfigured } from '@/lib/config';
import { refreshPushRegistration, useNotificationRouting } from '@/lib/notifications';
import { colors, fonts } from '@/lib/theme';
import { paywallParams, USAGE_QUERY_KEY } from '@/lib/usage';

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

function AppStack() {
  useNotificationRouting();
  useEffect(() => {
    if (isConfigured) void refreshPushRegistration();
  }, []);
  useUsageLimitRouting();

  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.background },
        headerShadowVisible: false,
        headerTintColor: colors.primary,
        headerTitleStyle: { fontFamily: fonts.bodySemiBold, color: colors.text },
        contentStyle: { backgroundColor: colors.background },
      }}>
      <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
      <Stack.Screen name="settings" options={{ presentation: 'modal', title: 'Settings' }} />
      <Stack.Screen name="upgrade" options={{ presentation: 'modal', title: 'Plans', gestureEnabled: true }} />
    </Stack>
  );
}

// Any API call that comes back 402 (USAGE_LIMIT / PLAN_LIMIT) opens the paywall, once,
// and every AI call refreshes the usage meter and banner
function useUsageLimitRouting() {
  const queryClient = useQueryClient();
  const pathname = usePathname();
  const pathRef = useRef(pathname);
  useEffect(() => {
    pathRef.current = pathname;
  }, [pathname]);

  useEffect(() => {
    let lastOpened = 0;
    setApiListeners({
      onAiCall: () => void queryClient.invalidateQueries({ queryKey: USAGE_QUERY_KEY }),
      onLimitReached: (info) => {
        void queryClient.invalidateQueries({ queryKey: USAGE_QUERY_KEY });
        // Don't stack paywalls when several requests fail together or it's already open
        if (pathRef.current === '/upgrade' || Date.now() - lastOpened < 3000) return;
        lastOpened = Date.now();
        router.push({ pathname: '/upgrade', params: paywallParams(info) });
      },
    });
    return () => setApiListeners({});
  }, [queryClient]);
}
