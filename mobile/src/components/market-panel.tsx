import { useQuery } from '@tanstack/react-query';
import { Image } from 'expo-image';
import * as WebBrowser from 'expo-web-browser';
import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';

import { getMarket, PriceQuoteRequest } from '@/lib/api';
import { formatMoney } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';
import { ErrorBanner, SectionTitle, Surface } from './ui';

// What the same card is listed for on eBay right now. These are asking prices, not sales:
// the cheapest Buy It Now is roughly the most a buyer would pay you for it today.
export function MarketPanel({ request, enabled = true }: { request: PriceQuoteRequest | null; enabled?: boolean }) {
  // Wait for typing to settle so editing the card details doesn't search eBay on every keystroke
  const settled = useDebounced(request, 900);
  const key =
    settled &&
    [settled.player, settled.year, settled.manufacturer, settled.set, settled.cardNumber, settled.parallel, settled.isAutograph, settled.gradingCompany, settled.grade];
  const market = useQuery({
    queryKey: ['market', key],
    queryFn: () => getMarket(settled!),
    enabled: enabled && !!settled?.player,
    staleTime: 10 * 60_000,
  });

  if (!request?.player) return null;

  return (
    <Surface>
      <SectionTitle right={market.isFetching ? <ActivityIndicator size="small" color={colors.primary} /> : undefined}>
        On eBay now
      </SectionTitle>
      {market.isError ? (
        <ErrorBanner message={market.error.message} onRetry={() => market.refetch()} />
      ) : market.data ? (
        market.data.activeCount === 0 ? (
          <Text style={styles.muted}>No matching active listings. It may be scarce, or the details may need fixing.</Text>
        ) : (
          <>
            <View style={styles.stats}>
              <Stat label="Lowest BIN" value={formatMoney(market.data.lowestBuyItNow)} highlight />
              <Stat label="Median BIN" value={formatMoney(market.data.medianBuyItNow)} />
              <Stat label="Listings" value={String(market.data.activeCount)} />
            </View>
            {market.data.cheapest.map((l) => (
              <Pressable
                key={l.itemId}
                onPress={() => WebBrowser.openBrowserAsync(l.url)}
                accessibilityRole="link"
                accessibilityLabel={`${l.title}, ${formatMoney(l.price)}`}
                style={({ pressed }) => [styles.row, pressed && { opacity: 0.7 }]}>
                <View style={styles.thumb}>
                  {l.imageUrl && <Image source={{ uri: l.imageUrl }} style={StyleSheet.absoluteFill} contentFit="cover" />}
                </View>
                <View style={{ flex: 1 }}>
                  <Text style={styles.title} numberOfLines={2}>
                    {l.title}
                  </Text>
                  <Text style={styles.muted}>
                    {l.listingType === 'AUCTION' ? 'Auction' : 'Buy It Now'}
                    {l.shipping != null ? ` · ${l.shipping === 0 ? 'free ship' : `+${formatMoney(l.shipping)} ship`}` : ''}
                  </Text>
                </View>
                <Text style={styles.price}>{formatMoney(l.price)}</Text>
              </Pressable>
            ))}
            <Text style={styles.footnote}>Asking prices, not sales. The comp above is based on recent sales.</Text>
          </>
        )
      ) : (
        <Text style={styles.muted}>Checking eBay…</Text>
      )}
    </Surface>
  );
}

function useDebounced<T>(value: T, ms: number): T {
  const [settled, setSettled] = useState(value);
  const serialized = JSON.stringify(value);
  useEffect(() => {
    const id = setTimeout(() => setSettled(value), ms);
    return () => clearTimeout(id);
    // Compare by content: callers pass a fresh object every render
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serialized, ms]);
  return settled;
}

function Stat({ label, value, highlight }: { label: string; value: string; highlight?: boolean }) {
  return (
    <View style={styles.stat}>
      <Text style={styles.statLabel}>{label}</Text>
      <Text style={[styles.statValue, highlight && { color: colors.primary }]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  stats: { flexDirection: 'row', gap: space.sm },
  stat: { flex: 1, gap: 2 },
  statLabel: { fontSize: 11, fontFamily: fonts.bodySemiBold, color: colors.textMuted },
  statValue: { fontSize: 18, fontFamily: fonts.displayMedium, color: colors.text },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.md, minHeight: 56 },
  thumb: { width: 40, height: 52, borderRadius: radius.sm, backgroundColor: colors.surfaceRaised, overflow: 'hidden' },
  title: { fontSize: 13, fontFamily: fonts.bodyMedium, color: colors.text, lineHeight: 18 },
  price: { fontSize: 16, fontFamily: fonts.displayMedium, color: colors.text },
  muted: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 18 },
  footnote: { fontSize: 11, fontFamily: fonts.body, color: colors.textMuted },
});
