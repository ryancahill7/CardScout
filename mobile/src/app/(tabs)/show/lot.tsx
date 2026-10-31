import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation } from '@tanstack/react-query';
import * as Haptics from 'expo-haptics';
import { Image } from 'expo-image';
import { router, Stack } from 'expo-router';
import { useEffect, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import Animated, { FadeInDown } from 'react-native-reanimated';

import { CapturedPhoto, CardCamera } from '@/components/card-camera';
import { Button, ErrorBanner, Field, HoloFrame, Loading, SectionTitle, Surface } from '@/components/ui';
import { VerdictResult } from '@/components/verdict-panel';
import { analyzeLot, LotAnalysis } from '@/lib/api';
import { prepareForUpload } from '@/lib/images';
import { formatMoney } from '@/lib/money';
import { useSettings } from '@/lib/settings';
import { cardMetaParts } from '@/lib/card-meta';
import { colors, fonts, radius, space } from '@/lib/theme';

const STEPS = ['Finding every card…', 'Reading names & sets…', 'Pricing each card…', 'Adding it up…'];

const parseMoney = (s: string) => {
  const n = Number(s.replace(/[$,]/g, '').trim());
  return s.trim() !== '' && Number.isFinite(n) ? n : null;
};

// One photo of a lot, binder page or dealer case: Claude finds each card, each is priced,
// and the total is judged against the asking price (fees counted per card).
export default function LotScreen() {
  const settings = useSettings();
  const [photo, setPhoto] = useState<string | null>(null);
  const [capturing, setCapturing] = useState(true);
  const [preparing, setPreparing] = useState(false);
  const [asking, setAsking] = useState('');
  const [shipping, setShipping] = useState('');

  const analyze = useMutation({
    mutationFn: () =>
      analyzeLot(photo!, {
        askingPrice: parseMoney(asking),
        shipping: parseMoney(shipping),
        minProfit: settings.targets.minProfit,
        minProfitPct: settings.targets.minProfitPct,
      }),
    onSuccess: () => void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success),
    onError: () => void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error),
  });

  async function handleCaptured(p: CapturedPhoto) {
    setCapturing(false);
    setPreparing(true);
    try {
      // Lots need more pixels than a single card so small cards stay readable
      setPhoto(await prepareForUpload(p.uri, p.width, p.height, 2400));
      analyze.reset();
    } finally {
      setPreparing(false);
    }
  }

  if (capturing) {
    return (
      <>
        {/* The camera has its own close button; the stack header would just eat screen space */}
        <Stack.Screen options={{ headerShown: false }} />
        <CardCamera
          side="front"
          mode="lot"
          onCaptured={handleCaptured}
          onCancel={photo ? () => setCapturing(false) : () => router.back()}
        />
      </>
    );
  }
  if (preparing) return <Loading label="Processing photo…" />;

  return (
    <>
    <Stack.Screen options={{ headerShown: true }} />
    <KeyboardAvoidingView style={{ flex: 1, backgroundColor: colors.background }} behavior={Platform.OS === 'ios' ? 'padding' : undefined} keyboardVerticalOffset={100}>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" keyboardDismissMode="interactive">
        {photo && (
          <Pressable onPress={() => setCapturing(true)} disabled={analyze.isPending} accessibilityRole="button" accessibilityLabel="Retake lot photo">
            <HoloFrame borderRadius={radius.lg}>
              <View style={styles.photo}>
                <Image source={{ uri: photo }} style={StyleSheet.absoluteFill} contentFit="cover" />
                <View style={styles.photoTag}>
                  <Ionicons name="refresh" size={12} color="#FFFFFF" />
                  <Text style={styles.photoTagText}>Retake</Text>
                </View>
              </View>
            </HoloFrame>
          </Pressable>
        )}

        <Surface>
          <View style={{ flexDirection: 'row', gap: space.md }}>
            <View style={{ flex: 1 }}>
              <Field label="Asking for the lot" value={asking} onChangeText={setAsking} keyboardType="decimal-pad" placeholder="Optional" />
            </View>
            <View style={{ flex: 1 }}>
              <Field label="Shipping to you" value={shipping} onChangeText={setShipping} keyboardType="decimal-pad" placeholder="0" />
            </View>
          </View>
          <Button
            title={analyze.isPending ? 'Analyzing…' : analyze.data ? 'Re-analyze' : 'Analyze lot'}
            icon="sparkles"
            onPress={() => analyze.mutate()}
            loading={analyze.isPending}
            disabled={!photo}
          />
          {analyze.isPending && <Progress />}
          {analyze.isError && <ErrorBanner message={analyze.error.message} onRetry={() => analyze.mutate()} />}
        </Surface>

        {analyze.data && <Results data={analyze.data} />}
      </ScrollView>
    </KeyboardAvoidingView>
    </>
  );
}

