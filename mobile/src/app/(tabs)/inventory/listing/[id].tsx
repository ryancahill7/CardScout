import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as Clipboard from 'expo-clipboard';
import * as Haptics from 'expo-haptics';
import { useLocalSearchParams } from 'expo-router';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';

import { Button, ErrorBanner, Loading, SectionTitle, Surface } from '@/components/ui';
import { getListingKit, updateCard } from '@/lib/api';
import { formatMoney, formatSignedMoney } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';

// Everything to list the card on eBay: copy each field, open eBay's listing page, then
// mark the card Listed at the suggested price. (Posting straight to eBay needs the eBay
// Sell API with per-user OAuth, which isn't set up yet.)
export default function ListingKitScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const cardId = Number(id);
  const queryClient = useQueryClient();
  const [copied, setCopied] = useState<string | null>(null);

  const kit = useQuery({ queryKey: ['listing-kit', cardId], queryFn: () => getListingKit(cardId) });

  const markListed = useMutation({
    mutationFn: (price: number | null) => updateCard(cardId, { status: 'LISTED', listedPrice: price }),
    onSuccess: (card) => {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.setQueryData(['card', cardId], card);
      queryClient.invalidateQueries({ queryKey: ['cards'] });
    },
  });

  async function copy(key: string, text: string) {
    await Clipboard.setStringAsync(text);
    void Haptics.selectionAsync();
    setCopied(key);
    setTimeout(() => setCopied((c) => (c === key ? null : c)), 1500);
  }

  if (kit.isPending) return <Loading />;
  if (kit.isError) {
    return (
      <View style={{ padding: space.lg, flex: 1, backgroundColor: colors.background }}>
        <ErrorBanner message={kit.error.message} onRetry={() => kit.refetch()} />
      </View>
    );
  }

  const k = kit.data;
  const p = k.pricing;
  const specificsText = k.itemSpecifics.map((s) => `${s.name}: ${s.value}`).join('\n');

  return (
    <ScrollView style={{ backgroundColor: colors.background }} contentContainerStyle={styles.content}>
      <Surface>
        <SectionTitle>Price it</SectionTitle>
        <View style={styles.priceRow}>
          <View style={{ flex: 1 }}>
            <Text style={styles.small}>{p.format}</Text>
            <Text style={styles.bin}>{formatMoney(p.buyItNowPrice)}</Text>
          </View>
          {p.buyItNowPrice != null && (
            <CopyChip label="Copy" done={copied === 'price'} onPress={() => copy('price', p.buyItNowPrice!.toFixed(2))} />
          )}
        </View>
        <View style={styles.grid}>
          <Cell label="Market comp" value={formatMoney(p.marketPrice)} />
          <Cell label="Auto-accept ≥" value={formatMoney(p.autoAcceptOffer, { cents: false })} />
          <Cell label="Auto-decline <" value={formatMoney(p.autoDeclineOffer, { cents: false })} />
          <Cell label="Break-even" value={formatMoney(p.breakEvenPrice)} />
          <Cell label="Nets at BIN" value={formatMoney(p.netAtBuyItNow)} />
          <Cell
            label="Profit at BIN"
            value={p.profitAtBuyItNow == null ? '—' : formatSignedMoney(p.profitAtBuyItNow)}
            color={p.profitAtBuyItNow == null ? undefined : p.profitAtBuyItNow >= 0 ? colors.primary : colors.danger}
          />
        </View>
        <Text style={styles.body}>{p.strategy}</Text>
      </Surface>

      <Block title="Title" text={k.title} hint={`${k.title.length}/80`} copied={copied === 'title'} onCopy={() => copy('title', k.title)} />
      <Block title="Item specifics" text={specificsText} copied={copied === 'specifics'} onCopy={() => copy('specifics', specificsText)} />
      <Block title="Description" text={k.description} copied={copied === 'desc'} onCopy={() => copy('desc', k.description)} />
      <Surface>
        <SectionTitle>Category & condition</SectionTitle>
        <Text style={styles.body}>{k.category}</Text>
        <Text style={styles.body}>Condition: {k.condition}</Text>
      </Surface>

      <Button title="Open eBay to list" icon="open-outline" onPress={() => WebBrowser.openBrowserAsync(k.sellUrl)} />
      {markListed.isError && <ErrorBanner message={markListed.error.message} />}
      <Button
        title={markListed.isSuccess ? 'Marked as listed' : `Mark listed${p.buyItNowPrice ? ` at ${formatMoney(p.buyItNowPrice)}` : ''}`}
        icon={markListed.isSuccess ? 'checkmark-circle' : 'pricetag-outline'}
        variant="secondary"
        disabled={markListed.isSuccess}
        loading={markListed.isPending}
        onPress={() => markListed.mutate(p.buyItNowPrice)}
      />
    </ScrollView>
  );
}

function Block({ title, text, hint, copied, onCopy }: { title: string; text: string; hint?: string; copied: boolean; onCopy: () => void }) {
  return (
    <Surface>
      <SectionTitle right={<CopyChip label="Copy" done={copied} onPress={onCopy} />}>{title}</SectionTitle>
      <Text style={styles.body} selectable>
        {text}
      </Text>
      {hint && <Text style={styles.small}>{hint}</Text>}
    </Surface>
  );
}

function CopyChip({ label, done, onPress }: { label: string; done: boolean; onPress: () => void }) {
  return (
    <Pressable onPress={onPress} hitSlop={8} accessibilityRole="button" accessibilityLabel={label} style={({ pressed }) => [styles.copy, pressed && { opacity: 0.7 }]}>
      <Ionicons name={done ? 'checkmark' : 'copy-outline'} size={14} color={colors.primary} />
      <Text style={styles.copyText}>{done ? 'Copied' : label}</Text>
    </Pressable>
  );
}

function Cell({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <View style={styles.cell}>
      <Text style={styles.small}>{label}</Text>
      <Text style={[styles.cellValue, color && { color }]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { padding: space.lg, gap: space.lg, paddingBottom: 64 },
  priceRow: { flexDirection: 'row', alignItems: 'center' },
  bin: { fontSize: 42, fontFamily: fonts.display, color: colors.text, letterSpacing: -1 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', rowGap: space.md },
  cell: { width: '33.33%', gap: 2 },
  cellValue: { fontSize: 16, fontFamily: fonts.displayMedium, color: colors.text },
  body: { fontSize: 14, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 21 },
  small: { fontSize: 12, fontFamily: fonts.bodyMedium, color: colors.textMuted },
  copy: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 10,
    minHeight: 32,
    borderRadius: radius.pill,
    backgroundColor: colors.primarySoft,
  },
  copyText: { fontSize: 13, fontFamily: fonts.bodySemiBold, color: colors.primary },
});
