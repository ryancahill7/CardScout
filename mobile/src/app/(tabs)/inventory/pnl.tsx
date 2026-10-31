import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation, useQuery } from '@tanstack/react-query';
import { File, Paths } from 'expo-file-system';
import { Image } from 'expo-image';
import { router } from 'expo-router';
import { Pressable, RefreshControl, ScrollView, Share, StyleSheet, Text, View } from 'react-native';

import { Button, ErrorBanner, Loading, SectionTitle, Surface } from '@/components/ui';
import { getPortfolio, getPortfolioCsv, imageSource } from '@/lib/api';
import { formatMoney, formatSignedMoney } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const monthLabel = (ym: string) => MONTHS[Number(ym.slice(5, 7)) - 1] ?? ym;
const signColor = (n: number) => (n >= 0 ? colors.primary : colors.danger);

export default function PnlScreen() {
  const portfolio = useQuery({ queryKey: ['portfolio'], queryFn: getPortfolio });

  const exportCsv = useMutation({
    mutationFn: async () => {
      const csv = await getPortfolioCsv();
      const file = new File(Paths.cache, `cardscout-${new Date().toISOString().slice(0, 10)}.csv`);
      if (file.exists) file.delete();
      file.create();
      await file.write(csv);
      await Share.share({ url: file.uri, title: 'CardScout export' });
    },
  });

  if (portfolio.isPending) return <Loading />;
  if (portfolio.isError) {
    return (
      <View style={{ padding: space.lg, flex: 1, backgroundColor: colors.background }}>
        <ErrorBanner message={portfolio.error.message} onRetry={() => portfolio.refetch()} />
      </View>
    );
  }

  const { held, sold, monthly, movers } = portfolio.data;
  const maxBar = Math.max(1, ...monthly.map((m) => Math.max(Math.abs(m.netProfit), m.spent)));
  const yearProfit = monthly.reduce((s, m) => s + m.netProfit, 0);

  return (
    <ScrollView
      style={{ backgroundColor: colors.background }}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={portfolio.isRefetching} onRefresh={() => void portfolio.refetch()} tintColor={colors.primary} />}>
      <Surface>
        <SectionTitle>Realized profit</SectionTitle>
        <Text style={[styles.hero, { color: signColor(sold.netProfit) }]}>{formatSignedMoney(sold.netProfit)}</Text>
        <Text style={styles.muted}>
          {sold.count} sold · {formatMoney(sold.revenue)} in sales
        </Text>
        <View style={styles.breakdown}>
          <Line label="Sales" value={formatMoney(sold.revenue)} />
          <Line label="eBay fees & packaging" value={`-${formatMoney(sold.fees)}`} />
          <Line label="Shipping paid" value={`-${formatMoney(sold.shipping)}`} />
          <Line label="Cost of cards sold" value={`-${formatMoney(sold.costBasis)}`} />
          <Line label="Net profit" value={formatSignedMoney(sold.netProfit)} color={signColor(sold.netProfit)} bold />
        </View>
      </Surface>

      <Surface>
        <SectionTitle>Holding</SectionTitle>
        <View style={styles.row}>
          <Big label="Market value" value={formatMoney(held.marketValue)} />
          <Big label="Cost in" value={formatMoney(held.costBasis)} />
        </View>
        <View style={styles.row}>
          <Big label="Unrealized (after fees)" value={formatSignedMoney(held.unrealizedProfit)} color={signColor(held.unrealizedProfit)} />
          <Big label="Cards" value={`${held.count}${held.pricedCount < held.count ? ` (${held.pricedCount} priced)` : ''}`} />
        </View>
      </Surface>

      <Surface>
        <SectionTitle right={<Text style={[styles.small, { color: signColor(yearProfit) }]}>12 mo {formatSignedMoney(yearProfit)}</Text>}>
          By month
        </SectionTitle>
        <View style={styles.chart}>
          {monthly.map((m) => (
            <View key={m.month} style={styles.chartCol} accessibilityLabel={`${monthLabel(m.month)}: profit ${formatMoney(m.netProfit)}, spent ${formatMoney(m.spent)}`}>
              <View style={styles.bars}>
                <View style={[styles.bar, { height: `${(m.spent / maxBar) * 100}%`, backgroundColor: colors.info, opacity: 0.5 }]} />
                <View style={[styles.bar, { height: `${(Math.abs(m.netProfit) / maxBar) * 100}%`, backgroundColor: signColor(m.netProfit) }]} />
              </View>
              <Text style={styles.chartLabel}>{monthLabel(m.month)[0]}</Text>
            </View>
          ))}
        </View>
        <View style={styles.legend}>
          <Legend color={colors.primary} label="Profit" />
          <Legend color={colors.info} label="Spent buying" />
        </View>
      </Surface>

      <Surface>
        <SectionTitle>Movers this week</SectionTitle>
        {movers.length === 0 ? (
          <Text style={styles.muted}>
            Market prices for your cards are re-checked daily. Big moves show up here and as alerts once there’s a week of history.
          </Text>
        ) : (
          movers.map((m) => {
            const thumb = imageSource(m.frontImageUrl);
            return (
              <Pressable
                key={m.cardId}
                onPress={() => router.push({ pathname: '/inventory/[id]', params: { id: String(m.cardId) } })}
                style={({ pressed }) => [styles.mover, pressed && { opacity: 0.7 }]}
                accessibilityRole="button">
                <View style={styles.moverThumb}>{thumb && <Image source={thumb} style={StyleSheet.absoluteFill} contentFit="cover" />}</View>
                <View style={{ flex: 1 }}>
                  <Text style={styles.moverName} numberOfLines={1}>
                    {m.player ?? m.title}
                  </Text>
                  <Text style={styles.small} numberOfLines={1}>
                    {formatMoney(m.from)} → {formatMoney(m.to)}
                  </Text>
                </View>
                <View style={[styles.change, { backgroundColor: m.changePct >= 0 ? colors.primarySoft : colors.dangerSoft }]}>
                  <Ionicons name={m.changePct >= 0 ? 'trending-up' : 'trending-down'} size={14} color={signColor(m.changePct)} />
                  <Text style={[styles.changeText, { color: signColor(m.changePct) }]}>{Math.abs(m.changePct).toFixed(0)}%</Text>
                </View>
              </Pressable>
            );
          })
        )}
      </Surface>

      {exportCsv.isError && <ErrorBanner message={exportCsv.error.message} />}
      <Button title="Export CSV (taxes / spreadsheet)" icon="download-outline" variant="secondary" onPress={() => exportCsv.mutate()} loading={exportCsv.isPending} />
      <Text style={styles.footnote}>
        Profit assumes eBay sales (13.25% + $0.40 per order + $0.75 packaging) and includes grading and the shipping you paid.
      </Text>
    </ScrollView>
  );
}