function Progress() {
  const [step, setStep] = useState(0);
  useEffect(() => {
    const id = setInterval(() => setStep((s) => Math.min(s + 1, STEPS.length - 1)), 6000);
    return () => clearInterval(id);
  }, []);
  return (
    <Animated.Text key={step} entering={FadeInDown.duration(250)} style={styles.progress}>
      {STEPS[step]} Big lots can take a minute.
    </Animated.Text>
  );
}

function Results({ data }: { data: LotAnalysis }) {
  return (
    <Animated.View entering={FadeInDown.duration(300)} style={{ gap: space.lg }}>
      <Surface>
        <SectionTitle>Lot value</SectionTitle>
        <Text style={styles.total}>{formatMoney(data.totalMarketValue)}</Text>
        <Text style={styles.muted}>
          {data.cards.length} card{data.cards.length === 1 ? '' : 's'} found · {data.pricedCount} priced
        </Text>
        {data.flip && <VerdictResult flip={data.flip} />}
        {data.notes && (
          <View style={styles.note}>
            <Ionicons name="information-circle-outline" size={18} color={colors.warning} />
            <Text style={styles.noteText}>{data.notes}</Text>
          </View>
        )}
      </Surface>

      <Surface>
        <SectionTitle>Cards</SectionTitle>
        {[...data.cards]
          .sort((a, b) => (b.price.suggestedPrice ?? -1) - (a.price.suggestedPrice ?? -1))
          .map((c, i) => {
            const tier = c.price.tiers.find((t) => t.key === c.price.selectedTier);
            const meta = cardMetaParts(c.card)
              .filter(Boolean)
              .join(' · ');
            return (
              <View key={i} style={styles.cardRow}>
                <View style={{ flex: 1, gap: 2 }}>
                  <Text style={styles.cardName} numberOfLines={1}>
                    {c.card.player ?? 'Unknown'}
                    {c.card.isRookie && c.card.category !== 'tcg' ? ' RC' : ''}
                    {c.card.isAutograph ? ' Auto' : ''}
                  </Text>
                  <Text style={styles.muted} numberOfLines={1}>
                    {meta || '—'}
                  </Text>
                  <Text style={styles.small}>
                    {[c.card.position, `${Math.round(c.card.confidence * 100)}% sure`, c.priceError].filter(Boolean).join(' · ')}
                  </Text>
                </View>
                <View style={{ alignItems: 'flex-end' }}>
                  <Text style={styles.cardPrice}>{formatMoney(c.price.suggestedPrice)}</Text>
                  {tier && <Text style={styles.small}>{tier.label}</Text>}
                </View>
              </View>
            );
          })}
      </Surface>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  content: { padding: space.lg, gap: space.lg, paddingBottom: 64 },
  photo: { aspectRatio: 4 / 3, backgroundColor: colors.surfaceRaised },
  photoTag: {
    position: 'absolute',
    left: space.sm,
    bottom: space.sm,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: colors.overlay,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: radius.pill,
  },
  photoTagText: { color: '#FFFFFF', fontFamily: fonts.bodySemiBold, fontSize: 12 },
  progress: { color: colors.textSecondary, textAlign: 'center', fontFamily: fonts.bodyMedium, fontSize: 14 },
  total: { fontSize: 44, fontFamily: fonts.display, color: colors.text, letterSpacing: -1.2 },
  muted: { fontSize: 13, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 18 },
  small: { fontSize: 11, fontFamily: fonts.body, color: colors.textMuted },
  note: { flexDirection: 'row', gap: space.sm, padding: space.md, borderRadius: radius.md, backgroundColor: colors.warningSoft },
  noteText: { flex: 1, color: colors.warning, fontFamily: fonts.bodyMedium, fontSize: 13, lineHeight: 18 },
  cardRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    paddingVertical: space.sm,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.border,
  },
  cardName: { fontSize: 15, fontFamily: fonts.bodySemiBold, color: colors.text },
  cardPrice: { fontSize: 17, fontFamily: fonts.displayMedium, color: colors.text },
});
