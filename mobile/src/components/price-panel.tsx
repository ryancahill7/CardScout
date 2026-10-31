import Ionicons from '@expo/vector-icons/Ionicons';
import * as Haptics from 'expo-haptics';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import Animated, { FadeIn, FadeInDown } from 'react-native-reanimated';

import { CompAlternative, PriceQuote } from '@/lib/api';
import { formatMoney, formatSignedMoney, netProceeds } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';
import { ErrorBanner, HoloFrame } from './ui';

// Market value hero: comp price for the card's grade, what it nets after eBay fees,
// the full grade ladder, and a way to fix a wrong match.
export function PricePanel({
  quote,
  loading,
  error,
  stale,
  costBasis,
  activePrice,
  onReprice,
  onPickAlternative,
  onUsePrice,
}: {
  quote: PriceQuote | null;
  loading?: boolean;
  error?: string | null;
  // The card details changed since this quote was made
  stale?: boolean;
  costBasis?: number | null;
  // The price currently in the form, highlighted in the ladder
  activePrice?: number | null;
  // Re-runs the search from the current details (drops any pinned comp)
  onReprice: () => void;
  onPickAlternative: (productId: string) => void;
  onUsePrice: (price: number) => void;
}) {
  const [showAlternatives, setShowAlternatives] = useState(false);
  const hasPrice = quote?.suggestedPrice != null;
  const selectedTier = quote?.tiers.find((t) => t.key === quote.selectedTier);
  const valuePrice = activePrice ?? quote?.suggestedPrice ?? null;
  const net = valuePrice != null ? netProceeds(valuePrice) : null;
  const profit = net != null && costBasis != null ? net - costBasis : null;
  const alternatives = (quote?.alternatives ?? []).filter((a) => a.productId !== quote?.compProductId);

  function pick(alt: CompAlternative) {
    void Haptics.selectionAsync();
    setShowAlternatives(false);
    onPickAlternative(alt.productId);
  }

  return (
    <HoloFrame>
      <View style={styles.inner}>
        <View style={styles.headerRow}>
          <Text style={styles.eyebrow}>
            Market value{selectedTier ? ` · ${selectedTier.label}` : ''}
          </Text>
          {loading ? (
            <ActivityIndicator size="small" color={colors.primary} />
          ) : (
            quote && hasPrice && <MatchBadge quote={quote} />
          )}
        </View>

        {hasPrice ? (
          <Animated.View entering={FadeIn.duration(250)} key={`${quote!.compProductId}-${valuePrice}`}>
            <Text style={styles.price} accessibilityLabel={`Market value ${formatMoney(valuePrice)}`}>
              {formatMoney(valuePrice)}
            </Text>
            <View style={styles.netRow}>
              <Text style={styles.netText}>
                Nets <Text style={styles.netStrong}>{formatMoney(net)}</Text> after eBay fees
              </Text>
              {profit != null && (
                <View style={[styles.profitPill, { backgroundColor: profit >= 0 ? colors.primarySoft : colors.dangerSoft }]}>
                  <Text style={[styles.profitText, { color: profit >= 0 ? colors.primary : colors.danger }]}>
                    {formatSignedMoney(profit)}
                  </Text>
                </View>
              )}
            </View>
          </Animated.View>
        ) : (
          <View style={{ gap: 4 }}>
            <Text style={styles.noPrice}>{loading ? 'Pricing…' : 'No confident comp'}</Text>
            {!loading && (
              <Text style={styles.muted}>
                {alternatives.length > 0
                  ? 'Pick the right card from the closest matches below, or fix the details and re-price.'
                  : 'Check the player, year and set below, then re-price. You can also enter a price yourself.'}
              </Text>
            )}
          </View>
        )}

        {error && <ErrorBanner message={error} onRetry={onReprice} />}

        {hasPrice && (
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            contentContainerStyle={styles.ladder}
            style={styles.ladderScroll}>
            {/* Grades with no sales data are hidden, except the card's own grade so a gap is visible */}
            {quote!.tiers.filter((t) => t.price != null || t.key === quote!.selectedTier).map((tier) => {
              const isGrade = tier.key === quote!.selectedTier;
              const isActive = tier.price != null && tier.price === valuePrice;
              return (
                <Pressable
                  key={tier.key}
                  disabled={tier.price == null}
                  onPress={() => {
                    void Haptics.selectionAsync();
                    onUsePrice(tier.price!);
                  }}
                  accessibilityRole="button"
                  accessibilityLabel={`Use ${tier.label} price ${formatMoney(tier.price)}`}
                  style={({ pressed }) => [
                    styles.tier,
                    isActive && styles.tierActive,
                    tier.price == null && { opacity: 0.4 },
                    pressed && { opacity: 0.7 },
                  ]}>
                  <Text style={[styles.tierLabel, isGrade && { color: colors.primary }]}>
                    {tier.label}
                    {isGrade ? ' •' : ''}
                  </Text>
                  <Text style={styles.tierPrice}>{formatMoney(tier.price, { cents: (tier.price ?? 0) < 100 })}</Text>
                </Pressable>
              );
            })}
          </ScrollView>
        )}

        {hasPrice && quote!.compProductName && (
          <Pressable
            disabled={!quote!.compUrl}
            onPress={() => quote!.compUrl && WebBrowser.openBrowserAsync(quote!.compUrl)}
            accessibilityRole="link"
            accessibilityLabel={`Open ${quote!.compProductName} on SportsCardsPro`}
            style={({ pressed }) => [styles.compRow, pressed && { opacity: 0.7 }]}>
            <View style={{ flex: 1 }}>
              <Text style={styles.compName} numberOfLines={1}>
                {quote!.compProductName}
              </Text>
              <Text style={styles.muted} numberOfLines={1}>
                {[quote!.compSetName, quote!.pricedAt && `updated ${relativeTime(quote!.pricedAt)}`]
                  .filter(Boolean)
                  .join(' · ')}
              </Text>
            </View>
            <Ionicons name="open-outline" size={18} color={colors.textSecondary} />
          </Pressable>
        )}

        <View style={styles.actions}>
          {(stale || !hasPrice || quote?.isManualPick) && (
            <ActionChip
              icon="refresh"
              label={stale ? 'Details changed · Re-price' : quote?.isManualPick && hasPrice ? 'Re-match' : 'Re-price'}
              highlight={stale}
              onPress={onReprice}
              disabled={loading}
            />
          )}
          {alternatives.length > 0 && hasPrice && (
            <ActionChip
              icon={showAlternatives ? 'chevron-up' : 'swap-horizontal'}
              label={showAlternatives ? 'Hide matches' : 'Wrong card?'}
              onPress={() => setShowAlternatives(!showAlternatives)}
              disabled={loading}
            />
          )}
        </View>

        {(showAlternatives || (!hasPrice && !loading)) && alternatives.length > 0 && (
          <Animated.View entering={FadeInDown.duration(220)} style={styles.altList}>
            <Text style={styles.eyebrow}>Closest matches</Text>
            {alternatives.map((alt) => (
              <Pressable
                key={alt.productId}
                onPress={() => pick(alt)}
                disabled={loading}
                accessibilityRole="button"
                accessibilityLabel={`Use ${alt.productName}, ${alt.setName}`}
                style={({ pressed }) => [styles.altRow, pressed && { backgroundColor: colors.surfacePressed }]}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.altName} numberOfLines={1}>
                    {alt.productName}
                  </Text>
                  <Text style={styles.muted} numberOfLines={1}>
                    {alt.setName}
                  </Text>
                </View>
                <Text style={styles.altScore}>{Math.round(alt.score * 100)}%</Text>
                <Ionicons name="chevron-forward" size={16} color={colors.textMuted} />
              </Pressable>
            ))}
          </Animated.View>
        )}
      </View>
    </HoloFrame>
  );
}

