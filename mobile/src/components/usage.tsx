import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { Usage } from '@/lib/api';
import { colors, fonts, radius, space } from '@/lib/theme';
import { formatResetDate, useUsage, WARN_AT_PERCENT } from '@/lib/usage';

const meterColor = (pct: number) => (pct >= 100 ? colors.danger : pct >= WARN_AT_PERCENT ? colors.warning : colors.primary);

// Bar showing how much of this month's AI allowance is used. Shown as a share, not dollars:
// users think in scans, and the dollar limit is our cost, not a price.
export function UsageMeter({ usage }: { usage: Usage }) {
  const pct = Math.min(100, Math.max(0, usage.percentUsed));
  const color = meterColor(usage.percentUsed);
  const label =
    usage.plan === 'pro'
      ? `Pro: ${usage.scansThisMonth} AI scans this month`
      : `${Math.floor(pct)}% of free AI scans used (${usage.scansThisMonth} this month)`;

  return (
    <View accessible accessibilityLabel={`${label}. Resets ${formatResetDate(usage.resetsAt)}.`}>
      <View style={styles.meterLabels}>
        <Text style={styles.meterText}>{label}</Text>
      </View>
      {usage.plan === 'free' && (
        <View style={styles.track}>
          <View style={[styles.fill, { width: `${pct}%`, backgroundColor: color }]} />
        </View>
      )}
      <Text style={styles.resets}>Resets {formatResetDate(usage.resetsAt)}</Text>
    </View>
  );
}

// Warning above the scanner once a free user passes 80% of the monthly allowance.
// At 100% it becomes the way back to the paywall (scans are blocked until reset or upgrade).
export function UsageBanner() {
  const { data: usage } = useUsage();
  if (!usage || usage.plan !== 'free' || usage.percentUsed < WARN_AT_PERCENT) return null;

  const out = usage.percentUsed >= 100;
  const color = out ? colors.danger : colors.warning;
  const text = out
    ? `You've used this month's free AI scans. They reset ${formatResetDate(usage.resetsAt)}.`
    : `You've used ${Math.floor(usage.percentUsed)}% of this month's free AI scans.`;

  return (
    <Pressable
      onPress={() => router.push({ pathname: '/upgrade', params: out ? { reason: 'limit', resetsAt: usage.resetsAt } : {} })}
      accessibilityRole="button"
      accessibilityLabel={`${text} See plans.`}
      style={({ pressed }) => [
        styles.banner,
        { borderColor: color, backgroundColor: out ? colors.dangerSoft : colors.warningSoft },
        pressed && { opacity: 0.8 },
      ]}>
      <Ionicons name={out ? 'lock-closed' : 'speedometer-outline'} size={18} color={color} />
      <Text style={styles.bannerText}>{text}</Text>
      <Text style={[styles.bannerAction, { color }]}>{out ? 'Upgrade' : 'Plans'}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  meterLabels: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: space.sm },
  meterText: { color: colors.text, fontFamily: fonts.bodySemiBold, fontSize: 14 },
  track: { height: 8, borderRadius: radius.pill, backgroundColor: colors.surfacePressed, overflow: 'hidden' },
  fill: { height: '100%', borderRadius: radius.pill },
  resets: { marginTop: space.sm, color: colors.textMuted, fontFamily: fonts.body, fontSize: 12 },
  banner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
    padding: space.md,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  bannerText: { flex: 1, color: colors.text, fontFamily: fonts.bodyMedium, fontSize: 13, lineHeight: 18 },
  bannerAction: { fontFamily: fonts.bodyBold, fontSize: 13 },
});
