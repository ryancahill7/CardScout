import { Stack } from 'expo-router';

import { colors, fonts } from '@/lib/theme';

export const unstable_settings = { anchor: 'index' };

export default function DealsLayout() {
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.background },
        headerShadowVisible: false,
        headerTintColor: colors.primary,
        headerTitleStyle: { fontFamily: fonts.bodySemiBold, color: colors.text },
        contentStyle: { backgroundColor: colors.background },
      }}>
      <Stack.Screen name="index" options={{ headerShown: false, title: 'Deals' }} />
      <Stack.Screen name="watchlists" options={{ title: 'Watchlists' }} />
      <Stack.Screen name="watchlist/[id]" options={{ title: 'Watchlist' }} />
    </Stack>
  );
}
