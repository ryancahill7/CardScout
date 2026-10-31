import Ionicons from '@expo/vector-icons/Ionicons';
import { ComponentProps, useMemo } from 'react';
import { StyleSheet, Text, TextInput, View } from 'react-native';
import Animated, { FadeIn } from 'react-native-reanimated';

import { FlipAnalysis } from '@/lib/api';
import { analyzeFlip, analyzeShowBuy, FlipTargets, verdictStyle } from '@/lib/flip';
import { ShowSettings } from '@/lib/settings';
import { formatMoney } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';
import { KEYBOARD_DONE_ID } from './ui';

type IconName = ComponentProps<typeof Ionicons>['name'];

const parse = (s: string) => {
  const n = Number(s.replace(/[$,]/g, '').trim());
  return s.trim() !== '' && Number.isFinite(n) && n >= 0 ? n : null;
};

// "Is this a good buy?" Type the seller's asking price and get a verdict, the most you
// should pay to hit your profit target, and an opening offer.
export function VerdictPanel({
  marketPrice,
  matchConfidence,
  asking,
  onAskingChange,
  shipping = 0,
  targets,
  compact,
  show,
}: {
  marketPrice: number | null | undefined;
  matchConfidence?: number | null;
  asking: string;
  onAskingChange: (value: string) => void;
  // What you pay to get the card to you (0 in person)
  shipping?: number;
  targets: FlipTargets;
  compact?: boolean;
  // Card-show mode: judge against a share of comps instead of an eBay flip target
  show?: ShowSettings;
}) {
  const askingPrice = parse(asking);
  const flip = useMemo(
    () =>
      askingPrice == null
        ? null
        : show
          ? analyzeShowBuy(marketPrice, askingPrice, show, matchConfidence)
          : analyzeFlip(marketPrice, askingPrice, shipping, targets, matchConfidence),
    [askingPrice, marketPrice, shipping, targets, matchConfidence, show],
  );
  // Before an asking price is typed, still show what to pay at most
  const ceiling = useMemo(
    () => (show ? analyzeShowBuy(marketPrice, 0, show) : analyzeFlip(marketPrice, 0, shipping, targets)).maxBuyPrice,
    [marketPrice, shipping, targets, show],
  );

  return (
    <View style={styles.wrap}>
      <View style={styles.inputRow}>
        <View style={{ flex: 1 }}>
          <Text style={styles.label}>Asking price</Text>
          <View style={styles.inputBox}>
            <Text style={styles.dollar}>$</Text>
            <TextInput
              value={asking}
              onChangeText={onAskingChange}
              keyboardType="decimal-pad"
              placeholder="0"
              placeholderTextColor={colors.textMuted}
              selectionColor={colors.primary}
              keyboardAppearance="dark"
              style={styles.input}
              accessibilityLabel="Asking price"
              returnKeyType="done"
              inputAccessoryViewID={KEYBOARD_DONE_ID}
            />
          </View>
        </View>
        <View style={styles.ceiling}>
          <Text style={styles.label}>{show ? `Pay up to (${show.buyPct}%)` : 'Pay up to'}</Text>
          <Text style={styles.ceilingValue}>{formatMoney(flip?.maxBuyPrice ?? ceiling)}</Text>
        </View>
      </View>

      {flip && <VerdictResult flip={flip} compact={compact} />}
    </View>
  );
}

export function VerdictResult({ flip, compact }: { flip: FlipAnalysis; compact?: boolean }) {
  const v = verdictStyle[flip.verdict];
  const profitColor = (flip.netProfit ?? 0) >= 0 ? colors.primary : colors.danger;
  return (
    <Animated.View entering={FadeIn.duration(200)} key={flip.verdict} style={{ gap: space.md }}>
      <View style={[styles.verdict, { backgroundColor: v.bg }]}>
        <Ionicons name={v.icon as IconName} size={26} color={v.fg} />
        <View style={{ flex: 1 }}>
          <Text style={[styles.verdictLabel, { color: v.fg }]}>{v.label}</Text>
          <Text style={[styles.verdictBlurb, { color: v.fg }]}>{v.blurb}</Text>
        </View>
        {flip.pctOfMarket != null && (
          <View style={{ alignItems: 'flex-end' }}>
            <Text style={[styles.pct, { color: v.fg }]}>{Math.round(flip.pctOfMarket)}%</Text>
            <Text style={[styles.pctLabel, { color: v.fg }]}>of market</Text>
          </View>
        )}
      </View>

      {flip.netProfit != null && (
        <View style={styles.stats}>
          <Stat label="Profit" value={formatMoney(flip.netProfit)} color={profitColor} />
          <Stat label="ROI" value={flip.netProfitPct == null ? '—' : `${Math.round(flip.netProfitPct)}%`} color={profitColor} />
          <Stat label="Open at" value={formatMoney(flip.openingOffer, { cents: (flip.openingOffer ?? 0) < 1 })} />
        </View>
      )}

      {!compact && (
        <View style={{ gap: 6 }}>
          {flip.reasons.map((r) => (
            <View key={r} style={styles.reason}>
              <View style={styles.bullet} />
              <Text style={styles.reasonText}>{r}</Text>
            </View>
          ))}
        </View>
      )}
    </Animated.View>
  );
}

function Stat({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <View style={styles.stat}>
      <Text style={styles.statLabel}>{label}</Text>
      <Text style={[styles.statValue, color && { color }]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: space.md },
  inputRow: { flexDirection: 'row', gap: space.md, alignItems: 'flex-end' },
  label: { fontSize: 12, fontFamily: fonts.bodySemiBold, color: colors.textSecondary, letterSpacing: 0.3, marginBottom: 6 },
  inputBox: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.borderStrong,
    borderRadius: radius.md,
    paddingHorizontal: 14,
    minHeight: 56,
  },
  dollar: { fontSize: 24, fontFamily: fonts.display, color: colors.textMuted, marginRight: 4 },
  input: { flex: 1, fontSize: 28, fontFamily: fonts.display, color: colors.text, paddingVertical: 8 },
  ceiling: {
    minWidth: 112,
    padding: space.md,
    borderRadius: radius.md,
    backgroundColor: colors.surfaceRaised,
    borderWidth: 1,
    borderColor: colors.border,
  },
  ceilingValue: { fontSize: 22, fontFamily: fonts.display, color: colors.primary },
  verdict: { flexDirection: 'row', alignItems: 'center', gap: space.md, padding: space.md, borderRadius: radius.lg },
  verdictLabel: { fontSize: 24, fontFamily: fonts.display, letterSpacing: -0.4 },
  verdictBlurb: { fontSize: 13, fontFamily: fonts.bodyMedium, opacity: 0.9 },
  pct: { fontSize: 22, fontFamily: fonts.display },
  pctLabel: { fontSize: 11, fontFamily: fonts.bodySemiBold, opacity: 0.85 },
  stats: { flexDirection: 'row', gap: space.sm },
  stat: {
    flex: 1,
    padding: space.md,
    borderRadius: radius.md,
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    gap: 2,
  },
  statLabel: { fontSize: 11, fontFamily: fonts.bodySemiBold, color: colors.textMuted },
  statValue: { fontSize: 18, fontFamily: fonts.displayMedium, color: colors.text },
  reason: { flexDirection: 'row', gap: space.sm, alignItems: 'flex-start' },
  bullet: { width: 5, height: 5, borderRadius: 3, backgroundColor: colors.textMuted, marginTop: 8 },
  reasonText: { flex: 1, fontSize: 14, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 20 },
});
