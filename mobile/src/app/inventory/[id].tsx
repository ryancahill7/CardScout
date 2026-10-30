import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as Haptics from 'expo-haptics';
import { Image } from 'expo-image';
import { useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import Animated, { FlipInYLeft } from 'react-native-reanimated';

import { CardForm, CardFormValues, fromFormValues, parseNumber, toFormValues } from '@/components/card-form';
import { PricePanel } from '@/components/price-panel';
import { Button, ErrorBanner, Field, HoloFrame, Loading, SectionTitle, StatusPicker, StatusPill, Surface } from '@/components/ui';
import { Card, CardStatus, getCard, imageSource, updateCard } from '@/lib/api';
import { formatMoney, formatSignedMoney, netProceeds } from '@/lib/money';
import { colors, fonts, radius, space } from '@/lib/theme';
import { useCardPricing } from '@/lib/use-pricing';

interface SaleValues {
  status: CardStatus;
  listedPrice: string;
  soldPrice: string;
}

const money = (v: number | null) => (v == null ? '' : v.toFixed(2));

export default function CardDetailScreen() {
  const queryClient = useQueryClient();
  const { id } = useLocalSearchParams<{ id: string }>();
  const cardId = Number(id);

  const card = useQuery({ queryKey: ['card', cardId], queryFn: () => getCard(cardId), enabled: Number.isFinite(cardId) });

  const [form, setForm] = useState<CardFormValues | null>(null);
  const [sale, setSale] = useState<SaleValues | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<number | null>(null);
  const [side, setSide] = useState<'front' | 'back'>('front');

  // Saves a price found by the automatic check without touching the rest of the form
  const backfill = useMutation({
    mutationFn: (patch: Parameters<typeof updateCard>[1]) => updateCard(cardId, patch),
    onSuccess: (updated) => {
      queryClient.setQueryData(['card', cardId], updated);
      queryClient.invalidateQueries({ queryKey: ['cards'] });
    },
  });

  // An explicit re-price or comp pick replaces the price in the form. The automatic check
  // on open only shows the live comp, unless the card was saved without a price (e.g.
  // SportsCardsPro was down when it was scanned): then the price is saved right away so
  // the collection total includes it. A price the user set is never overwritten.
  const autoPricing = useRef(false);
  const pricing = useCardPricing(form, (quote) => {
    const auto = autoPricing.current;
    autoPricing.current = false;
    if (quote.suggestedPrice == null) return;
    if (auto && card.data?.suggestedPrice != null) return;

    setForm((f) => f && { ...f, suggestedPrice: quote.suggestedPrice!.toFixed(2) });
    if (auto) backfill.mutate({ suggestedPrice: quote.suggestedPrice, compProductId: quote.compProductId });
  });

  function loadForm(c: Card) {
    setForm(toFormValues(c));
    setSale({ status: c.status, listedPrice: money(c.listedPrice), soldPrice: money(c.soldPrice) });
  }

  // Initialize the editable form once the card loads (and only then, so edits aren't clobbered by refetches).
  // Done during render rather than in an effect, so the first paint already has the form.
  if (card.data && !form) loadForm(card.data);

  // Fetch a live price once the form is ready, pinned to the comp saved with the card
  const pricedOnce = useRef(false);
  useEffect(() => {
    if (form && card.data && !pricedOnce.current) {
      pricedOnce.current = true;
      autoPricing.current = true;
      pricing.reprice(card.data.compProductId);
    }
  }, [form, card.data, pricing]);

  const save = useMutation({
    mutationFn: (patch: Parameters<typeof updateCard>[1]) => updateCard(cardId, patch),
    onSuccess: (updated) => {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.setQueryData(['card', cardId], updated);
      queryClient.invalidateQueries({ queryKey: ['cards'] });
      loadForm(updated);
      setSavedAt(Date.now());
    },
  });

  function handleSave() {
    if (!form || !sale) return;
    const parsed = fromFormValues(form);
    if (!parsed.ok) {
      setFormError(parsed.error);
      return;
    }
    const listedPrice = parseNumber(sale.listedPrice);
    const soldPrice = parseNumber(sale.soldPrice);
    if (listedPrice === undefined || soldPrice === undefined) {
      setFormError('Prices must be numbers.');
      return;
    }
    setFormError(null);
    save.mutate({
      ...parsed.fields,
      status: sale.status,
      listedPrice,
      soldPrice,
      compProductId: pricing.quote?.compProductId ?? card.data?.compProductId ?? null,
    });
  }

  if (card.isPending) return <Loading />;
  if (card.isError) {
    return (
      <View style={{ padding: space.lg, backgroundColor: colors.background, flex: 1 }}>
        <ErrorBanner message={card.error.message} onRetry={() => card.refetch()} />
      </View>
    );
  }
  if (!form || !sale) return <Loading />;

  const images = { front: imageSource(card.data.frontImageUrl), back: imageSource(card.data.backImageUrl) };
  const shown = images[side];
  const costBasis = parseNumber(form.costBasis) ?? null;
  const soldPrice = parseNumber(sale.soldPrice) ?? null;
  const subtitle = [form.year, form.manufacturer, form.set, form.cardNumber && `#${form.cardNumber}`]
    .filter(Boolean)
    .join(' · ');

  return (
    <KeyboardAvoidingView
      style={{ flex: 1, backgroundColor: colors.background }}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      keyboardVerticalOffset={100}>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <Pressable
          onPress={() => {
            void Haptics.selectionAsync();
            setSide(side === 'front' ? 'back' : 'front');
          }}
          accessibilityRole="button"
          accessibilityLabel={`Showing ${side}. Tap to flip the card.`}
          style={styles.heroWrap}>
          <HoloFrame borderRadius={radius.xl} width={2}>
            <Animated.View key={side} entering={FlipInYLeft.duration(380)} style={styles.hero}>
              {shown ? (
                <Image source={shown} style={StyleSheet.absoluteFill} contentFit="cover" transition={150} />
              ) : (
                <Text style={styles.muted}>No photo</Text>
              )}
            </Animated.View>
          </HoloFrame>
          <View style={styles.flipHint}>
            <Ionicons name="sync" size={13} color={colors.textSecondary} />
            <Text style={styles.flipText}>{side === 'front' ? 'Front' : 'Back'} · tap to flip</Text>
          </View>
        </Pressable>

        <View style={{ gap: space.sm }}>
          <StatusPill status={card.data.status} />
          <Text style={styles.player}>{form.player || card.data.ebayTitle}</Text>
          {subtitle.length > 0 && <Text style={styles.subtitle}>{subtitle}</Text>}
        </View>

        <PricePanel
          quote={pricing.quote}
          loading={pricing.isPending}
          error={pricing.error}
          stale={pricing.stale}
          activePrice={parseNumber(form.suggestedPrice) ?? null}
          costBasis={costBasis}
          onReprice={() => pricing.reprice()}
          onPickAlternative={(productId) => pricing.reprice(productId)}
          onUsePrice={(p) => setForm({ ...form, suggestedPrice: p.toFixed(2) })}
        />

        <Surface>
          <SectionTitle>Status</SectionTitle>
          <StatusPicker value={sale.status} onChange={(s) => s && setSale({ ...sale, status: s })} />
          <View style={{ flexDirection: 'row', gap: space.md }}>
            <View style={{ flex: 1 }}>
              <Field
                label="Listed at"
                value={sale.listedPrice}
                onChangeText={(v) => setSale({ ...sale, listedPrice: v })}
                keyboardType="decimal-pad"
                placeholder="—"
              />
            </View>
            <View style={{ flex: 1 }}>
              <Field
                label="Sold for"
                value={sale.soldPrice}
                onChangeText={(v) => setSale({ ...sale, soldPrice: v })}
                keyboardType="decimal-pad"
                placeholder="—"
              />
            </View>
          </View>
          {soldPrice != null && (
            <View style={styles.saleResult}>
              <Text style={styles.muted}>
                Netted {formatMoney(netProceeds(soldPrice))} after fees
                {card.data.soldAt ? ` · ${new Date(card.data.soldAt).toLocaleDateString()}` : ''}
              </Text>
              {costBasis != null && (
                <Text
                  style={[
                    styles.saleProfit,
                    { color: netProceeds(soldPrice) - costBasis >= 0 ? colors.primary : colors.danger },
                  ]}>
                  {formatSignedMoney(netProceeds(soldPrice) - costBasis)}
                </Text>
              )}
            </View>
          )}
        </Surface>

        <CardForm values={form} onChange={setForm} />

        {formError && <ErrorBanner message={formError} />}
        {save.isError && <ErrorBanner message={save.error.message} />}
        {savedAt && !save.isPending && !save.isError && (
          <View style={styles.savedRow}>
            <Ionicons name="checkmark-circle" size={18} color={colors.primary} />
            <Text style={styles.savedText}>Saved</Text>
          </View>
        )}
        <Button title="Save changes" icon="checkmark" onPress={handleSave} loading={save.isPending} />
        <Button
          title="Discard changes"
          variant="ghost"
          onPress={() => {
            loadForm(card.data);
            setFormError(null);
          }}
          disabled={save.isPending}
        />
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  content: { padding: space.lg, gap: space.lg, paddingBottom: 48 },
  heroWrap: { alignSelf: 'center', width: '68%', gap: space.sm },
  hero: {
    aspectRatio: 2.5 / 3.5,
    backgroundColor: colors.surfaceRaised,
    alignItems: 'center',
    justifyContent: 'center',
  },
  flipHint: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 6 },
  flipText: { color: colors.textSecondary, fontFamily: fonts.bodyMedium, fontSize: 12 },
  player: { fontSize: 30, fontFamily: fonts.display, color: colors.text, letterSpacing: -0.6, lineHeight: 34 },
  subtitle: { fontSize: 15, fontFamily: fonts.body, color: colors.textSecondary },
  muted: { color: colors.textSecondary, fontFamily: fonts.body, fontSize: 13 },
  saleResult: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: space.md },
  saleProfit: { fontFamily: fonts.display, fontSize: 18 },
  savedRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 6 },
  savedText: { color: colors.primary, fontFamily: fonts.bodySemiBold, fontSize: 15 },
});
