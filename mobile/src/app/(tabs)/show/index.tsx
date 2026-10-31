import Ionicons from '@expo/vector-icons/Ionicons';
import { useQueryClient } from '@tanstack/react-query';
import * as Haptics from 'expo-haptics';
import { Image } from 'expo-image';
import { router } from 'expo-router';
import { memo, useCallback, useMemo, useRef, useState } from 'react';
import { FlatList, KeyboardAvoidingView, Platform, Pressable, StyleSheet, Text, View } from 'react-native';
import Animated, { FadeInDown, LinearTransition } from 'react-native-reanimated';
import { SafeAreaView } from 'react-native-safe-area-context';

import { CapturedPhoto, CardCamera } from '@/components/card-camera';
import { GradingPanel } from '@/components/grading-panel';
import { SoldCompsPanel } from '@/components/sold-comps-panel';
import { HeaderActions } from '@/components/header-actions';
import { Button, ErrorBanner, Loading, ScreenTitle } from '@/components/ui';
import { UsageBanner } from '@/components/usage';
import { VerdictPanel } from '@/components/verdict-panel';
import { Card, createCard, identifyCard, IdentifyResponse } from '@/lib/api';
import { isConfigured } from '@/lib/config';
import { analyzeShowBuy, verdictStyle } from '@/lib/flip';
import { prepareForUpload } from '@/lib/images';
import { formatMoney, formatSignedMoney, netProceeds } from '@/lib/money';
import { updateSettings, useSettings } from '@/lib/settings';
import { cardMetaParts } from '@/lib/card-meta';
import { colors, fonts, radius, space } from '@/lib/theme';

interface ShowItem {
  key: string;
  photoUri: string;
  status: 'working' | 'done' | 'error';
  result?: IdentifyResponse;
  error?: string;
  asking: string;
  bought?: Card;
  buying?: boolean;
  buyError?: string;
}

const parseMoney = (s: string) => {
  const n = Number(s.replace(/[$,]/g, '').trim());
  return s.trim() !== '' && Number.isFinite(n) ? n : null;
};

