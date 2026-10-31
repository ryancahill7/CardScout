import Ionicons from '@expo/vector-icons/Ionicons';
import * as Haptics from 'expo-haptics';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { WebView } from 'react-native-webview';

import { PriceQuote } from '@/lib/api';
import { formatMoney } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';
import { certUrl } from './card-form';
import { SectionTitle, Surface } from './ui';

// The card fields the sold searches need (from an identification, a form, or a saved card)
export interface SoldCompsCard {
  player: string | null;
  year: number | null;
  manufacturer: string | null;
  set: string | null;
  cardNumber: string | null;
  parallel: string | null;
  isAutograph: boolean;
  gradingCompany: string | null;
  grade: number | null;
  certNumber?: string | null;
}

type Source = 'ebay' | 'scp';

// eBay's own sold-listings search for the card, newest sales first
export function ebaySoldUrl(card: SoldCompsCard) {
  const q = [
    card.year,
    card.manufacturer,
    card.set,
    card.player,
    card.parallel,
    card.cardNumber && `#${card.cardNumber}`,
    card.isAutograph && 'auto',
    card.gradingCompany && card.grade != null && `${card.gradingCompany} ${card.grade}`,
  ]
    .filter(Boolean)
    .join(' ');
  return `https://www.ebay.com/sch/i.html?_nkw=${encodeURIComponent(q)}&LH_Sold=1&LH_Complete=1&_sop=13&rt=nc`;
}

