import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation } from '@tanstack/react-query';
import * as Haptics from 'expo-haptics';
import { router, Stack } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';

import { Button, ErrorBanner, Field, SectionTitle, Surface } from '@/components/ui';
import { sendTestPush } from '@/lib/api';
import { openBillingPortal, openCheckout, useBillingStatus } from '@/lib/billing';
import { registerForPush } from '@/lib/notifications';
import { DEFAULT_SETTINGS, updateSettings, useSettings } from '@/lib/settings';
import { colors, fonts, radius, space } from '@/lib/theme';

const num = (s: string) => {
  const n = Number(s.replace(/[$,%]/g, '').trim());
  return s.trim() !== '' && Number.isFinite(n) && n >= 0 ? n : null;
};

// Opened as a modal from any tab. Done, Save and swipe-down all return to where you were.
function close() {
  if (router.canGoBack()) router.back();
  else router.replace('/');
}

export default function SettingsScreen() {
  const settings = useSettings();
  const [minProfit, setMinProfit] = useState(String(settings.targets.minProfit));
  const [minProfitPct, setMinProfitPct] = useState(String(settings.targets.minProfitPct));
  const [shipping, setShipping] = useState(String(settings.gradingShipping));
  const [buyPct, setBuyPct] = useState(String(settings.show.buyPct));
  const [resale, setResale] = useState(settings.show.resale);
  const [fees, setFees] = useState<Record<string, string>>(
    Object.fromEntries(settings.gradingServices.map((s) => [s.id, String(s.fee)])),
  );
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const push = useMutation({
    mutationFn: async () => {
      const result = await registerForPush();
      if (!result.ok) throw new Error(result.reason);
      return sendTestPush();
    },
  });

  function save() {
    const profit = num(minProfit);
    const pct = num(minProfitPct);
    const ship = num(shipping);
    const showPct = num(buyPct);
    if (profit == null || pct == null || ship == null || showPct == null)
      return setError('Enter numbers for the target, buy percentage and shipping.');
    if (showPct < 10 || showPct > 150) return setError('Buy percentage should be between 10 and 150.');
    const services = settings.gradingServices.map((s) => ({ ...s, fee: num(fees[s.id] ?? '') ?? s.fee }));
    setError(null);
    updateSettings({
      targets: { minProfit: profit, minProfitPct: pct },
      gradingShipping: ship,
      gradingServices: services,
      show: { buyPct: Math.round(showPct), resale },
    });
    void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    setSaved(true);
    close();
  }

  function reset() {
    updateSettings(DEFAULT_SETTINGS);
    setMinProfit(String(DEFAULT_SETTINGS.targets.minProfit));
    setMinProfitPct(String(DEFAULT_SETTINGS.targets.minProfitPct));
    setShipping(String(DEFAULT_SETTINGS.gradingShipping));
    setBuyPct(String(DEFAULT_SETTINGS.show.buyPct));
    setResale(DEFAULT_SETTINGS.show.resale);
    setFees(Object.fromEntries(DEFAULT_SETTINGS.gradingServices.map((s) => [s.id, String(s.fee)])));
  }

  return (
    <KeyboardAvoidingView style={{ flex: 1, backgroundColor: colors.background }} behavior={Platform.OS === 'ios' ? 'padding' : undefined} keyboardVerticalOffset={100}>
      <Stack.Screen
        options={{
          headerLeft: () => (
            <Pressable onPress={close} hitSlop={10} accessibilityRole="button" accessibilityLabel="Cancel">
              <Text style={styles.headerCancel}>Cancel</Text>
            </Pressable>
          ),
          headerRight: () => (
            <Pressable onPress={save} hitSlop={10} accessibilityRole="button" accessibilityLabel="Save settings">
              <Text style={styles.headerSave}>Save</Text>
            </Pressable>
          ),
        }}
      />
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" keyboardDismissMode="interactive">
        <SubscriptionSection />

        <Surface>
          <SectionTitle>Card shows</SectionTitle>
          <Text style={styles.hint}>
            Show mode calls a card a Buy when the asking price is at or under this share of its sold comps. Most cash buyers
            pay 70-85%.
          </Text>
          <Field label="Pay up to (% of comps)" value={buyPct} onChangeText={(v) => (setBuyPct(v), setSaved(false))} keyboardType="number-pad" />
          <Text style={styles.label}>I resell cards</Text>
          <View style={styles.segmented}>
            {(['cash', 'ebay'] as const).map((r) => (
              <Pressable
                key={r}
                onPress={() => (setResale(r), setSaved(false))}
                accessibilityRole="tab"
                accessibilityState={{ selected: resale === r }}
                style={[styles.segment, resale === r && styles.segmentSelected]}>
                <Text style={[styles.segmentText, resale === r && { color: colors.text }]}>
                  {r === 'cash' ? 'In person (no fees)' : 'On eBay (after fees)'}
                </Text>
              </Pressable>
            ))}
          </View>
        </Surface>

        <Surface>
          <SectionTitle>Profit target</SectionTitle>
          <Text style={styles.hint}>
            The Scan tab and the lot analyzer call a card a Buy when flipping it on eBay makes at least this much.
            Watchlists have their own targets.
          </Text>
          <View style={styles.row}>
            <View style={{ flex: 1 }}>
              <Field label="Profit ($)" value={minProfit} onChangeText={(v) => (setMinProfit(v), setSaved(false))} keyboardType="decimal-pad" />
            </View>
            <View style={{ flex: 1 }}>
              <Field label="Return (%)" value={minProfitPct} onChangeText={(v) => (setMinProfitPct(v), setSaved(false))} keyboardType="decimal-pad" />
            </View>
          </View>
        </Surface>

        <Surface>
          <SectionTitle>Grading fees</SectionTitle>
          <Text style={styles.hint}>Per card. Check the graders’ sites for current prices; these change often.</Text>
          {settings.gradingServices.map((s) => (
            <Field
              key={s.id}
              label={`${s.name} (${s.turnaround})`}
              value={fees[s.id] ?? ''}
              onChangeText={(v) => (setFees({ ...fees, [s.id]: v }), setSaved(false))}
              keyboardType="decimal-pad"
            />
          ))}
          <Field label="Shipping + insurance to grader, per card" value={shipping} onChangeText={(v) => (setShipping(v), setSaved(false))} keyboardType="decimal-pad" />
        </Surface>

        {error && <ErrorBanner message={error} />}
        <Button title={saved ? 'Saved' : 'Save settings'} icon={saved ? 'checkmark-circle' : 'checkmark'} onPress={save} />
        <Button title="Reset to defaults" variant="ghost" onPress={reset} />

        <Surface>
          <SectionTitle>Deal alerts</SectionTitle>
          <Text style={styles.hint}>
            Get a push notification when a watchlist finds a profitable listing or a card you own moves 15%+ in a week.
          </Text>
          <Button title="Turn on & send a test alert" icon="notifications-outline" variant="secondary" onPress={() => push.mutate()} loading={push.isPending} />
          {push.isError && <ErrorBanner message={push.error.message} />}
          {push.data && (
            <View style={styles.ok}>
              <Ionicons name="checkmark-circle" size={18} color={colors.primary} />
              <Text style={styles.okText}>
                {push.data.sent > 0 ? 'Alerts are on. A test notification is on its way.' : 'Registered, but Expo did not accept the test. Check the API log.'}
              </Text>
            </View>
          )}
        </Surface>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

// Current plan plus Upgrade (Stripe Checkout) or Manage (Stripe Billing Portal), both in the browser.
// The status refetches when the browser closes and whenever the app returns to the foreground.
function SubscriptionSection() {
  const status = useBillingStatus();
  const open = useMutation({
    mutationFn: (target: 'month' | 'year' | 'portal') => (target === 'portal' ? openBillingPortal() : openCheckout(target)),
    onSettled: () => void status.refetch(),
  });

  const s = status.data;
  const isPro = s?.plan === 'pro';
  const until = s?.currentPeriodEnd ? new Date(s.currentPeriodEnd).toLocaleDateString() : null;
  const detail = !s
    ? null
    : isPro
      ? s.cancelAtPeriodEnd
        ? `Cancels on ${until ?? 'the end of this period'}.`
        : until
          ? `Renews on ${until}.`
          : null
      : s.status === 'past_due' || s.status === 'unpaid'
        ? 'Your last payment failed. Update your card to get Pro back.'
        : s.billingEnabled
          ? 'Upgrade to Pro for more scans and alerts. Payment is handled by Stripe in your browser.'
          : 'Subscriptions are not available yet.';

  return (
    <Surface>
      <SectionTitle
        right={
          s && (
            <View style={[styles.planPill, isPro && styles.planPillPro]}>
              <Text style={[styles.planPillText, isPro && { color: colors.onPrimary }]}>{isPro ? 'PRO' : 'FREE'}</Text>
            </View>
          )
        }>
        Subscription
      </SectionTitle>
      {status.isPending && <Text style={styles.hint}>Checking your plan…</Text>}
      {status.isError && <ErrorBanner message={status.error.message} onRetry={() => void status.refetch()} />}
      {detail && <Text style={styles.hint}>{detail}</Text>}
      {s && (isPro || s.status != null) && (
        <Button
          title="Manage subscription"
          icon="card-outline"
          variant="secondary"
          onPress={() => open.mutate('portal')}
          loading={open.isPending && open.variables === 'portal'}
        />
      )}
      {s && !isPro && s.billingEnabled && (
        <>
          <Button
            title="Upgrade to Pro (monthly)"
            icon="sparkles-outline"
            onPress={() => open.mutate('month')}
            loading={open.isPending && open.variables === 'month'}
          />
          {s.yearlyAvailable && (
            <Button
              title="Upgrade yearly"
              variant="ghost"
              onPress={() => open.mutate('year')}
              loading={open.isPending && open.variables === 'year'}
            />
          )}
        </>
      )}
      {open.isError && <ErrorBanner message={open.error.message} />}
    </Surface>
  );
}

const styles = StyleSheet.create({
  planPill: {
    paddingHorizontal: space.sm,
    paddingVertical: 2,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: colors.borderStrong,
  },
  planPillPro: { backgroundColor: colors.primary, borderColor: colors.primary },
  planPillText: { fontSize: 11, fontFamily: fonts.bodyBold, color: colors.textSecondary, letterSpacing: 0.6 },
  content: { padding: space.lg, gap: space.lg, paddingBottom: 64 },
  row: { flexDirection: 'row', gap: space.md },
  label: { fontSize: 12, fontFamily: fonts.bodySemiBold, color: colors.textSecondary, letterSpacing: 0.3 },
  segmented: {
    flexDirection: 'row',
    borderRadius: radius.md,
    padding: 4,
    gap: 4,
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
  },
  segment: { flex: 1, minHeight: 40, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center' },
  segmentSelected: { backgroundColor: colors.surfacePressed },
  segmentText: { color: colors.textMuted, fontFamily: fonts.bodySemiBold, fontSize: 13 },
  hint: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 19 },
  headerCancel: { color: colors.textSecondary, fontFamily: fonts.bodyMedium, fontSize: 16 },
  headerSave: { color: colors.primary, fontFamily: fonts.bodyBold, fontSize: 16 },
  ok: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  okText: { flex: 1, color: colors.primary, fontFamily: fonts.bodyMedium, fontSize: 14 },
});
