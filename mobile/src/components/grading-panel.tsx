import Ionicons from '@expo/vector-icons/Ionicons';
import * as Haptics from 'expo-haptics';
import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { GradeOutlook, PriceTier } from '@/lib/api';
import { analyzeGrading, GRADE_PRESETS, GradeMix, mixFromOutlook } from '@/lib/flip';
import { formatMoney, formatSignedMoney } from '@/lib/money';
import { useSettings } from '@/lib/settings';
import { colors, fonts, radius, space } from '@/lib/theme';
import { SectionTitle, Surface } from './ui';

const pct = (n: number) => `${Math.round(n * 100)}%`;

// "Should I grade this?" Expected value of slabbing a raw card at each grading service,
// from the comp ladder and a grade forecast (the AI's read of the photos, or a preset).
export function GradingPanel({ tiers, outlook }: { tiers: PriceTier[]; outlook?: GradeOutlook | null }) {
  const settings = useSettings();
  const [presetKey, setPresetKey] = useState<string>(outlook ? 'ai' : 'clean');

  const mix: GradeMix = useMemo(() => {
    if (presetKey === 'ai' && outlook) return mixFromOutlook(outlook);
    return (GRADE_PRESETS.find((p) => p.key === presetKey) ?? GRADE_PRESETS[1]).mix;
  }, [presetKey, outlook]);

  const analysis = useMemo(
    () => analyzeGrading(tiers, mix, settings.gradingServices, settings.gradingShipping),
    [tiers, mix, settings.gradingServices, settings.gradingShipping],
  );

  const hasGradedComps = tiers.some((t) => t.key !== 'raw' && t.price != null);
  if (!hasGradedComps) return null;

  const best = [...analysis.results].sort((a, b) => b.gainVsRaw - a.gainVsRaw)[0];
  const presets = [...(outlook ? [{ key: 'ai', label: 'AI read' }] : []), ...GRADE_PRESETS];

  return (
    <Surface>
      <SectionTitle>Grade it?</SectionTitle>

      {best && (
        <View style={[styles.hero, { backgroundColor: best.worthIt ? colors.primarySoft : colors.surfaceRaised }]}>
          <Ionicons
            name={best.worthIt ? 'ribbon' : 'hand-left-outline'}
            size={22}
            color={best.worthIt ? colors.primary : colors.textSecondary}
          />
          <Text style={[styles.heroText, { color: best.worthIt ? colors.primary : colors.text }]}>
            {best.worthIt
              ? `Grade with ${best.service.name}: about ${formatSignedMoney(best.gainVsRaw)} more than selling raw`
              : 'Sell it raw. Grading costs more than it adds at these odds.'}
          </Text>
        </View>
      )}

      <View style={styles.chips}>
        {presets.map((p) => {
          const selected = p.key === presetKey;
          return (
            <Pressable
              key={p.key}
              onPress={() => {
                void Haptics.selectionAsync();
                setPresetKey(p.key);
              }}
              accessibilityRole="button"
              accessibilityState={{ selected }}
              style={[styles.chip, selected && styles.chipSelected]}>
              {p.key === 'ai' && <Ionicons name="sparkles" size={12} color={selected ? colors.primary : colors.textSecondary} />}
              <Text style={[styles.chipText, selected && { color: colors.primary }]}>{p.label}</Text>
            </Pressable>
          );
        })}
      </View>

      <View style={styles.mixRow}>
        <MixCell label="10" value={mix.gem10} />
        <MixCell label="9" value={mix.mint9} />
        <MixCell label="8" value={mix.nearMint8} />
        <MixCell label="≤7" value={mix.lower} />
      </View>
      {presetKey === 'ai' && outlook?.summary && <Text style={styles.muted}>{outlook.summary}</Text>}

      <View style={{ gap: 2 }}>
        <View style={styles.tableHead}>
          <Text style={[styles.th, { flex: 1 }]}>Service</Text>
          <Text style={[styles.th, styles.num]}>Cost</Text>
          <Text style={[styles.th, styles.num]}>Exp. net</Text>
          <Text style={[styles.th, styles.num]}>vs raw</Text>
        </View>
        {analysis.results.map((r) => (
          <View key={r.service.id} style={styles.tr}>
            <View style={{ flex: 1 }}>
              <Text style={styles.service}>{r.service.name}</Text>
              <Text style={styles.small}>{r.service.turnaround}</Text>
            </View>
            <Text style={[styles.td, styles.num]}>{formatMoney(r.totalCost)}</Text>
            <Text style={[styles.td, styles.num]}>{formatMoney(r.expectedNet)}</Text>
            <Text style={[styles.td, styles.num, { color: r.gainVsRaw >= 0 ? colors.primary : colors.danger }]}>
              {formatSignedMoney(r.gainVsRaw)}
            </Text>
          </View>
        ))}
      </View>
      <View style={styles.footRow}>
        <Text style={[styles.footnote, { flex: 1 }]}>
          Raw nets {formatMoney(analysis.rawNet)} today. Costs include {formatMoney(settings.gradingShipping)} shipping per card.
        </Text>
        <Pressable onPress={() => router.push('/settings')} hitSlop={8} accessibilityRole="button" accessibilityLabel="Edit grading fees">
          <Text style={styles.link}>Edit fees</Text>
        </Pressable>
      </View>
    </Surface>
  );
}

function MixCell({ label, value }: { label: string; value: number }) {
  return (
    <View style={styles.mixCell}>
      <View style={styles.mixBarTrack}>
        <View style={[styles.mixBar, { height: `${Math.max(4, value * 100)}%` }]} />
      </View>
      <Text style={styles.mixPct}>{pct(value)}</Text>
      <Text style={styles.small}>PSA {label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  hero: { flexDirection: 'row', alignItems: 'center', gap: space.md, padding: space.md, borderRadius: radius.md },
  heroText: { flex: 1, fontSize: 15, fontFamily: fonts.bodySemiBold, lineHeight: 21 },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    minHeight: 36,
    paddingHorizontal: 12,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: colors.border,
  },
  chipSelected: { borderColor: colors.primary, backgroundColor: colors.primarySoft },
  chipText: { fontSize: 13, fontFamily: fonts.bodySemiBold, color: colors.textSecondary },
  mixRow: { flexDirection: 'row', gap: space.sm },
  mixCell: { flex: 1, alignItems: 'center', gap: 2 },
  mixBarTrack: {
    width: '100%',
    height: 44,
    borderRadius: radius.sm,
    backgroundColor: colors.background,
    justifyContent: 'flex-end',
    overflow: 'hidden',
  },
  mixBar: { width: '100%', backgroundColor: colors.primary, opacity: 0.75 },
  mixPct: { fontSize: 14, fontFamily: fonts.displayMedium, color: colors.text },
  tableHead: { flexDirection: 'row', paddingBottom: 4 },
  th: { fontSize: 11, fontFamily: fonts.bodySemiBold, color: colors.textMuted },
  tr: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: 46,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.border,
  },
  num: { width: 72, textAlign: 'right' },
  td: { fontSize: 14, fontFamily: fonts.displayMedium, color: colors.text },
  service: { fontSize: 14, fontFamily: fonts.bodySemiBold, color: colors.text },
  small: { fontSize: 11, fontFamily: fonts.body, color: colors.textMuted },
  muted: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 18 },
  footRow: { flexDirection: 'row', alignItems: 'center', gap: space.md },
  link: { fontSize: 13, fontFamily: fonts.bodySemiBold, color: colors.primary },
  footnote: { fontSize: 11, fontFamily: fonts.body, color: colors.textMuted, lineHeight: 16 },
});