// SportsCardsPro product page (it lists the individual sales behind each grade's price).
// URLs are /game/<set slug>/<card slug>; falls back to the search page if names are missing.
function sportsCardsProUrl(quote: PriceQuote) {
  const slug = (s: string) =>
    s
      .toLowerCase()
      .replace(/[#[\]().,'’/]/g, '')
      .replace(/&/g, 'and')
      .trim()
      .replace(/\s+/g, '-');
  if (quote.compProductName && quote.compSetName)
    return `https://www.sportscardspro.com/game/${slug(quote.compSetName)}/${slug(quote.compProductName)}`;
  return quote.compUrl;
}

// What this card has actually sold for: the comp for its grade (and the grades around it)
// from SportsCardsPro's recorded sales, plus the recent individual sales shown in the app.
export function SoldCompsPanel({ quote, card }: { quote: PriceQuote; card: SoldCompsCard }) {
  const [source, setSource] = useState<Source | null>(null);
  const priced = quote.tiers.filter((t) => t.price != null);
  const index = priced.findIndex((t) => t.key === quote.selectedTier);
  // The card's grade plus its neighbors, so you can see how much a grade up or down moves it
  const around = index < 0 ? priced.slice(0, 3) : priced.slice(Math.max(0, index - 2), index + 3);
  const selected = quote.tiers.find((t) => t.key === quote.selectedTier);
  const verify = certUrl(card.gradingCompany, card.certNumber);
  const scpUrl = sportsCardsProUrl(quote);
  const urls: Record<Source, string | null> = { ebay: ebaySoldUrl(card), scp: scpUrl };

  return (
    <Surface>
      <SectionTitle
        right={
          quote.salesVolume ? <Text style={styles.small}>{quote.salesVolume.toLocaleString()} sales tracked</Text> : undefined
        }>
        Sold comps
      </SectionTitle>

      {selected?.price != null ? (
        <View style={styles.hero}>
          <Text style={styles.heroLabel}>{selected.label} sells for</Text>
          <Text style={styles.heroValue}>{formatMoney(selected.price)}</Text>
        </View>
      ) : (
        <Text style={styles.muted}>No sales recorded at {selected?.label ?? 'this grade'}. Check the recent sales below.</Text>
      )}

      {around.length > 0 && (
        <View style={styles.ladder}>
          {around.map((t) => (
            <View key={t.key} style={[styles.tier, t.key === quote.selectedTier && styles.tierActive]}>
              <Text style={[styles.tierLabel, t.key === quote.selectedTier && { color: colors.primary }]}>{t.label}</Text>
              <Text style={styles.tierPrice}>{formatMoney(t.price, { cents: (t.price ?? 0) < 100 })}</Text>
            </View>
          ))}
        </View>
      )}

      <View style={{ gap: space.sm }}>
        <Text style={styles.label}>Recent sales</Text>
        <View style={styles.segmented} accessibilityRole="tablist">
          {(
            [
              ['ebay', 'eBay sold'],
              ['scp', 'Price history'],
            ] as const
          ).map(([key, label]) => {
            const active = source === key;
            return (
              <Pressable
                key={key}
                disabled={!urls[key]}
                onPress={() => {
                  void Haptics.selectionAsync();
                  setSource(active ? null : key);
                }}
                accessibilityRole="tab"
                accessibilityState={{ selected: active }}
                style={[styles.segment, active && styles.segmentSelected, !urls[key] && { opacity: 0.4 }]}>
                <Text style={[styles.segmentText, active && { color: colors.text }]}>{label}</Text>
              </Pressable>
            );
          })}
        </View>
        {source && urls[source] ? (
          <InlineSales key={source} url={urls[source]!} onOpenFull={() => WebBrowser.openBrowserAsync(urls[source]!)} />
        ) : (
          <Text style={styles.small}>Tap a source to see the latest sales of this card right here.</Text>
        )}
      </View>

      <View style={styles.links}>
        <LinkButton icon="open-outline" label="eBay sold" onPress={() => WebBrowser.openBrowserAsync(urls.ebay!)} />
        {scpUrl && <LinkButton icon="open-outline" label="SportsCardsPro" onPress={() => WebBrowser.openBrowserAsync(scpUrl)} />}
        {verify && (
          <LinkButton icon="shield-checkmark-outline" label={`Verify ${card.gradingCompany}`} onPress={() => WebBrowser.openBrowserAsync(verify)} />
        )}
      </View>
      {card.certNumber && (
        <Text style={styles.small}>
          {card.gradingCompany} cert #{card.certNumber}
        </Text>
      )}
    </Surface>
  );
}

// The sales page, embedded in the panel. It scrolls on its own; "Full screen" opens it larger.
function InlineSales({ url, onOpenFull }: { url: string; onOpenFull: () => void }) {
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);

  if (failed) {
    return (
      <View style={styles.webFallback}>
        <Text style={styles.muted}>Couldn’t load the sales page.</Text>
        <View style={{ flexDirection: 'row', gap: space.sm }}>
          <LinkButton
            icon="refresh"
            label="Retry"
            onPress={() => {
              setFailed(false);
              setLoading(true);
              setAttempt((a) => a + 1);
            }}
          />
          <LinkButton icon="open-outline" label="Open" onPress={onOpenFull} />
        </View>
      </View>
    );
  }

  return (
    <View style={styles.webWrap}>
      <WebView
        key={attempt}
        source={{ uri: url }}
        style={styles.web}
        nestedScrollEnabled
        setSupportMultipleWindows={false}
        allowsBackForwardNavigationGestures
        onLoadEnd={() => setLoading(false)}
        onError={() => setFailed(true)}
        onHttpError={(e) => e.nativeEvent.statusCode >= 500 && setFailed(true)}
      />
      {loading && (
        <View style={styles.webLoading} pointerEvents="none">
          <ActivityIndicator color={colors.primary} />
          <Text style={styles.small}>Loading recent sales…</Text>
        </View>
      )}
      <Pressable onPress={onOpenFull} accessibilityRole="button" accessibilityLabel="Open sales full screen" style={styles.fullButton}>
        <Ionicons name="expand" size={14} color="#FFFFFF" />
        <Text style={styles.fullText}>Full screen</Text>
      </Pressable>
    </View>
  );
}

function LinkButton({ icon, label, onPress }: { icon: React.ComponentProps<typeof Ionicons>['name']; label: string; onPress: () => void }) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="link"
      accessibilityLabel={label}
      style={({ pressed }) => [styles.link, pressed && { opacity: 0.7 }]}>
      <Ionicons name={icon} size={15} color={colors.primary} />
      <Text style={styles.linkText}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  hero: { flexDirection: 'row', alignItems: 'baseline', justifyContent: 'space-between' },
  heroLabel: { fontSize: 14, fontFamily: fonts.bodyMedium, color: colors.textSecondary },
  heroValue: { fontSize: 28, fontFamily: fonts.display, color: colors.text },
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
  segment: { flex: 1, minHeight: 38, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center' },
  segmentSelected: { backgroundColor: colors.surfacePressed },
  segmentText: { color: colors.textMuted, fontFamily: fonts.bodySemiBold, fontSize: 14 },
  webWrap: { height: 520, borderRadius: radius.md, overflow: 'hidden', backgroundColor: '#FFFFFF' },
  web: { flex: 1 },
  webLoading: {
    ...StyleSheet.absoluteFill,
    alignItems: 'center',
    justifyContent: 'center',
    gap: space.sm,
    backgroundColor: colors.surfaceRaised,
  },
  webFallback: { gap: space.sm, padding: space.md, borderRadius: radius.md, backgroundColor: colors.surfaceRaised },
  fullButton: {
    position: 'absolute',
    right: space.sm,
    bottom: space.sm,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: radius.pill,
    backgroundColor: colors.overlay,
  },
  fullText: { color: '#FFFFFF', fontFamily: fonts.bodySemiBold, fontSize: 12 },
  links: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
  link: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    minHeight: 40,
    paddingHorizontal: 12,
    borderRadius: radius.pill,
    backgroundColor: colors.primarySoft,
  },
  linkText: { fontSize: 13, fontFamily: fonts.bodySemiBold, color: colors.primary },
  muted: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 18 },
  small: { fontSize: 12, fontFamily: fonts.bodyMedium, color: colors.textMuted },
});
