import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { ReactNode } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { HoloFrame } from '@/components/ui';
import { colors, fonts, radius, space } from '@/lib/theme';
import { useUsage } from '@/lib/usage';

// Right side of each tab's title row: an always-visible (but quiet) way to Pro, then Settings.
// Free users see a small holo "Pro" chip that opens the plans screen; Pro users see a static badge.
export function HeaderActions({ children }: { children?: ReactNode }) {
  return (
    <View style={styles.row}>
      <ProChip />
      {children}
      <Pressable
        onPress={() => router.push('/settings')}
        hitSlop={6}
        accessibilityRole="button"
        accessibilityLabel="Settings"
        style={({ pressed }) => [styles.iconButton, pressed && { opacity: 0.7 }]}>
        <Ionicons name="settings-outline" size={20} color={colors.text} />
      </Pressable>
    </View>
  );
}

export function HeaderIconButton({ icon, label, onPress }: { icon: React.ComponentProps<typeof Ionicons>['name']; label: string; onPress: () => void }) {
  return (
    <Pressable onPress={onPress} hitSlop={6} accessibilityRole="button" accessibilityLabel={label} style={({ pressed }) => [styles.iconButton, pressed && { opacity: 0.7 }]}>
      <Ionicons name={icon} size={20} color={colors.text} />
    </Pressable>
  );
}

function ProChip() {
  const { data: usage } = useUsage();
  if (usage?.plan === 'pro') {
    return (
      <View style={styles.proBadge} accessible accessibilityLabel="You're on Pro">
        <Text style={styles.proBadgeText}>PRO</Text>
      </View>
    );
  }
  return (
    <Pressable
      onPress={() => router.push('/upgrade')}
      hitSlop={6}
      accessibilityRole="button"
      accessibilityLabel="See CardScout Pro plans"
      style={({ pressed }) => pressed && { opacity: 0.75 }}>
      <HoloFrame borderRadius={radius.pill} width={1.5}>
        <View style={styles.chip}>
          <Ionicons name="sparkles" size={14} color={colors.text} />
          <Text style={styles.chipText}>Pro</Text>
        </View>
      </HoloFrame>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  iconButton: {
    width: 40,
    height: 40,
    borderRadius: radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surfaceRaised,
    borderWidth: 1,
    borderColor: colors.border,
  },
  chip: { flexDirection: 'row', alignItems: 'center', gap: 4, height: 37, paddingHorizontal: space.md },
  chipText: { color: colors.text, fontFamily: fonts.bodyBold, fontSize: 14 },
  proBadge: {
    paddingHorizontal: space.sm,
    paddingVertical: 4,
    borderRadius: radius.pill,
    backgroundColor: colors.primary,
  },
  proBadgeText: { color: colors.onPrimary, fontFamily: fonts.bodyBold, fontSize: 11, letterSpacing: 0.6 },
});