// Card show mode: snap the front of a card, get the market price and a buy / pass verdict
// against the dealer's asking price in a few seconds, and log what you buy. Scans run in
// the background, so you can keep scanning while earlier cards are still being priced.
export default function ShowScreen() {
  const queryClient = useQueryClient();
  const settings = useSettings();
  const [items, setItems] = useState<ShowItem[]>([]);
  const [capturing, setCapturing] = useState(false);
  const [preparing, setPreparing] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const counter = useRef(0);

  const patch = useCallback((key: string, change: Partial<ShowItem>) => {
    setItems((all) => all.map((i) => (i.key === key ? { ...i, ...change } : i)));
  }, []);

  const identify = useCallback(
    async (key: string, uri: string) => {
      patch(key, { status: 'working', error: undefined });
      try {
        const result = await identifyCard(uri);
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        patch(key, { status: 'done', result });
      } catch (e) {
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
        patch(key, { status: 'error', error: e instanceof Error ? e.message : String(e) });
      }
    },
    [patch],
  );

  async function handleCaptured(photo: CapturedPhoto) {
    setCapturing(false);
    setPreparing(true);
    try {
      const uri = await prepareForUpload(photo.uri, photo.width, photo.height);
      const key = `scan-${++counter.current}`;
      setItems((all) => [{ key, photoUri: uri, status: 'working', asking: '' }, ...all]);
      setExpanded(key);
      void identify(key, uri);
    } finally {
      setPreparing(false);
    }
  }

  const buy = useCallback(
    async (item: ShowItem) => {
      const r = item.result;
      const paid = parseMoney(item.asking);
      if (!r) return;
      patch(item.key, { buying: true, buyError: undefined });
      try {
        const card = await createCard({
          player: r.card.player,
          year: r.card.year,
          manufacturer: r.card.manufacturer,
          set: r.card.set,
          cardNumber: r.card.cardNumber,
          parallel: r.card.parallel,
          serialNumber: r.card.serialNumber,
          isRookie: r.card.isRookie,
          isAutograph: r.card.isAutograph,
          isRelic: r.card.isRelic,
          gradingCompany: r.card.gradingCompany,
          grade: r.card.grade,
          certNumber: r.card.certNumber ?? null,
          conditionNotes: r.card.conditionNotes,
          ebayTitle: r.card.suggestedEbayTitle,
          suggestedPrice: r.price.suggestedPrice,
          costBasis: paid,
          confidence: r.card.confidence,
          compProductId: r.price.compProductId,
          frontImagePath: r.frontImagePath,
          backImagePath: r.backImagePath,
          aiRawJson: r.aiRawJson,
          purchaseSource: 'Card show',
          purchasedAt: new Date().toISOString(),
          category: r.card.category === 'tcg' ? 'tcg' : 'sports',
          game: r.card.game ?? null,
          setCode: r.card.setCode ?? null,
          rarity: r.card.rarity ?? null,
          language: r.card.language ?? null,
        });
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        queryClient.invalidateQueries({ queryKey: ['cards'] });
        queryClient.invalidateQueries({ queryKey: ['portfolio'] });
        patch(item.key, { bought: card, buying: false });
      } catch (e) {
        patch(item.key, { buying: false, buyError: e instanceof Error ? e.message : String(e) });
      }
    },
    [patch, queryClient],
  );

  const session = useMemo(() => {
    const bought = items.filter((i) => i.bought);
    const spent = bought.reduce((sum, i) => sum + (parseMoney(i.asking) ?? 0), 0);
    const profit = bought.reduce((sum, i) => {
      const market = i.result?.price.suggestedPrice;
      if (market == null) return sum;
      const resale = settings.show.resale === 'cash' ? market : netProceeds(market);
      return sum + resale - (parseMoney(i.asking) ?? 0);
    }, 0);
    return { scanned: items.length, bought: bought.length, spent, profit };
  }, [items, settings.show.resale]);

  if (!isConfigured) {
    return (
      <SafeAreaView style={styles.screen} edges={['top']}>
        <View style={{ padding: space.lg }}>
          <ErrorBanner message="API not configured. Fill in mobile/.env, then restart Expo." />
        </View>
      </SafeAreaView>
    );
  }

  if (capturing) {
    return <CardCamera side="front" mode="quick" onCaptured={handleCaptured} onCancel={() => setCapturing(false)} />;
  }
  if (preparing) return <Loading label="Processing photo…" />;


  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <FlatList
          data={items}
          keyExtractor={(i) => i.key}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode="on-drag"
          contentContainerStyle={styles.content}
          ListHeaderComponent={
            <View style={{ gap: space.lg }}>
              <ScreenTitle
                title="Show mode"
                subtitle="Sold comps, cash offers, buy or pass"
                right={<HeaderActions />}
              />
              <UsageBanner />
              {items.length > 0 && (
                <View style={styles.session}>
                  <SessionStat label="Scanned" value={String(session.scanned)} />
                  <SessionStat label="Bought" value={String(session.bought)} />
                  <SessionStat label="Spent" value={formatMoney(session.spent, { cents: false })} />
                  <SessionStat
                    label="Est. profit"
                    value={session.bought ? formatSignedMoney(session.profit) : '—'}
                    color={session.bought ? (session.profit >= 0 ? colors.primary : colors.danger) : undefined}
                  />
                </View>
              )}
              <BuyTerms />
              <View style={styles.actions}>
                <View style={{ flex: 2 }}>
                  <Button title="Scan a card" icon="scan" onPress={() => setCapturing(true)} />
                </View>
                <View style={{ flex: 1 }}>
                  <Button title="Lot" icon="grid-outline" variant="secondary" onPress={() => router.push('/show/lot')} />
                </View>
              </View>
            </View>
          }
          ItemSeparatorComponent={() => <View style={{ height: space.md }} />}
          renderItem={({ item }) => (
            <ShowItemCard
              item={item}
              expanded={expanded === item.key}
              onToggle={() => setExpanded(expanded === item.key ? null : item.key)}
              onAsking={(asking) => patch(item.key, { asking })}
              onBuy={() => buy(item)}
              onRetry={() => identify(item.key, item.photoUri)}
              onRemove={() => setItems((all) => all.filter((i) => i.key !== item.key))}
            />
          )}
          ListEmptyComponent={
            <View style={styles.empty}>
              <View style={styles.emptyIcon}>
                <Ionicons name="storefront-outline" size={34} color={colors.primary} />
              </View>
              <Text style={styles.emptyTitle}>Walk the floor</Text>
              <Text style={styles.emptyBody}>
                Snap the front of any card on a dealer’s table. You’ll see what it sells for, what it’s listed for on eBay
                right now, and whether the asking price is a buy, fair, or a pass for your profit target.
              </Text>
            </View>
          }
        />
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

// How you buy at shows: the share of sold comps you'll pay, and how you'll resell
function BuyTerms() {
  const settings = useSettings();
  const { buyPct, resale } = settings.show;
  const setPct = (pct: number) => {
    void Haptics.selectionAsync();
    updateSettings({ show: { ...settings.show, buyPct: Math.min(100, Math.max(40, pct)) } });
  };
  return (
    <View style={styles.terms}>
      <View style={{ flex: 1, gap: 2 }}>
        <Text style={styles.sessionLabel}>Paying up to</Text>
        <Text style={styles.termsValue}>{buyPct}% of comps</Text>
      </View>
      <View style={styles.stepper}>
        <Pressable onPress={() => setPct(buyPct - 5)} disabled={buyPct <= 40} hitSlop={6} accessibilityRole="button" accessibilityLabel="Lower buy percentage" style={({ pressed }) => [styles.stepButton, (pressed || buyPct <= 40) && { opacity: 0.5 }]}>
          <Ionicons name="remove" size={18} color={colors.text} />
        </Pressable>
        <Pressable onPress={() => setPct(buyPct + 5)} disabled={buyPct >= 100} hitSlop={6} accessibilityRole="button" accessibilityLabel="Raise buy percentage" style={({ pressed }) => [styles.stepButton, (pressed || buyPct >= 100) && { opacity: 0.5 }]}>
          <Ionicons name="add" size={18} color={colors.text} />
        </Pressable>
      </View>
      <Pressable
        onPress={() => {
          void Haptics.selectionAsync();
          updateSettings({ show: { ...settings.show, resale: resale === 'cash' ? 'ebay' : 'cash' } });
        }}
        accessibilityRole="button"
        accessibilityLabel={`Resale: ${resale === 'cash' ? 'cash, no fees' : 'eBay, after fees'}. Tap to switch.`}
        style={({ pressed }) => [styles.resale, pressed && { opacity: 0.7 }]}>
        <Ionicons name={resale === 'cash' ? 'cash-outline' : 'logo-usd'} size={14} color={colors.primary} />
        <Text style={styles.resaleText}>{resale === 'cash' ? 'Cash resale' : 'eBay resale'}</Text>
      </Pressable>
    </View>
  );
}

function SessionStat({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <View style={{ flex: 1, gap: 2 }}>
      <Text style={styles.sessionLabel}>{label}</Text>
      <Text style={[styles.sessionValue, color && { color }]}>{value}</Text>
    </View>
  );
}

const ShowItemCard = memo(function ShowItemCard({
  item,
  expanded,
  onToggle,
  onAsking,
  onBuy,
  onRetry,
  onRemove,
}: {
  item: ShowItem;
  expanded: boolean;
  onToggle: () => void;
  onAsking: (v: string) => void;
  onBuy: () => void;
  onRetry: () => void;
  onRemove: () => void;
}) {
  const settings = useSettings();
  const r = item.result;
  const quote = r?.price;
  const market = quote?.suggestedPrice ?? null;
  const tier = quote?.tiers.find((t) => t.key === quote.selectedTier);
  const asking = parseMoney(item.asking);
  const flip = asking != null ? analyzeShowBuy(market, asking, settings.show, quote?.matchConfidence) : null;
  const v = flip ? verdictStyle[flip.verdict] : null;

  const title = r ? (r.card.player ?? r.card.suggestedEbayTitle) : item.status === 'error' ? "Couldn't identify" : 'Identifying…';
  const meta = r
    ? [...cardMetaParts(r.card),
        r.card.gradingCompany && r.card.grade && `${r.card.gradingCompany} ${r.card.grade}`,
        r.card.certNumber && `cert ${r.card.certNumber}`]
        .filter(Boolean)
        .join(' · ')
    : '';

  return (
    <Animated.View entering={FadeInDown.duration(250)} layout={LinearTransition} style={styles.item}>
      <Pressable onPress={onToggle} disabled={!r} style={styles.itemHeader} accessibilityRole="button" accessibilityLabel={title}>
        <View style={styles.thumb}>
          <Image source={{ uri: item.photoUri }} style={StyleSheet.absoluteFill} contentFit="cover" />
        </View>
        <View style={{ flex: 1, gap: 2 }}>
          <Text style={styles.itemTitle} numberOfLines={1}>
            {title}
          </Text>
          {meta.length > 0 && (
            <Text style={styles.itemMeta} numberOfLines={2}>
              {meta}
            </Text>
          )}
          {r && (
            <Text style={styles.itemMarket}>
              {market != null ? formatMoney(market) : 'No comp'}
              <Text style={styles.itemMetaInline}>
                {tier ? ` · ${tier.label}` : ''}
                {quote?.matchConfidence != null ? ` · ${Math.round(quote.matchConfidence * 100)}% match` : ''}
              </Text>
            </Text>
          )}
        </View>
        {item.status === 'working' ? (
          // Still identifying: let the user drop it (e.g. a bad photo) instead of waiting it out
          <Pressable
            onPress={onRemove}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel="Cancel this scan"
            style={({ pressed }) => [styles.badge, pressed && { opacity: 0.6 }]}>
            <Ionicons name="close" size={16} color={colors.textSecondary} />
          </Pressable>
        ) : item.bought ? (
          <View style={[styles.badge, { backgroundColor: colors.primary }]}>
            <Ionicons name="bag-check" size={14} color={colors.onPrimary} />
          </View>
        ) : v ? (
          <View style={[styles.badge, { backgroundColor: v.bg }]}>
            <Text style={[styles.badgeText, { color: v.fg }]}>{v.label}</Text>
          </View>
        ) : null}
      </Pressable>

      {item.status === 'error' && (
        <View style={{ gap: space.sm }}>
          <ErrorBanner message={item.error ?? 'Something went wrong.'} onRetry={onRetry} />
          <Button title="Remove" variant="ghost" compact onPress={onRemove} />
        </View>
      )}

      {r && r.priceError && <ErrorBanner message={r.priceError} />}

      {r && !item.bought && (
        <VerdictPanel
          marketPrice={market}
          matchConfidence={quote?.matchConfidence}
          asking={item.asking}
          onAskingChange={onAsking}
          targets={settings.targets}
          show={settings.show}
          compact={!expanded}
        />
      )}

      {item.bought && (
        <Pressable
          onPress={() =>
            router.push({ pathname: '/inventory/[id]', params: { id: String(item.bought!.id) } }, { withAnchor: true })
          }
          style={styles.boughtRow}
          accessibilityRole="button">
          <Text style={styles.boughtText}>
            Bought for {formatMoney(asking)} · in your collection
          </Text>
          <Ionicons name="chevron-forward" size={16} color={colors.primary} />
        </Pressable>
      )}

      {r && !item.bought && (
        <View style={styles.itemActions}>
          <View style={{ flex: 1 }}>
            <Button
              title={asking != null ? `Bought for ${formatMoney(asking)}` : 'Bought it'}
              icon="bag-add-outline"
              compact
              onPress={onBuy}
              loading={item.buying}
              variant={flip?.meetsTargets ? 'primary' : 'secondary'}
            />
          </View>
          <Button title={expanded ? 'Less' : 'More'} variant="ghost" compact onPress={onToggle} />
          <Pressable
            onPress={onRemove}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel="Remove scan"
            style={({ pressed }) => [styles.trash, pressed && { opacity: 0.6 }]}>
            <Ionicons name="trash-outline" size={18} color={colors.textSecondary} />
          </Pressable>
        </View>
      )}
      {item.buyError && <ErrorBanner message={item.buyError} />}

      {r && expanded && (
        <View style={{ gap: space.md }}>
          {quote && <SoldCompsPanel quote={quote} card={r.card} />}
          {quote?.compProductName && (
            <Text style={styles.itemMeta}>
              Matched to {quote.compProductName} · {quote.compSetName}
            </Text>
          )}
          {quote && !r.card.gradingCompany && <GradingPanel tiers={quote.tiers} outlook={r.card.gradeOutlook} />}
        </View>
      )}
    </Animated.View>
  );
});

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  content: { padding: space.lg, gap: space.lg, paddingBottom: 64 },
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
  session: {
    flexDirection: 'row',
    gap: space.sm,
    padding: space.md,
    borderRadius: radius.lg,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
  sessionLabel: { fontSize: 11, fontFamily: fonts.bodySemiBold, color: colors.textMuted },
  sessionValue: { fontSize: 17, fontFamily: fonts.displayMedium, color: colors.text },
  actions: { flexDirection: 'row', gap: space.md },
  terms: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    padding: space.md,
    borderRadius: radius.lg,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
  termsValue: { fontSize: 17, fontFamily: fonts.displayMedium, color: colors.text },
  stepper: { flexDirection: 'row', gap: space.sm },
  stepButton: {
    width: 40,
    height: 40,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surfaceRaised,
    borderWidth: 1,
    borderColor: colors.border,
  },
  resale: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    minHeight: 40,
    paddingHorizontal: 10,
    borderRadius: radius.pill,
    backgroundColor: colors.primarySoft,
  },
  resaleText: { fontSize: 12, fontFamily: fonts.bodySemiBold, color: colors.primary },
  item: {
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    borderWidth: 1,
    borderColor: colors.border,
    padding: space.lg,
    gap: space.md,
  },
  itemHeader: { flexDirection: 'row', gap: space.md, alignItems: 'center' },
  thumb: { width: 54, height: 76, borderRadius: radius.sm, overflow: 'hidden', backgroundColor: colors.surfaceRaised },
  itemTitle: { fontSize: 18, fontFamily: fonts.display, color: colors.text },
  itemMeta: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 18 },
  itemMetaInline: { fontSize: 13, fontFamily: fonts.body, color: colors.textMuted },
  itemMarket: { fontSize: 18, fontFamily: fonts.displayMedium, color: colors.primary, marginTop: 2 },
  badge: {
    minWidth: 44,
    height: 30,
    paddingHorizontal: 10,
    borderRadius: radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surfaceRaised,
  },
  badgeText: { fontSize: 13, fontFamily: fonts.bodyBold, color: colors.textSecondary },
  itemActions: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  trash: { width: 44, height: 44, alignItems: 'center', justifyContent: 'center' },
  boughtRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: space.md,
    borderRadius: radius.md,
    backgroundColor: colors.primarySoft,
  },
  boughtText: { fontSize: 14, fontFamily: fonts.bodySemiBold, color: colors.primary },
  ladder: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
  tier: {
    paddingVertical: 8,
    paddingHorizontal: 10,
    borderRadius: radius.md,
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
  },
  tierActive: { borderColor: colors.primary, backgroundColor: colors.primarySoft },
  tierLabel: { fontSize: 11, fontFamily: fonts.bodySemiBold, color: colors.textMuted },
  tierPrice: { fontSize: 15, fontFamily: fonts.displayMedium, color: colors.text },
  empty: { alignItems: 'center', gap: space.sm, paddingVertical: 48, paddingHorizontal: space.lg },
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
