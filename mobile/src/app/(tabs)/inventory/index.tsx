import Ionicons from '@expo/vector-icons/Ionicons';
import { useQuery } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { router } from 'expo-router';
import { memo, useMemo, useState } from 'react';
import { FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { ErrorBanner, Loading, ScreenTitle, StatusPicker, StatusPill } from '@/components/ui';
import { Card, CardStatus, getCards, imageSource } from '@/lib/api';
import { formatMoney, formatSignedMoney, netProceeds } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';

export default function InventoryScreen() {
  const [status, setStatus] = useState<CardStatus | undefined>(undefined);

  // The whole collection is fetched once and filtered here, so switching filters is
  // instant and the summary always covers everything.
  const cards = useQuery({ queryKey: ['cards', 'ALL'], queryFn: () => getCards() });
  const visible = useMemo(
    () => (status ? cards.data?.filter((c) => c.status === status) : cards.data) ?? [],
    [cards.data, status],
  );

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      {cards.isPending ? (
        <Loading />
      ) : cards.isError ? (
        <View style={{ padding: space.lg }}>
          <ErrorBanner message={cards.error.message} onRetry={() => cards.refetch()} />
        </View>
      ) : (
        <FlatList
          data={visible}
          keyExtractor={(card) => String(card.id)}
          numColumns={2}
          columnWrapperStyle={{ gap: space.md }}
          renderItem={({ item }) => <CardTile card={item} />}
          contentContainerStyle={styles.list}
          refreshControl={
            <RefreshControl refreshing={cards.isRefetching} onRefresh={() => void cards.refetch()} tintColor={colors.primary} />
          }
          ListHeaderComponent={
            <View style={{ gap: space.lg, marginBottom: space.xs }}>
              <ScreenTitle title="Collection" subtitle={`${cards.data.length} card${cards.data.length === 1 ? '' : 's'}`} />
              {cards.data.length > 0 && <Summary cards={cards.data} />}
              <StatusPicker value={status} onChange={setStatus} includeAll />
            </View>
          }
          ListEmptyComponent={
            <View style={styles.empty}>
              <View style={styles.emptyIcon}>
                <Ionicons name="albums-outline" size={36} color={colors.primary} />
              </View>
              <Text style={styles.emptyTitle}>{status ? 'Nothing here yet' : 'Start your collection'}</Text>
              <Text style={styles.emptyBody}>
                {status ? 'No cards with this status.' : 'Scan a card and it shows up here with its market value.'}
              </Text>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}

function Summary({ cards }: { cards: Card[] }) {
  const stats = useMemo(() => {
    const held = cards.filter((c) => c.status !== 'SOLD');
    const sold = cards.filter((c) => c.status === 'SOLD' && c.soldPrice != null);
    const cost = (c: Card) => (c.costBasis ?? 0) + (c.gradingCost ?? 0);
    return {
      value: held.reduce((sum, c) => sum + (c.marketPrice ?? c.listedPrice ?? c.suggestedPrice ?? 0), 0),
      cost: held.reduce((sum, c) => sum + cost(c), 0),
      realized: sold.reduce((sum, c) => sum + netProceeds(c.soldPrice!) - (c.saleShippingCost ?? 0) - cost(c), 0),
      soldCount: sold.length,
    };
  }, [cards]);

  return (
    <View style={styles.summary}>
      <Text style={styles.summaryLabel}>Market value</Text>
      <Text style={styles.summaryValue}>{formatMoney(stats.value)}</Text>
      <View style={styles.summaryRow}>
        <Stat label="Cost in" value={formatMoney(stats.cost)} />
        <Stat
          label={`Realized · ${stats.soldCount} sold`}
          value={stats.soldCount ? formatSignedMoney(stats.realized) : '—'}
          color={stats.soldCount ? (stats.realized >= 0 ? colors.primary : colors.danger) : undefined}
        />
      </View>
      <Pressable
        onPress={() => router.push('/inventory/pnl')}
        accessibilityRole="button"
        accessibilityLabel="Profit and loss"
        style={({ pressed }) => [styles.pnlLink, pressed && { opacity: 0.7 }]}>
        <Ionicons name="stats-chart" size={16} color={colors.primary} />
        <Text style={styles.pnlText}>Profit & loss, movers, export</Text>
        <Ionicons name="chevron-forward" size={16} color={colors.primary} />
      </Pressable>
    </View>
  );
}

function Stat({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <View style={{ flex: 1, gap: 2 }}>
      <Text style={styles.statLabel}>{label}</Text>
      <Text style={[styles.statValue, color && { color }]}>{value}</Text>
    </View>
  );
}

const CardTile = memo(function CardTile({ card }: { card: Card }) {
  const thumb = imageSource(card.frontImageUrl);
  const price =
    card.status === 'SOLD' ? card.soldPrice : card.status === 'LISTED' ? card.listedPrice : (card.marketPrice ?? card.suggestedPrice);
  const name = card.player ?? card.ebayTitle;
  const meta = [card.year, card.set ?? card.manufacturer].filter(Boolean).join(' ');

  return (
    <Pressable
      onPress={() => router.push({ pathname: '/inventory/[id]', params: { id: String(card.id) } })}
      accessibilityRole="button"
      accessibilityLabel={`${card.ebayTitle}, ${formatMoney(price)}`}
      style={({ pressed }) => [styles.tile, pressed && { transform: [{ scale: 0.97 }], opacity: 0.9 }]}>
      <View style={styles.thumb}>
        {thumb ? (
          <Image source={thumb} style={StyleSheet.absoluteFill} contentFit="cover" transition={200} />
        ) : (
          <Ionicons name="image-outline" size={28} color={colors.textMuted} />
        )}
        <View style={styles.tileStatus}>
          <StatusPill status={card.status} />
        </View>
      </View>
      <View style={styles.tileBody}>
        <Text style={styles.tileName} numberOfLines={1}>
          {name}
        </Text>
        <Text style={styles.tileMeta} numberOfLines={1}>
          {meta || ' '}
        </Text>
        <Text style={styles.tilePrice}>{formatMoney(price)}</Text>
      </View>
    </Pressable>
  );
});

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  list: { padding: space.lg, gap: space.md, paddingBottom: space.xxl },
  summary: {
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    borderWidth: 1,
    borderColor: colors.border,
    padding: space.lg + 2,
    gap: 2,
  },
  summaryLabel: {
    fontSize: 12,
    fontFamily: fonts.bodySemiBold,
    color: colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 1.2,
  },
  summaryValue: { fontSize: 40, fontFamily: fonts.display, color: colors.text, letterSpacing: -1.2 },
  summaryRow: {
    flexDirection: 'row',
    gap: space.lg,
    marginTop: space.md,
    paddingTop: space.md,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.border,
  },
  pnlLink: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
    marginTop: space.md,
    paddingTop: space.md,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.border,
  },
  pnlText: { flex: 1, color: colors.primary, fontFamily: fonts.bodySemiBold, fontSize: 14 },
  statLabel: { fontSize: 12, fontFamily: fonts.bodyMedium, color: colors.textMuted },
  statValue: { fontSize: 18, fontFamily: fonts.displayMedium, color: colors.text },
  tile: {
    flex: 1,
    maxWidth: '50%',
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    overflow: 'hidden',
  },
  thumb: {
    aspectRatio: 2.5 / 3.5,
    backgroundColor: colors.surfaceRaised,
    alignItems: 'center',
    justifyContent: 'center',
  },
  tileStatus: { position: 'absolute', top: space.sm, left: space.sm },
  tileBody: { padding: space.md, gap: 2 },
  tileName: { fontSize: 15, fontFamily: fonts.bodySemiBold, color: colors.text },
  tileMeta: { fontSize: 12, fontFamily: fonts.body, color: colors.textSecondary },
  tilePrice: { fontSize: 18, fontFamily: fonts.display, color: colors.text, marginTop: 4 },
  empty: { alignItems: 'center', gap: space.sm, paddingVertical: 64, paddingHorizontal: space.xl },
  emptyIcon: {
    width: 72,
    height: 72,
    borderRadius: radius.xl,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: space.sm,
  },
  emptyTitle: { fontSize: 20, fontFamily: fonts.display, color: colors.text },
  emptyBody: { color: colors.textSecondary, textAlign: 'center', fontFamily: fonts.body, fontSize: 15, lineHeight: 22 },
});
