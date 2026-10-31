import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { FlatList, Pressable, RefreshControl, StyleSheet, Switch, Text, View } from 'react-native';

import { Button, ErrorBanner, Loading } from '@/components/ui';
import { getWatchlists, updateWatchlist, Watchlist } from '@/lib/api';
import { formatMoney } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';
import { relativeTime } from '@/lib/time';

const typeLabel = { ANY: 'Auctions + BIN', AUCTION: 'Auctions', FIXED_PRICE: 'Buy It Now' } as const;

export default function WatchlistsScreen() {
  const queryClient = useQueryClient();
  const lists = useQuery({ queryKey: ['watchlists'], queryFn: getWatchlists });

  const toggle = useMutation({
    mutationFn: (w: Watchlist) => updateWatchlist(w.id, { ...w, isActive: !w.isActive }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['watchlists'] }),
  });

  if (lists.isPending) return <Loading />;

  return (
    <FlatList
      style={{ backgroundColor: colors.background }}
      data={lists.data ?? []}
      keyExtractor={(w) => String(w.id)}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={lists.isRefetching} onRefresh={() => void lists.refetch()} tintColor={colors.primary} />}
      ListHeaderComponent={
        <View style={{ gap: space.md }}>
          {lists.isError && <ErrorBanner message={lists.error.message} onRetry={() => lists.refetch()} />}
          {toggle.isError && <ErrorBanner message={toggle.error.message} />}
          <Button title="New watchlist" icon="add" onPress={() => router.push({ pathname: '/deals/watchlist/[id]', params: { id: 'new' } })} />
        </View>
      }
      ItemSeparatorComponent={() => <View style={{ height: space.md }} />}
      renderItem={({ item }) => (
        <Pressable
          onPress={() => router.push({ pathname: '/deals/watchlist/[id]', params: { id: String(item.id) } })}
          accessibilityRole="button"
          accessibilityLabel={`Edit ${item.name}`}
          style={({ pressed }) => [styles.card, !item.isActive && { opacity: 0.6 }, pressed && { backgroundColor: colors.surfacePressed }]}>
          <View style={{ flex: 1, gap: 3 }}>
            <Text style={styles.name}>{item.name}</Text>
            <Text style={styles.query} numberOfLines={1}>
              “{item.query}”
            </Text>
            <Text style={styles.meta}>
              {[
                typeLabel[item.listingType],
                item.maxPrice != null && `≤ ${formatMoney(item.maxPrice, { cents: false })}`,
                `+$${item.minProfit} & ${item.minProfitPct}%`,
                item.gradedOnly && 'graded only',
              ]
                .filter(Boolean)
                .join(' · ')}
            </Text>
            <Text style={styles.small}>
              {item.lastRunAt ? `Checked ${relativeTime(item.lastRunAt)}` : 'Not checked yet'}
              {item.notify ? '' : ' · alerts off'}
            </Text>
          </View>
          <View style={{ alignItems: 'center', gap: space.sm }}>
            <Switch
              value={item.isActive}
              onValueChange={() => toggle.mutate(item)}
              trackColor={{ true: colors.primary, false: colors.borderStrong }}
              accessibilityLabel={`${item.name} active`}
            />
            <Ionicons name="chevron-forward" size={16} color={colors.textMuted} />
          </View>
        </Pressable>
      )}
      ListEmptyComponent={
        <Text style={styles.emptyText}>
          No watchlists yet. Try a player plus a qualifier, e.g. “Elly De La Cruz PSA 10” or “2023 Bowman Chrome auto”.
        </Text>
      }
    />
  );
}

const styles = StyleSheet.create({
  content: { padding: space.lg, gap: space.md, paddingBottom: space.xxl },
  card: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: space.lg,
  },
  name: { fontSize: 17, fontFamily: fonts.display, color: colors.text },
  query: { fontSize: 14, fontFamily: fonts.bodyMedium, color: colors.textSecondary },
  meta: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary },
  small: { fontSize: 12, fontFamily: fonts.body, color: colors.textMuted },
  emptyText: { color: colors.textSecondary, fontFamily: fonts.body, fontSize: 15, lineHeight: 22, textAlign: 'center', marginTop: space.xl },
});
