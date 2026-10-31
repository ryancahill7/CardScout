import Ionicons from '@expo/vector-icons/Ionicons';
import { useQuery } from '@tanstack/react-query';
import { Tabs } from 'expo-router';
import { ComponentProps } from 'react';
import { StyleSheet, View } from 'react-native';

import { getHits } from '@/lib/api';
import { isConfigured } from '@/lib/config';
import { colors, fonts, radius } from '@/lib/theme';

type IconName = ComponentProps<typeof Ionicons>['name'];

function TabIcon({ name, focused }: { name: IconName; focused: boolean }) {
  return (
    <View style={[styles.tabIcon, focused && styles.tabIconActive]}>
      <Ionicons
        name={focused ? name : (`${name}-outline` as IconName)}
        size={22}
        color={focused ? colors.primary : colors.textMuted}
      />
    </View>
  );
}

// Tapping the tab you're already on pops its stack back to the top (React Navigation default)
export default function TabsLayout() {
  // Badge the Deals tab with profitable watchlist alerts the user hasn't acted on yet
  const hits = useQuery({
    queryKey: ['hits', 'alerts'],
    queryFn: () => getHits(),
    enabled: isConfigured,
    refetchInterval: 120_000,
  });
  const fresh = hits.data?.filter((h) => h.status === 'NEW').length ?? 0;

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.textMuted,
        tabBarStyle: styles.tabBar,
        tabBarLabelStyle: styles.tabLabel,
        sceneStyle: { backgroundColor: colors.background },
      }}>
      <Tabs.Screen
        name="index"
        options={{ title: 'Scan', tabBarIcon: ({ focused }) => <TabIcon name="scan-circle" focused={focused} /> }}
      />
      <Tabs.Screen
        name="show"
        options={{ title: 'Show', tabBarIcon: ({ focused }) => <TabIcon name="storefront" focused={focused} /> }}
      />
      <Tabs.Screen
        name="deals"
        options={{
          title: 'Deals',
          tabBarIcon: ({ focused }) => <TabIcon name="flash" focused={focused} />,
          tabBarBadge: fresh > 0 ? (fresh > 99 ? '99+' : fresh) : undefined,
          tabBarBadgeStyle: { backgroundColor: colors.primary, color: colors.onPrimary, fontFamily: fonts.bodyBold, fontSize: 11 },
        }}
      />
      <Tabs.Screen
        name="inventory"
        options={{ title: 'Collection', tabBarIcon: ({ focused }) => <TabIcon name="albums" focused={focused} /> }}
      />
    </Tabs>
  );
}

const styles = StyleSheet.create({
  tabBar: {
    backgroundColor: colors.background,
    borderTopColor: colors.border,
    borderTopWidth: StyleSheet.hairlineWidth,
    paddingTop: 6,
  },
  tabLabel: { fontFamily: fonts.bodySemiBold, fontSize: 11, marginTop: 4 },
  tabIcon: { width: 52, height: 30, borderRadius: radius.pill, alignItems: 'center', justifyContent: 'center' },
  tabIconActive: { backgroundColor: colors.primarySoft },
});
