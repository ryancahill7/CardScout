import { Stack } from 'expo-router';

import { colors, fonts } from '@/lib/theme';

export const unstable_settings = { anchor: 'index' };

export default function ShowLayout() {
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.background },
        headerShadowVisible: false,
        headerTintColor: colors.primary,
        headerTitleStyle: { fontFamily: fonts.bodySemiBold, color: colors.text },
        contentStyle: { backgroundColor: colors.background },
      }}>
      <Stack.Screen name="index" options={{ headerShown: false, title: 'Show' }} />
      <Stack.Screen name="lot" options={{ title: 'Lot analyzer' }} />
    </Stack>
  );
}