function MatchBadge({ quote }: { quote: PriceQuote }) {
  if (quote.isManualPick) {
    return (
      <View style={[styles.badge, { backgroundColor: colors.infoSoft }]}>
        <Ionicons name="hand-left" size={12} color={colors.info} />
        <Text style={[styles.badgeText, { color: colors.info }]}>Pinned comp</Text>
      </View>
    );
  }
  const pct = Math.round((quote.matchConfidence ?? 0) * 100);
  const color = pct >= 80 ? colors.primary : pct >= 65 ? colors.warning : colors.danger;
  return (
    <View style={[styles.badge, { backgroundColor: `${color}1F` }]}>
      <View style={[styles.badgeDot, { backgroundColor: color }]} />
      <Text style={[styles.badgeText, { color }]}>{pct}% match</Text>
    </View>
  );
}

function ActionChip({
  icon,
  label,
  onPress,
  highlight,
  disabled,
}: {
  icon: React.ComponentProps<typeof Ionicons>['name'];
  label: string;
  onPress: () => void;
  highlight?: boolean;
  disabled?: boolean;
}) {
  const fg = highlight ? colors.onPrimary : colors.text;
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={label}
      style={({ pressed }) => [
        styles.actionChip,
        highlight && { backgroundColor: colors.primary, borderColor: colors.primary },
        (pressed || disabled) && { opacity: 0.6 },
      ]}>
      <Ionicons name={icon} size={15} color={fg} />
      <Text style={[styles.actionChipText, { color: fg }]}>{label}</Text>
    </Pressable>
  );
}