function Line({ label, value, color, bold }: { label: string; value: string; color?: string; bold?: boolean }) {
  return (
    <View style={styles.line}>
      <Text style={[styles.lineLabel, bold && { color: colors.text, fontFamily: fonts.bodySemiBold }]}>{label}</Text>
      <Text style={[styles.lineValue, color && { color }, bold && { fontFamily: fonts.display }]}>{value}</Text>
    </View>
  );
}

function Big({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <View style={{ flex: 1, gap: 2 }}>
      <Text style={styles.small}>{label}</Text>
      <Text style={[styles.bigValue, color && { color }]}>{value}</Text>
    </View>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
      <View style={{ width: 10, height: 10, borderRadius: 3, backgroundColor: color }} />
      <Text style={styles.small}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { padding: space.lg, gap: space.lg, paddingBottom: 64 },
  hero: { fontSize: 44, fontFamily: fonts.display, letterSpacing: -1.2 },
  muted: { fontSize: 14, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 20 },
  small: { fontSize: 12, fontFamily: fonts.bodyMedium, color: colors.textMuted },
  footnote: { fontSize: 12, fontFamily: fonts.body, color: colors.textMuted, lineHeight: 17, textAlign: 'center' },
  breakdown: { gap: 6, paddingTop: space.md, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: colors.border },
  line: { flexDirection: 'row', justifyContent: 'space-between' },
  lineLabel: { fontSize: 14, fontFamily: fonts.body, color: colors.textSecondary },
  lineValue: { fontSize: 15, fontFamily: fonts.displayMedium, color: colors.text },
  row: { flexDirection: 'row', gap: space.lg },
  bigValue: { fontSize: 20, fontFamily: fonts.displayMedium, color: colors.text },
  chart: { flexDirection: 'row', height: 120, gap: 4, alignItems: 'flex-end' },
  chartCol: { flex: 1, height: '100%', alignItems: 'center', gap: 4 },
  bars: { flex: 1, width: '100%', flexDirection: 'row', alignItems: 'flex-end', gap: 1 },
  bar: { flex: 1, borderTopLeftRadius: 2, borderTopRightRadius: 2, minHeight: 1 },
  chartLabel: { fontSize: 10, fontFamily: fonts.bodySemiBold, color: colors.textMuted },
  legend: { flexDirection: 'row', gap: space.lg },
  mover: { flexDirection: 'row', alignItems: 'center', gap: space.md, minHeight: 52 },
  moverThumb: { width: 34, height: 46, borderRadius: 4, overflow: 'hidden', backgroundColor: colors.surfaceRaised },
  moverName: { fontSize: 15, fontFamily: fonts.bodySemiBold, color: colors.text },
  change: { flexDirection: 'row', alignItems: 'center', gap: 4, borderRadius: radius.pill, paddingHorizontal: 10, paddingVertical: 4 },
  changeText: { fontSize: 13, fontFamily: fonts.bodyBold },
});
