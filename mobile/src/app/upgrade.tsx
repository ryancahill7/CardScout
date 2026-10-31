import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation } from '@tanstack/react-query';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';

import { Button, ErrorBanner, HoloFrame, Surface } from '@/components/ui';
import { UsageMeter } from '@/components/usage';
import { Plan } from '@/lib/api';
import { openCheckout } from '@/lib/billing';
import { colors, fonts, radius, space } from '@/lib/theme';
import { formatResetDate, usePlans, useUsage } from '@/lib/usage';

type Interval = 'month' | 'year';

// Paywall / plans, opened as a modal: automatically when the API returns 402, or from Settings
// and the usage banner. Close, "Not now" and swipe-down all return to where the user was.
function close() {
  if (router.canGoBack()) router.back();
  else router.replace('/');
}

const money = (n: number) => (Number.isInteger(n) ? `$${n}` : `$${n.toFixed(2)}`);

export default function UpgradeScreen() {
  const params = useLocalSearchParams<{ reason?: string; message?: string; resetsAt?: string }>();
  const plans = usePlans();
  const { data: usage } = useUsage();
  const [interval, setBillingInterval] = useState<Interval>('year');

  const free = plans.find((p) => p.id === 'free')!;
  const pro = plans.find((p) => p.id === 'pro')!;
  const isPro = usage?.plan === 'pro';
  const yearlySavings = Math.round((1 - pro.yearlyPriceUsd / (pro.monthlyPriceUsd * 12)) * 100);

  const checkout = useMutation({ mutationFn: () => openCheckout(interval) });

  const atLimit = params.reason === 'limit';
  const title = isPro
    ? "You're on Pro"
    : atLimit
      ? "You've hit your free limit"
      : params.reason === 'plan'
        ? 'That needs Pro'
        : 'Flip more with Pro';
  const subtitle = isPro
    ? atLimit
      ? `You've reached this month's fair-use limit. It resets ${formatResetDate(params.resetsAt || usage?.resetsAt)}.`
      : 'Thanks for supporting CardScout.'
    : atLimit
      ? `Free AI scans reset ${formatResetDate(params.resetsAt || usage?.resetsAt)}. Pricing by card details, deals and watchlists keep working.`
      : params.message || 'Scan every card at the show, analyze whole lots, and run more watchlists.';

  return (
    <View style={{ flex: 1, backgroundColor: colors.background }}>
      <Stack.Screen
        options={{
          title: '',
          headerLeft: () => (
            <Pressable onPress={close} hitSlop={12} accessibilityRole="button" accessibilityLabel="Close">
              <Ionicons name="close" size={26} color={colors.textSecondary} />
            </Pressable>
          ),
        }}
      />
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.hero}>
          <View style={[styles.heroIcon, atLimit && !isPro && { backgroundColor: colors.warningSoft }]}>
            <Ionicons
              name={atLimit && !isPro ? 'lock-closed' : 'sparkles'}
              size={28}
              color={atLimit && !isPro ? colors.warning : colors.primary}
            />
          </View>
          <Text style={styles.title} accessibilityRole="header">
            {title}
          </Text>
          <Text style={styles.subtitle}>{subtitle}</Text>
        </View>

        {usage && (
          <Surface>
            <UsageMeter usage={usage} />
          </Surface>
        )}

        {!isPro && (
          <>
            <View style={styles.segmented} accessibilityRole="tablist">
              {(['month', 'year'] as const).map((i) => (
                <Pressable
                  key={i}
                  onPress={() => setBillingInterval(i)}
                  accessibilityRole="tab"
                  accessibilityState={{ selected: interval === i }}
                  style={[styles.segment, interval === i && styles.segmentSelected]}>
                  <Text style={[styles.segmentText, interval === i && { color: colors.text }]}>
                    {i === 'month' ? 'Monthly' : 'Yearly'}
                  </Text>
                  {i === 'year' && yearlySavings > 0 && <Text style={styles.save}>Save {yearlySavings}%</Text>}
                </Pressable>
              ))}
            </View>

            <View style={styles.columns}>
              <PlanColumn plan={free} interval={interval} current={!isPro} />
              <HoloFrame style={{ flex: 1 }} borderRadius={radius.lg}>
                <PlanColumn plan={pro} interval={interval} highlighted />
              </HoloFrame>
            </View>

            {checkout.isError && <ErrorBanner message={checkout.error.message} />}
            <Button
              title={`Upgrade to Pro · ${interval === 'month' ? `${money(pro.monthlyPriceUsd)}/mo` : `${money(pro.yearlyPriceUsd)}/yr`}`}
              icon="sparkles"
              loading={checkout.isPending}
              onPress={() => checkout.mutate()}
            />
            <Text style={styles.fine}>Cancel anytime. Billed through Stripe; you&apos;ll finish checkout in your browser.</Text>
          </>
        )}

        <Button title={isPro ? 'Done' : 'Not now'} variant="ghost" onPress={close} />
      </ScrollView>
    </View>
  );
}