function relativeTime(iso: string) {
  const minutes = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60_000));
  if (minutes < 2) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

const styles = StyleSheet.create({
  inner: { padding: space.lg + 2, gap: space.md },
  headerRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', minHeight: 24 },
  eyebrow: {
    fontSize: 12,
    fontFamily: fonts.bodySemiBold,
    color: colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 1.2,
  },
  price: { fontSize: 48, fontFamily: fonts.display, color: colors.text, letterSpacing: -1.5, lineHeight: 54 },
  netRow: { flexDirection: 'row', alignItems: 'center', gap: space.sm, flexWrap: 'wrap' },
  netText: { fontSize: 15, fontFamily: fonts.body, color: colors.textSecondary },
  netStrong: { fontFamily: fonts.bodySemiBold, color: colors.text },
  profitPill: { borderRadius: radius.pill, paddingHorizontal: 10, paddingVertical: 3 },
  profitText: { fontSize: 13, fontFamily: fonts.displayMedium },
  noPrice: { fontSize: 26, fontFamily: fonts.display, color: colors.text, letterSpacing: -0.5 },
  muted: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 18 },
  ladderScroll: { marginHorizontal: -(space.lg + 2) },
  ladder: { gap: space.sm, paddingHorizontal: space.lg + 2 },
  tier: {
    minWidth: 78,
    paddingVertical: 10,
    paddingHorizontal: 12,
    borderRadius: radius.md,
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    gap: 2,
  },
  tierActive: { borderColor: colors.primary, backgroundColor: colors.primarySoft },
  tierLabel: { fontSize: 11, fontFamily: fonts.bodySemiBold, color: colors.textMuted, letterSpacing: 0.3 },
  tierPrice: { fontSize: 16, fontFamily: fonts.displayMedium, color: colors.text },
  compRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    paddingTop: space.md,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.border,
  },
  compName: { fontSize: 14, fontFamily: fonts.bodySemiBold, color: colors.text },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
  actionChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    minHeight: 44,
    paddingHorizontal: 14,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: colors.borderStrong,
  },
  actionChipText: { fontSize: 14, fontFamily: fonts.bodySemiBold },
  badge: { flexDirection: 'row', alignItems: 'center', gap: 6, borderRadius: radius.pill, paddingHorizontal: 10, paddingVertical: 4 },
  badgeDot: { width: 6, height: 6, borderRadius: 3 },
  badgeText: { fontSize: 12, fontFamily: fonts.bodySemiBold },
  altList: { gap: 2 },
  altRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    minHeight: 52,
    paddingHorizontal: space.sm,
    marginHorizontal: -space.sm,
    borderRadius: radius.md,
  },
  altName: { fontSize: 14, fontFamily: fonts.bodyMedium, color: colors.text },
  altScore: { fontSize: 13, fontFamily: fonts.displayMedium, color: colors.textSecondary },
});
