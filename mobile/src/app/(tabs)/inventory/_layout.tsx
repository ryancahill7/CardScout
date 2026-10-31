import { Stack } from 'expo-router';

import { colors, fonts } from '@/lib/theme';

// The list is always the bottom of this stack, so opening a card from another tab
// (e.g. "View card" after a scan) still has a back arrow to the collection
export const unstable_settings = { anchor: 'index' };

export default function InventoryLayout() {
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.background },
        headerShadowVisible: false,
        headerTintColor: colors.primary,
        headerTitleStyle: { fontFamily: fonts.bodySemiBold, color: colors.text },
        contentStyle: { backgroundColor: colors.background },
      }}>
      <Stack.Screen name="index" options={{ headerShown: false, title: 'Collection' }} />
      <Stack.Screen name="[id]" options={{ title: '' }} />
      <Stack.Screen name="pnl" options={{ title: 'Profit & loss' }} />
      <Stack.Screen name="listing/[id]" options={{ title: 'List on eBay' }} />
    </Stack>
  );
}
