import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as Haptics from 'expo-haptics';
import { Image } from 'expo-image';
import { router } from 'expo-router';
import * as WebBrowser from 'expo-web-browser';
import { memo, useState } from 'react';
import { FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { Button, ErrorBanner, Loading, ScreenTitle } from '@/components/ui';
import { getHits, getMarketDeals, getWatchlists, HitStatus, MarketDeal, updateHitStatus, WatchlistHit } from '@/lib/api';
import { formatMoney } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';
import { relativeTime, timeLeft } from '@/lib/time';

type Tab = 'alerts' | 'saved' | 'market';

const TABS: { key: Tab; label: string }[] = [
  { key: 'alerts', label: 'My alerts' },
  { key: 'saved', label: 'Saved' },
  { key: 'market', label: 'Market' },
];

const tierLabels: Record<string, string> = {
  raw: 'Raw',
  g1: 'Grade 1',
  g2: 'Grade 2',
  g3: 'Grade 3',
  g4: 'Grade 4',
  g5: 'Grade 5',
  g6: 'Grade 6',
  sgc10: 'SGC 10',
  cgc10: 'CGC 10',
  g7: 'Grade 7',
  g8: 'Grade 8',
  g9: 'Grade 9',
  g95: 'Grade 9.5',
  psa10: 'PSA 10',
  bgs10: 'BGS 10',
};

export default function DealsScreen() {
  const [tab, setTab] = useState<Tab>('alerts');
  const queryClient = useQueryClient();

  // The watchlist worker runs every few minutes; poll while the screen is up
  const alerts = useQuery({ queryKey: ['hits', 'alerts'], queryFn: () => getHits(), refetchInterval: 60_000, enabled: tab === 'alerts' });
  const saved = useQuery({ queryKey: ['hits', 'saved'], queryFn: () => getHits({ status: 'SAVED' }), enabled: tab === 'saved' });
  const market = useQuery({ queryKey: ['market-deals'], queryFn: getMarketDeals, refetchInterval: 120_000, enabled: tab === 'market' });
  const watchlists = useQuery({ queryKey: ['watchlists'], queryFn: getWatchlists });

  const setStatus = useMutation({
    mutationFn: ({ id, status }: { id: number; status: HitStatus }) => updateHitStatus(id, status),
    onMutate: () => void Haptics.selectionAsync(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hits'] }),
  });

  const active = tab === 'market' ? market : tab === 'saved' ? saved : alerts;
  const activeLists = watchlists.data?.filter((w) => w.isActive).length ?? 0;

  const header = (
    <View style={{ gap: space.lg, marginBottom: space.xs }}>
      <ScreenTitle
        title="Deals"
        subtitle={
          tab === 'market'
            ? 'Underpriced listings from the CardScout deal finder'
            : `${activeLists} active watchlist${activeLists === 1 ? '' : 's'} · checked every 5 min`
        }
        right={
          <View style={{ flexDirection: 'row', gap: space.sm }}>
            <IconButton icon="list" label="Watchlists" onPress={() => router.push('/deals/watchlists')} />
            <IconButton icon="settings-outline" label="Settings" onPress={() => router.push('/settings')} />
          </View>
        }
      />
      <View style={styles.segmented} accessibilityRole="tablist">
        {TABS.map((t) => (
          <Pressable
            key={t.key}
            accessibilityRole="tab"
            accessibilityState={{ selected: tab === t.key }}
            onPress={() => {
              if (tab !== t.key) void Haptics.selectionAsync();
              setTab(t.key);
            }}
            style={[styles.segment, tab === t.key && styles.segmentSelected]}>
            <Text style={[styles.segmentText, tab === t.key && { color: colors.text }]}>{t.label}</Text>
          </Pressable>
        ))}
      </View>
      {setStatus.isError && <ErrorBanner message={setStatus.error.message} />}
    </View>
  );

  if (active.isPending && !active.data) {
    return (
      <SafeAreaView style={styles.screen} edges={['top']}>
        <View style={styles.content}>{header}</View>
        <Loading />
      </SafeAreaView>
    );
  }

  const refresh = (
    <RefreshControl refreshing={active.isRefetching} onRefresh={() => void active.refetch()} tintColor={colors.primary} />
  );

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      {tab === 'market' ? (
        <FlatList
          data={market.data ?? []}
          keyExtractor={(d) => d.itemId}
          contentContainerStyle={styles.content}
          refreshControl={refresh}
          ListHeaderComponent={
            <>
              {header}
              {market.isError && <ErrorBanner message={market.error.message} onRetry={() => market.refetch()} />}
            </>
          }
          ItemSeparatorComponent={Separator}
          renderItem={({ item }) => <MarketDealRow deal={item} />}
          ListEmptyComponent={<Empty icon="trending-down" title="No market deals right now" body="The deal finder checks eBay for your watched players every 10 minutes." />}
        />
      ) : (
        <FlatList
          data={(tab === 'saved' ? saved.data : alerts.data) ?? []}
          keyExtractor={(h) => String(h.id)}
          contentContainerStyle={styles.content}
          refreshControl={refresh}
          ListHeaderComponent={
            <>
              {header}
              {active.isError && <ErrorBanner message={active.error.message} onRetry={() => active.refetch()} />}
            </>
          }
          ItemSeparatorComponent={Separator}
          renderItem={({ item }) => (
            <HitRow
              hit={item}
              busy={setStatus.isPending}
              onStatus={(status) => setStatus.mutate({ id: item.id, status })}
            />
          )}
          ListEmptyComponent={
            tab === 'saved' ? (
              <Empty icon="bookmark-outline" title="Nothing saved" body="Tap Save on an alert to keep it here." />
            ) : activeLists === 0 ? (
              <Empty
                icon="notifications-outline"
                title="Watch eBay for flips"
                body="Create a watchlist (a player, set or card) and CardScout checks new eBay listings every 5 minutes, prices them, and alerts you when one would flip for a profit after fees and shipping."
                action={<Button title="Create a watchlist" icon="add" onPress={() => router.push({ pathname: '/deals/watchlist/[id]', params: { id: 'new' } })} />}
              />
            ) : (
              <Empty icon="search" title="No profitable listings yet" body="Your watchlists are running. New alerts show up here (and as a push notification)." />
            )
          }
        />
      )}
    </SafeAreaView>
  );
}

const Separator = () => <View style={{ height: space.md }} />;

function IconButton({ icon, label, onPress }: { icon: React.ComponentProps<typeof Ionicons>['name']; label: string; onPress: () => void }) {
  return (
    <Pressable onPress={onPress} hitSlop={6} accessibilityRole="button" accessibilityLabel={label} style={({ pressed }) => [styles.iconButton, pressed && { opacity: 0.7 }]}>
      <Ionicons name={icon} size={20} color={colors.text} />
    </Pressable>
  );
}

function Empty({ icon, title, body, action }: { icon: React.ComponentProps<typeof Ionicons>['name']; title: string; body: string; action?: React.ReactNode }) {
  return (
    <View style={styles.empty}>
      <View style={styles.emptyIcon}>
        <Ionicons name={icon} size={34} color={colors.primary} />
      </View>
      <Text style={styles.emptyTitle}>{title}</Text>
      <Text style={styles.emptyBody}>{body}</Text>
      {action && <View style={{ alignSelf: 'stretch', marginTop: space.md }}>{action}</View>}
    </View>
  );
}

const HitRow = memo(function HitRow({ hit, busy, onStatus }: { hit: WatchlistHit; busy: boolean; onStatus: (s: HitStatus) => void }) {
  const auction = hit.listingType === 'AUCTION';
  const left = auction ? timeLeft(hit.endTime) : null;
  const profitColor = (hit.netProfit ?? 0) >= 0 ? colors.primary : colors.danger;

  return (
    <View style={styles.card}>
      <Pressable
        onPress={() => hit.listingUrl && WebBrowser.openBrowserAsync(hit.listingUrl)}
        accessibilityRole="link"
        accessibilityLabel={`${hit.title}, open on eBay`}
        style={({ pressed }) => [styles.row, pressed && { opacity: 0.8 }]}>
        <View style={styles.thumb}>
          {hit.imageUrl ? (
            <Image source={{ uri: hit.imageUrl }} style={StyleSheet.absoluteFill} contentFit="cover" transition={150} />
          ) : (
            <Ionicons name="image-outline" size={22} color={colors.textMuted} />
          )}
        </View>
        <View style={{ flex: 1, gap: 3 }}>
          <View style={styles.pillRow}>
            <View style={[styles.pill, { backgroundColor: auction ? colors.warningSoft : colors.infoSoft }]}>
              <Text style={[styles.pillText, { color: auction ? colors.warning : colors.info }]}>
                {auction ? (left ?? 'Ending') : 'Buy It Now'}
              </Text>
            </View>
            <Text style={styles.small} numberOfLines={1}>
              {hit.watchlistName} · {relativeTime(hit.createdAt)}
            </Text>
          </View>
          <Text style={styles.title} numberOfLines={2}>
            {hit.title}
          </Text>
          <Text style={styles.muted} numberOfLines={1}>
            {formatMoney(hit.price)}
            {hit.shippingCost != null ? ` + ${formatMoney(hit.shippingCost)} ship${hit.shippingEstimated ? ' (est.)' : ''}` : ''}
            {' · comp '}
            {formatMoney(hit.compPrice)}
            {hit.compTier ? ` ${tierLabels[hit.compTier] ?? ''}` : ''}
          </Text>
        </View>
      </Pressable>

      <View style={styles.numbers}>
        <Num label="Net profit" value={formatMoney(hit.netProfit)} color={profitColor} big />
        <Num label="ROI" value={hit.netProfitPct == null ? '—' : `${Math.round(hit.netProfitPct)}%`} color={profitColor} />
        <Num label={auction ? 'Max bid' : 'Pay up to'} value={formatMoney(hit.maxBuyPrice)} />
        <Num label="Match" value={hit.matchConfidence == null ? '—' : `${Math.round(hit.matchConfidence * 100)}%`} />
      </View>

      <View style={styles.actions}>
        <View style={{ flex: 1 }}>
          <Button
            title={auction ? 'Bid on eBay' : 'Buy on eBay'}
            icon="open-outline"
            compact
            onPress={() => hit.listingUrl && WebBrowser.openBrowserAsync(hit.listingUrl)}
          />
        </View>
        {hit.status !== 'SAVED' ? (
          <Button title="Save" icon="bookmark-outline" variant="secondary" compact disabled={busy} onPress={() => onStatus('SAVED')} />
        ) : (
          <Button title="Bought" icon="bag-check-outline" variant="secondary" compact disabled={busy} onPress={() => onStatus('BOUGHT')} />
        )}
        <Pressable
          onPress={() => onStatus('DISMISSED')}
          disabled={busy}
          hitSlop={6}
          accessibilityRole="button"
          accessibilityLabel="Dismiss"
          style={({ pressed }) => [styles.dismiss, pressed && { opacity: 0.6 }]}>
          <Ionicons name="close" size={20} color={colors.textSecondary} />
        </Pressable>
      </View>
    </View>
  );
});

const MarketDealRow = memo(function MarketDealRow({ deal }: { deal: MarketDeal }) {
  const auction = deal.listingType === 'AUCTION';
  const left = auction ? timeLeft(deal.endTime) : null;
  return (
    <Pressable
      onPress={() => WebBrowser.openBrowserAsync(deal.url)}
      accessibilityRole="link"
      accessibilityLabel={`${deal.title}, open on eBay`}
      style={({ pressed }) => [styles.card, pressed && { opacity: 0.85 }]}>
      <View style={styles.pillRow}>
        <View style={[styles.pill, { backgroundColor: auction ? colors.warningSoft : colors.infoSoft }]}>
          <Text style={[styles.pillText, { color: auction ? colors.warning : colors.info }]}>{auction ? (left ?? 'Ending') : 'Buy It Now'}</Text>
        </View>
        <Text style={styles.small}>found {relativeTime(deal.foundAt)}</Text>
      </View>
      <Text style={styles.title} numberOfLines={2}>
        {deal.title}
      </Text>
      <Text style={styles.muted} numberOfLines={1}>
        {formatMoney(deal.price)}
        {deal.shippingCost != null ? ` + ${formatMoney(deal.shippingCost)} ship` : ''} · comp {formatMoney(deal.compPrice)} ·{' '}
        {deal.compName}
      </Text>
      <View style={styles.numbers}>
        <Num label="Net profit" value={formatMoney(deal.netProfit)} color={colors.primary} big />
        <Num label="ROI" value={`${Math.round(deal.netProfitPct)}%`} color={colors.primary} />
        <Num label="Under comp" value={`${Math.round(deal.discountPct)}%`} />
        <Num label="Match" value={`${Math.round(deal.matchConfidence * 100)}%`} />
      </View>
    </Pressable>
  );
});

function Num({ label, value, color, big }: { label: string; value: string; color?: string; big?: boolean }) {
  return (
    <View style={{ flex: big ? 1.3 : 1, gap: 1 }}>
      <Text style={styles.numLabel}>{label}</Text>
      <Text style={[styles.numValue, big && { fontSize: 19 }, color && { color }]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  content: { padding: space.lg, gap: space.md, paddingBottom: space.xxl },
  iconButton: {
    width: 40,
    height: 40,
    borderRadius: radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surfaceRaised,
    borderWidth: 1,
    borderColor: colors.border,
    marginBottom: 4,
  },
  segmented: {
    flexDirection: 'row',
    borderRadius: radius.md,
    padding: 4,
    gap: 4,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
  segment: { flex: 1, minHeight: 38, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center' },
  segmentSelected: { backgroundColor: colors.surfacePressed },
  segmentText: { color: colors.textMuted, fontFamily: fonts.bodySemiBold, fontSize: 14 },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    borderWidth: 1,
    borderColor: colors.border,
    padding: space.lg,
    gap: space.md,
  },
  row: { flexDirection: 'row', gap: space.md },
  thumb: {
    width: 64,
    height: 86,
    borderRadius: radius.sm,
    overflow: 'hidden',
    backgroundColor: colors.surfaceRaised,
    alignItems: 'center',
    justifyContent: 'center',
  },
  pillRow: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  pill: { borderRadius: radius.pill, paddingHorizontal: 8, paddingVertical: 3 },
  pillText: { fontSize: 11, fontFamily: fonts.bodyBold },
  title: { fontSize: 15, fontFamily: fonts.bodySemiBold, color: colors.text, lineHeight: 20 },
  muted: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary },
  small: { flexShrink: 1, fontSize: 12, fontFamily: fonts.body, color: colors.textMuted },
  numbers: {
    flexDirection: 'row',
    gap: space.sm,
    paddingTop: space.md,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.border,
  },
  numLabel: { fontSize: 11, fontFamily: fonts.bodySemiBold, color: colors.textMuted },
  numValue: { fontSize: 16, fontFamily: fonts.displayMedium, color: colors.text },
  actions: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  dismiss: { width: 44, height: 44, alignItems: 'center', justifyContent: 'center' },
  empty: { alignItems: 'center', gap: space.sm, paddingVertical: 48, paddingHorizontal: space.md },
  emptyIcon: {
    width: 72,
    height: 72,
    borderRadius: radius.xl,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: space.sm,
  },
  emptyTitle: { fontSize: 20, fontFamily: fonts.display, color: colors.text, textAlign: 'center' },
  emptyBody: { color: colors.textSecondary, textAlign: 'center', fontFamily: fonts.body, fontSize: 15, lineHeight: 22 },
});