function PlanColumn({ plan, interval, highlighted, current }: { plan: Plan; interval: Interval; highlighted?: boolean; current?: boolean }) {
  const price = plan.monthlyPriceUsd === 0 ? '$0' : interval === 'month' ? money(plan.monthlyPriceUsd) : money(plan.yearlyPriceUsd);
  const per = plan.monthlyPriceUsd === 0 ? 'forever' : interval === 'month' ? 'per month' : 'per year';
  const perMonth = plan.monthlyPriceUsd > 0 && interval === 'year' ? `${money(Math.floor((plan.yearlyPriceUsd / 12) * 100) / 100)}/mo` : null;

  return (
    <View
      style={[styles.column, !highlighted && styles.columnPlain]}
      accessible
      accessibilityLabel={`${plan.name}: ${price} ${per}. ${plan.features.join('. ')}`}>
      <View style={styles.columnHeader}>
        <Text style={[styles.planName, highlighted && { color: colors.primary }]}>{plan.name}</Text>
        {current && <Text style={styles.currentPill}>Current</Text>}
      </View>
      <Text style={styles.price}>{price}</Text>
      <Text style={styles.per}>{perMonth ? `${per} · ${perMonth}` : per}</Text>
      <View style={styles.features}>
        {plan.features.map((f) => (
          <View key={f} style={styles.feature}>
            <Ionicons name="checkmark" size={14} color={highlighted ? colors.primary : colors.textMuted} style={{ marginTop: 2 }} />
            <Text style={[styles.featureText, highlighted && { color: colors.text }]}>{f}</Text>
          </View>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { padding: space.lg, gap: space.lg, paddingBottom: 64 },
  hero: { alignItems: 'center', gap: space.sm, paddingTop: space.sm },
  heroIcon: {
    width: 56,
    height: 56,
    borderRadius: radius.pill,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: space.xs,
  },
  title: { color: colors.text, fontFamily: fonts.display, fontSize: 26, textAlign: 'center' },
  subtitle: { color: colors.textSecondary, fontFamily: fonts.body, fontSize: 14, lineHeight: 20, textAlign: 'center' },
  segmented: {
    flexDirection: 'row',
    borderRadius: radius.md,
    padding: 4,
    gap: 4,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
  segment: { flex: 1, minHeight: 44, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center' },
  segmentSelected: { backgroundColor: colors.surfacePressed },
  segmentText: { color: colors.textMuted, fontFamily: fonts.bodySemiBold, fontSize: 14 },
  save: { color: colors.primary, fontFamily: fonts.bodySemiBold, fontSize: 11, marginTop: 1 },
  columns: { flexDirection: 'row', gap: space.sm, alignItems: 'stretch' },
  column: { flex: 1, padding: space.md, gap: 2, backgroundColor: colors.surface },
  columnPlain: { borderRadius: radius.lg, borderWidth: 1, borderColor: colors.border },
  columnHeader: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: space.xs },
  planName: { color: colors.text, fontFamily: fonts.bodyBold, fontSize: 16 },
  currentPill: {
    color: colors.textSecondary,
    fontFamily: fonts.bodySemiBold,
    fontSize: 10,
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: radius.pill,
    backgroundColor: colors.surfacePressed,
    overflow: 'hidden',
  },
  price: { color: colors.text, fontFamily: fonts.display, fontSize: 26, marginTop: space.sm },
  per: { color: colors.textMuted, fontFamily: fonts.body, fontSize: 12 },
  features: { gap: space.sm, marginTop: space.md },
  feature: { flexDirection: 'row', gap: 6 },
  featureText: { flex: 1, color: colors.textSecondary, fontFamily: fonts.body, fontSize: 12, lineHeight: 17 },
  fine: { color: colors.textMuted, fontFamily: fonts.body, fontSize: 12, textAlign: 'center', marginTop: -space.sm },
});
