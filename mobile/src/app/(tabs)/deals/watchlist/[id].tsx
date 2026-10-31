import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as Haptics from 'expo-haptics';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert, KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Switch, Text, View } from 'react-native';

import { Button, ErrorBanner, Field, Loading, SectionTitle, Surface } from '@/components/ui';
import { createWatchlist, deleteWatchlist, getWatchlists, updateWatchlist, WatchlistInput, WatchlistListingType } from '@/lib/api';
import { colors, fonts, radius, space } from '@/lib/theme';

const TYPES: { key: WatchlistListingType; label: string }[] = [
  { key: 'ANY', label: 'Both' },
  { key: 'FIXED_PRICE', label: 'Buy It Now' },
  { key: 'AUCTION', label: 'Auctions' },
];

interface FormValues {
  name: string;
  query: string;
  listingType: WatchlistListingType;
  maxPrice: string;
  minProfit: string;
  minProfitPct: string;
  gradedOnly: boolean;
  notify: boolean;
  isActive: boolean;
}

const EMPTY: FormValues = {
  name: '',
  query: '',
  listingType: 'ANY',
  maxPrice: '',
  minProfit: '5',
  minProfitPct: '20',
  gradedOnly: false,
  notify: true,
  isActive: true,
};

export default function WatchlistEditScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const isNew = id === 'new';
  const listId = Number(id);
  const queryClient = useQueryClient();

  const lists = useQuery({ queryKey: ['watchlists'], queryFn: getWatchlists, enabled: !isNew });
  const existing = lists.data?.find((w) => w.id === listId);

  const [form, setForm] = useState<FormValues | null>(isNew ? EMPTY : null);
  const [error, setError] = useState<string | null>(null);

  if (!form && existing) {
    setForm({
      name: existing.name,
      query: existing.query,
      listingType: existing.listingType,
      maxPrice: existing.maxPrice == null ? '' : String(existing.maxPrice),
      minProfit: String(existing.minProfit),
      minProfitPct: String(existing.minProfitPct),
      gradedOnly: existing.gradedOnly,
      notify: existing.notify,
      isActive: existing.isActive,
    });
  }

  const save = useMutation({
    mutationFn: (input: WatchlistInput) => (isNew ? createWatchlist(input) : updateWatchlist(listId, input)),
    onSuccess: () => {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.invalidateQueries({ queryKey: ['watchlists'] });
      router.back();
    },
  });

  const remove = useMutation({
    mutationFn: () => deleteWatchlist(listId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['watchlists'] });
      queryClient.invalidateQueries({ queryKey: ['hits'] });
      router.back();
    },
  });

  if (!form) return lists.isError ? <ErrorBanner message={lists.error.message} /> : <Loading />;

  const set = <K extends keyof FormValues>(key: K) => (value: FormValues[K]) => setForm({ ...form, [key]: value });

  function submit() {
    if (!form) return;
    const num = (s: string) => (s.trim() === '' ? null : Number(s.replace(/[$,%]/g, '')));
    const maxPrice = num(form.maxPrice);
    const minProfit = num(form.minProfit) ?? 0;
    const minProfitPct = num(form.minProfitPct) ?? 0;
    if (form.query.trim().length < 3) return setError('Enter at least 3 characters to search for.');
    if ([maxPrice, minProfit, minProfitPct].some((n) => n != null && !Number.isFinite(n)))
      return setError('Prices and percentages must be numbers.');
    setError(null);
    save.mutate({
      name: form.name.trim() || form.query.trim(),
      query: form.query.trim(),
      listingType: form.listingType,
      maxPrice,
      minProfit,
      minProfitPct,
      gradedOnly: form.gradedOnly,
      notify: form.notify,
      isActive: form.isActive,
    });
  }

  return (
    <KeyboardAvoidingView style={{ flex: 1, backgroundColor: colors.background }} behavior={Platform.OS === 'ios' ? 'padding' : undefined} keyboardVerticalOffset={100}>
      <Stack.Screen options={{ title: isNew ? 'New watchlist' : 'Edit watchlist' }} />
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" keyboardDismissMode="interactive">
        <Surface>
          <SectionTitle>Search eBay for</SectionTitle>
          <Field
            label="Keywords"
            value={form.query}
            onChangeText={set('query')}
            placeholder="e.g. Paul Skenes Bowman Chrome PSA 10"
            autoCapitalize="words"
            autoCorrect={false}
          />
          <Field label="Name (optional)" value={form.name} onChangeText={set('name')} placeholder="Shown on alerts" />
          <Text style={styles.label}>Listing type</Text>
          <View style={styles.segmented}>
            {TYPES.map((t) => (
              <Pressable
                key={t.key}
                onPress={() => set('listingType')(t.key)}
                accessibilityRole="tab"
                accessibilityState={{ selected: form.listingType === t.key }}
                style={[styles.segment, form.listingType === t.key && styles.segmentSelected]}>
                <Text style={[styles.segmentText, form.listingType === t.key && { color: colors.text }]}>{t.label}</Text>
              </Pressable>
            ))}
          </View>
          <Text style={styles.hint}>Auctions are only checked in their last 24 hours, when the bid is close to final.</Text>
          <Field label="Max price" value={form.maxPrice} onChangeText={set('maxPrice')} keyboardType="decimal-pad" placeholder="No limit" />
        </Surface>

        <Surface>
          <SectionTitle>Alert me when a flip makes at least</SectionTitle>
          <View style={{ flexDirection: 'row', gap: space.md }}>
            <View style={{ flex: 1 }}>
              <Field label="Profit ($)" value={form.minProfit} onChangeText={set('minProfit')} keyboardType="decimal-pad" />
            </View>
            <View style={{ flex: 1 }}>
              <Field label="Return (%)" value={form.minProfitPct} onChangeText={set('minProfitPct')} keyboardType="decimal-pad" />
            </View>
          </View>
          <Text style={styles.hint}>
            After eBay’s 13.25% fee, the $0.40 order fee, $0.75 packaging, and the shipping you pay the seller.
          </Text>
          <Toggle label="Graded cards only" value={form.gradedOnly} onChange={set('gradedOnly')} />
          <Toggle label="Push notifications" value={form.notify} onChange={set('notify')} />
          <Toggle label="Active" value={form.isActive} onChange={set('isActive')} />
        </Surface>

        {error && <ErrorBanner message={error} />}
        {save.isError && <ErrorBanner message={save.error.message} />}
        {remove.isError && <ErrorBanner message={remove.error.message} />}
        <Button title={isNew ? 'Create watchlist' : 'Save'} icon="checkmark" onPress={submit} loading={save.isPending} />
        {!isNew && (
          <Button
            title="Delete watchlist"
            variant="danger"
            loading={remove.isPending}
            onPress={() =>
              Alert.alert('Delete watchlist?', 'Its alerts are deleted too.', [
                { text: 'Cancel', style: 'cancel' },
                { text: 'Delete', style: 'destructive', onPress: () => remove.mutate() },
              ])
            }
          />
        )}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

function Toggle({ label, value, onChange }: { label: string; value: boolean; onChange: (v: boolean) => void }) {
  return (
    <View style={styles.toggle}>
      <Text style={styles.toggleText}>{label}</Text>
      <Switch value={value} onValueChange={onChange} trackColor={{ true: colors.primary, false: colors.borderStrong }} accessibilityLabel={label} />
    </View>
  );
}

const styles = StyleSheet.create({
  content: { padding: space.lg, gap: space.lg, paddingBottom: 64 },
  label: { fontSize: 12, fontFamily: fonts.bodySemiBold, color: colors.textSecondary, letterSpacing: 0.3 },
  hint: { fontSize: 12, fontFamily: fonts.body, color: colors.textMuted, lineHeight: 17 },
  segmented: {
    flexDirection: 'row',
    borderRadius: radius.md,
    padding: 4,
    gap: 4,
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
  },
  segment: { flex: 1, minHeight: 38, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center' },
  segmentSelected: { backgroundColor: colors.surfacePressed },
  segmentText: { color: colors.textMuted, fontFamily: fonts.bodySemiBold, fontSize: 14 },
  toggle: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', minHeight: 44 },
  toggleText: { fontSize: 15, fontFamily: fonts.bodyMedium, color: colors.text },
});
