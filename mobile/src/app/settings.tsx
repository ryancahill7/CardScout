import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation, useQuery } from '@tanstack/react-query';
import * as Haptics from 'expo-haptics';
import { router, Stack } from 'expo-router';
import { useState } from 'react';
import { Alert, KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';

import { Button, ErrorBanner, Field, SectionTitle, Surface } from '@/components/ui';
import { sendTestPush } from '@/lib/api';
import { deleteAccount, fetchMe, signOut, useAuth } from '@/lib/auth';
import { registerForPush, unregisterPush } from '@/lib/notifications';
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

        <AccountSection />
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const providerNames: Record<string, string> = { apple: 'Apple', google: 'Google', ebay: 'eBay', oidc: 'SSO' };

// Who's signed in, sign out, and delete account (required by the App Store when sign-up exists)
function AccountSection() {
  const auth = useAuth();
  const me = useQuery({ queryKey: ['auth-me'], queryFn: fetchMe, enabled: auth.method === 'tokens' });
  const [busy, setBusy] = useState<'signout' | 'delete' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const user = me.data?.user ?? auth.user;

  async function doSignOut() {
    setBusy('signout');
    await unregisterPush();
    await signOut();
  }

  function confirmSignOut() {
    Alert.alert('Sign out?', 'You can sign back in any time. Deal alerts stop on this phone until you do.', [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Sign out', style: 'destructive', onPress: () => void doSignOut() },
    ]);
  }

  async function doDelete() {
    setError(null);
    setBusy('delete');
    try {
      await deleteAccount();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setBusy(null);
    }
  }

  function confirmDelete() {
    Alert.alert(
      'Delete your account?',
      'This permanently deletes your account, collection, card photos, watchlists, alerts and price history. It can’t be undone.',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Delete',
          style: 'destructive',
          onPress: () =>
            Alert.alert('Are you sure?', 'Everything is deleted right away.', [
              { text: 'Keep my account', style: 'cancel' },
              { text: 'Delete forever', style: 'destructive', onPress: () => void doDelete() },
            ]),
        },
      ],
    );
  }

  const methods = [user?.hasPassword ? 'Email & password' : null, ...(user?.providers ?? []).map((p) => providerNames[p] ?? p)];
  const via = methods.filter(Boolean).join(', ');

  return (
    <Surface>
      <SectionTitle>Account</SectionTitle>
      {auth.method === 'apikey' ? (
        <Text style={styles.hint}>Signed in with the dev API key from mobile/.env.</Text>
      ) : (
        <View style={{ gap: 2 }}>
          {user?.displayName && <Text style={styles.accountName}>{user.displayName}</Text>}
          <Text style={styles.hint}>{user?.email ?? 'No email on this account'}</Text>
          {!!via && <Text style={styles.hint}>Signs in with {via}</Text>}
        </View>
      )}
      <Button title="Sign out" icon="log-out-outline" variant="secondary" onPress={confirmSignOut} loading={busy === 'signout'} disabled={!!busy} />
      {auth.method === 'tokens' && (
        <Button title="Delete account" icon="trash-outline" variant="danger" onPress={confirmDelete} loading={busy === 'delete'} disabled={!!busy} />
      )}
      {error && <ErrorBanner message={error} />}
    </Surface>
  );
}

const styles = StyleSheet.create({
  accountName: { color: colors.text, fontFamily: fonts.bodySemiBold, fontSize: 16 },
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
