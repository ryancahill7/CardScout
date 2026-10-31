import Ionicons from '@expo/vector-icons/Ionicons';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import * as Haptics from 'expo-haptics';
import { Image } from 'expo-image';
import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import Animated, {
  FadeInDown,
  useAnimatedStyle,
  useSharedValue,
  withRepeat,
  withTiming,
  ZoomIn,
} from 'react-native-reanimated';
import { SafeAreaView } from 'react-native-safe-area-context';

import { CapturedPhoto, CardCamera } from '@/components/card-camera';
import { CardForm, CardFormValues, fromFormValues, parseNumber, toFormValues } from '@/components/card-form';
import { GradingPanel } from '@/components/grading-panel';
import { MarketPanel } from '@/components/market-panel';
import { PricePanel } from '@/components/price-panel';
import { SoldCompsPanel } from '@/components/sold-comps-panel';
import { Button, ErrorBanner, HoloFrame, Loading, ScreenTitle, SectionTitle, Surface } from '@/components/ui';
import { VerdictPanel } from '@/components/verdict-panel';
import { Card, createCard, gameLabel, identifyCard, IdentifyResponse, isTcg, PriceQuote, quoteRequestFrom } from '@/lib/api';
import { cardMetaParts, unknownName } from '@/lib/card-meta';
import { isConfigured } from '@/lib/config';
import { prepareForUpload } from '@/lib/images';
import { formatMoney } from '@/lib/money';
import { useSettings } from '@/lib/settings';
import { colors, fonts, radius, space } from '@/lib/theme';
import { useCardPricing } from '@/lib/use-pricing';

type Side = 'front' | 'back';

const IDENTIFY_STEPS = ['Reading the card…', 'Identifying the card & set…', 'Pulling market comps…'];

export default function ScanScreen() {
  const queryClient = useQueryClient();

  const [photos, setPhotos] = useState<Record<Side, string | null>>({ front: null, back: null });
  const [capturing, setCapturing] = useState<Side | null>('front');
  const [preparing, setPreparing] = useState(false);
  const [photoError, setPhotoError] = useState<string | null>(null);
  const [identified, setIdentified] = useState<IdentifyResponse | null>(null);
  const [form, setForm] = useState<CardFormValues | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [saved, setSaved] = useState<Card | null>(null);
  const [asking, setAsking] = useState('');
  const settings = useSettings();

  // A new quote replaces the price in the form (the user can still type over it)
  const pricing = useCardPricing(form, (quote) =>
    setForm((f) => f && { ...f, suggestedPrice: quote.suggestedPrice == null ? f.suggestedPrice : quote.suggestedPrice.toFixed(2) }),
  );

  function applyResult(result: IdentifyResponse) {
    void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    const values = toFormValues({
      ...result.card,
      ebayTitle: result.card.suggestedEbayTitle,
      suggestedPrice: result.price.suggestedPrice,
      costBasis: null,
    });
    setIdentified(result);
    setForm(values);
    pricing.seed(result.price, values, result.priceError);
  }

  // Bumped by Start over, so a slow identify that finishes afterwards is ignored
  const attempt = useRef(0);
  // The front photo the latest front-only read is for, so a retaken front ignores the old read
  const frontReadFor = useRef<string | null>(null);
  // Set when the user skips the back before the front-only read has come back
  const useFrontOnly = useRef(false);

  // Front-only read, started as soon as the front is captured (while the user lines up the
  // back). It says whether the card is a TCG card: TCG backs are identical within a game, so
  // those go straight to review. It's also the result when a sports card's back is skipped.
  const frontRead = useMutation({
    mutationFn: async (frontUri: string) => {
      const mine = attempt.current;
      const result = await identifyCard(frontUri);
      return { result, frontUri, stale: mine !== attempt.current || frontReadFor.current !== frontUri };
    },
    onSuccess: ({ result, stale }) => {
      if (stale) return;
      if (isTcg(result.card) || useFrontOnly.current) {
        attempt.current++; // a front+back identify already in flight isn't needed any more
        setCapturing(null);
        applyResult(result);
      }
    },
  });

  const frontReadResult = frontRead.data && !frontRead.data.stale && frontRead.data.frontUri === photos.front ? frontRead.data.result : null;

  const identify = useMutation({
    mutationFn: async () => {
      const mine = ++attempt.current;
      // Front only and the background read already has it: no second call
      if (!photos.back && frontReadResult) return { result: frontReadResult, stale: false };
      const result = await identifyCard(photos.front!, photos.back);
      return { result, stale: mine !== attempt.current };
    },
    onSuccess: ({ result, stale }) => {
      if (stale) return;
      applyResult(result);
    },
    onError: () => void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error),
  });

  const save = useMutation({
    mutationFn: createCard,
    onSuccess: (card) => {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.invalidateQueries({ queryKey: ['cards'] });
      setSaved(card);
    },
  });

  function reset() {
    attempt.current++;
    frontReadFor.current = null;
    useFrontOnly.current = false;
    frontRead.reset();
    setPhotos({ front: null, back: null });
    setCapturing('front');
    setIdentified(null);
    setForm(null);
    setFormError(null);
    setSaved(null);
    setAsking('');
    setPhotoError(null);
    pricing.reset();
    identify.reset();
    save.reset();
  }

  async function handleCaptured(side: Side, photo: CapturedPhoto) {
    setPreparing(true);
    setPhotoError(null);
    try {
      const uri = await prepareForUpload(photo.uri, photo.width, photo.height);
      const next = { ...photos, [side]: uri };
      setPhotos(next);
      if (side === 'front') {
        // Start reading the front now; a TCG card skips the back step when it comes back
        frontReadFor.current = uri;
        useFrontOnly.current = false;
        frontRead.mutate(uri);
      }
      // Guided flow: front, then back (sports only, optional), then review. Retakes go straight back to review.
      setCapturing(side === 'front' && !next.back ? 'back' : null);
      identify.reset();
    } catch (e) {
      setPhotoError(`Couldn't process the photo: ${e instanceof Error ? e.message : String(e)}`);
      setCapturing(null);
    } finally {
      setPreparing(false);
    }
  }

  function handleSave() {
    if (!form || !identified) return;
    const parsed = fromFormValues(form);
    if (!parsed.ok) {
      setFormError(parsed.error);
      return;
    }
    setFormError(null);
    save.mutate({
      ...parsed.fields,
      confidence: identified.card.confidence,
      compProductId: pricing.quote?.compProductId ?? null,
      frontImagePath: identified.frontImagePath,
      backImagePath: identified.backImagePath,
      aiRawJson: identified.aiRawJson,
    });
  }

  if (!isConfigured) {
    return (
      <SafeAreaView style={styles.screen} edges={['top']}>
        <View style={{ padding: space.lg }}>
          <ErrorBanner message="API not configured. Copy mobile/.env.example to mobile/.env, fill in EXPO_PUBLIC_API_URL and EXPO_PUBLIC_API_KEY, then restart Expo." />
        </View>
      </SafeAreaView>
    );
  }

  if (preparing) return <Loading label="Processing photo…" />;

  if (capturing) {
    const backHint =
      capturing !== 'back' || !photos.front
        ? null
        : frontRead.isPending
          ? 'Checking the card…'
          : frontReadResult && !isTcg(frontReadResult.card)
            ? 'Sports card · the back adds the year and card #'
            : null;
    return (
      <CardCamera
        key={capturing}
        side={capturing}
        hint={backHint}
        onCaptured={(photo) => handleCaptured(capturing, photo)}
        onCancel={photos.front || photos.back ? () => setCapturing(null) : undefined}
        onSkip={
          capturing === 'back' && photos.front
            ? () => {
                setCapturing(null);
                if (frontReadResult) applyResult(frontReadResult);
                else if (frontRead.isPending) useFrontOnly.current = true;
              }
            : undefined
        }
      />
    );
  }

  if (saved) {
    return (
      <SafeAreaView style={[styles.screen, styles.centered]} edges={['top']}>
        <Animated.View entering={ZoomIn.springify().damping(14)} style={styles.savedBadge}>
          <Ionicons name="checkmark" size={44} color={colors.onPrimary} />
        </Animated.View>
        <Text style={styles.savedTitle}>Added to your collection</Text>
        <Text style={styles.savedSubtitle}>{saved.ebayTitle}</Text>
        {saved.suggestedPrice != null && <Text style={styles.savedValue}>{formatMoney(saved.suggestedPrice)}</Text>}
        <View style={{ alignSelf: 'stretch', gap: space.md, marginTop: space.xl }}>
          <Button title="Scan another card" icon="scan" onPress={reset} />
          <Button
            title="View card"
            variant="secondary"
            onPress={() => {
              reset();
              router.push({ pathname: '/inventory/[id]', params: { id: String(saved.id) } }, { withAnchor: true });
            }}
          />
        </View>
      </SafeAreaView>
    );
  }

  const busy = identify.isPending || save.isPending;
  // Waiting on the front-only read after skipping the back
  const readingFront = !identified && useFrontOnly.current && frontRead.isPending;
  // TCG cards don't need the back, so the slot is only shown when there is one
  const sides = identified && isTcg(identified.card) && !photos.back ? (['front'] as const) : (['front', 'back'] as const);

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" keyboardDismissMode="interactive">
          <ScreenTitle
            title={identified ? 'Review' : 'Scan'}
            subtitle={
              identified
                ? 'Check the details, then add it to your collection'
                : photos.back
                  ? 'Both sides captured. Ready when you are.'
                  : 'Front captured. Add the back for sports cards, or go ahead.'
            }
            right={
              <Pressable onPress={reset} disabled={save.isPending} hitSlop={10} accessibilityRole="button" accessibilityLabel="Start over">
                <Text style={[styles.link, save.isPending && { opacity: 0.4 }]}>{identify.isPending ? 'Cancel' : 'Start over'}</Text>
              </Pressable>
            }
          />

          <View style={styles.photoRow}>
            {sides.map((side) => (
              <PhotoSlot
                key={side}
                side={side}
                uri={photos[side]}
                scanning={identify.isPending || readingFront}
                disabled={busy}
                onPress={() => setCapturing(side)}
              />
            ))}
          </View>

          {photoError && <ErrorBanner message={photoError} />}

          {!identified && (
            <View style={{ gap: space.md }}>
              {identify.isError && <ErrorBanner message={identify.error.message} onRetry={() => identify.mutate()} />}
              <Button
                title={identify.isPending || readingFront ? 'Identifying…' : 'Identify & price'}
                icon="sparkles"
                loading={identify.isPending || readingFront}
                disabled={!photos.front}
                onPress={() => identify.mutate()}
              />
              {(identify.isPending || readingFront) && <IdentifyProgress />}
            </View>
          )}

          {identified && form && (
            <Animated.View entering={FadeInDown.duration(320)} style={{ gap: space.lg }}>
              <IdentityHeader result={identified} form={form} />
              <PricePanel
                quote={pricing.quote}
                loading={pricing.isPending}
                error={pricing.error}
                stale={pricing.stale}
                activePrice={parseNumber(form.suggestedPrice) ?? null}
                costBasis={parseNumber(form.costBasis) ?? null}
                onReprice={() => pricing.reprice()}
                onPickAlternative={(id) => pricing.reprice(id)}
                onUsePrice={(p) => setForm({ ...form, suggestedPrice: p.toFixed(2) })}
              />
              {pricing.quote && pricing.quote.suggestedPrice != null && <ScanSoldComps form={form} quote={pricing.quote} />}
              <Surface>
                <SectionTitle>Buying it?</SectionTitle>
                <VerdictPanel
                  marketPrice={pricing.quote?.suggestedPrice}
                  matchConfidence={pricing.quote?.matchConfidence}
                  asking={asking}
                  onAskingChange={(v) => {
                    setAsking(v);
                    // What you pay is your cost basis unless you've typed one
                    setForm((f) => f && (f.costBasis.trim() === '' || f.costBasis === asking ? { ...f, costBasis: v } : f));
                  }}
                  targets={settings.targets}
                />
              </Surface>
              {pricing.quote && !form.gradingCompany.trim() && (
                <GradingPanel tiers={pricing.quote.tiers} outlook={identified.card.gradeOutlook} />
              )}
              <ScanMarket form={form} />
              <CardForm values={form} onChange={setForm} />
              {formError && <ErrorBanner message={formError} />}
              {save.isError && <ErrorBanner message={save.error.message} />}
              <Button title="Add to collection" icon="add-circle" onPress={handleSave} loading={save.isPending} />
              <Button title="Discard" variant="ghost" onPress={reset} disabled={save.isPending} />
            </Animated.View>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

function ScanSoldComps({ form, quote }: { form: CardFormValues; quote: PriceQuote }) {
  const parsed = fromFormValues(form);
  return parsed.ok ? <SoldCompsPanel quote={quote} card={parsed.fields} /> : null;
}

// eBay listings for the card as currently entered
function ScanMarket({ form }: { form: CardFormValues }) {
  const parsed = fromFormValues(form);
  return <MarketPanel request={parsed.ok ? quoteRequestFrom(parsed.fields) : null} />;
}

function PhotoSlot({
  side,
  uri,
  scanning,
  disabled,
  onPress,
}: {
  side: Side;
  uri: string | null;
  scanning: boolean;
  disabled: boolean;
  onPress: () => void;
}) {
  return (
    <Pressable
      style={({ pressed }) => [{ flex: 1 }, pressed && { transform: [{ scale: 0.98 }] }]}
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={`Retake ${side} photo`}>
      <HoloFrame borderRadius={radius.lg}>
        <View style={styles.photo}>
          {uri ? (
            <Image source={{ uri }} style={StyleSheet.absoluteFill} contentFit="cover" transition={200} />
          ) : (
            <Ionicons name="camera-outline" size={32} color={colors.textMuted} />
          )}
          {scanning && <ScanLine />}
          <View style={styles.photoTag}>
            <Text style={styles.photoTagText}>{side === 'front' ? 'Front' : 'Back'}</Text>
            {!disabled && <Ionicons name="refresh" size={12} color="#FFFFFF" />}
          </View>
        </View>
      </HoloFrame>
    </Pressable>
  );
}

// Sweeping light across the photo while Claude reads the card
function ScanLine() {
  const progress = useSharedValue(0);
  useEffect(() => {
    progress.value = withRepeat(withTiming(1, { duration: 1400 }), -1, true);
  }, [progress]);
  const style = useAnimatedStyle(() => ({ top: `${progress.value * 96}%` }));
  return <Animated.View style={[styles.scanLine, style]} pointerEvents="none" />;
}

function IdentifyProgress() {
  const [step, setStep] = useState(0);
  useEffect(() => {
    const id = setInterval(() => setStep((s) => Math.min(s + 1, IDENTIFY_STEPS.length - 1)), 3500);
    return () => clearInterval(id);
  }, []);
  return (
    <Animated.Text key={step} entering={FadeInDown.duration(250)} style={styles.progressText} accessibilityLiveRegion="polite">
      {IDENTIFY_STEPS[step]}
    </Animated.Text>
  );
}

function IdentityHeader({ result, form }: { result: IdentifyResponse; form: CardFormValues }) {
  const pct = Math.round(result.card.confidence * 100);
  const color = pct >= 80 ? colors.primary : pct >= 50 ? colors.warning : colors.danger;
  const tcg = form.category === 'tcg';
  const subtitle = tcg
    ? cardMetaParts({ ...form, game: null, parallel: null, rarity: null }).join(' · ')
    : [form.year, form.manufacturer, form.set, form.cardNumber && `#${form.cardNumber}`].filter(Boolean).join(' · ');
  const tags = (
    tcg
      ? [
          gameLabel(form.game || 'other'),
          form.parallel,
          form.rarity,
          form.isAutograph && 'Signed',
          form.gradingCompany && form.grade && `${form.gradingCompany} ${form.grade}`,
        ]
      : [
          form.parallel && form.parallel,
          form.isRookie && 'RC',
          form.isAutograph && 'Auto',
          form.isRelic && 'Relic',
          form.gradingCompany && form.grade && `${form.gradingCompany} ${form.grade}`,
        ]
  ).filter(Boolean) as string[];

  return (
    <View style={{ gap: space.sm }}>
      <View style={styles.aiRow}>
        <Ionicons name="sparkles" size={14} color={color} />
        <Text style={[styles.aiText, { color }]}>AI read · {pct}% sure</Text>
        {pct < 80 && <Text style={styles.aiHint}>double-check below</Text>}
      </View>
      <Text style={styles.player}>{form.player || unknownName(form)}</Text>
      {subtitle.length > 0 && <Text style={styles.subtitle}>{subtitle}</Text>}
      {tags.length > 0 && (
        <View style={styles.tags}>
          {tags.map((tag) => (
            <View key={tag} style={styles.tag}>
              <Text style={styles.tagText}>{tag}</Text>
            </View>
          ))}
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  centered: { alignItems: 'center', justifyContent: 'center', padding: space.xl, gap: space.sm },
  content: { padding: space.lg, gap: space.lg, paddingBottom: 48 },
  link: { color: colors.primary, fontFamily: fonts.bodySemiBold, fontSize: 15, paddingBottom: 6 },
  photoRow: { flexDirection: 'row', gap: space.md },
  photo: {
    aspectRatio: 2.5 / 3.5,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surfaceRaised,
  },
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
  scanLine: {
    position: 'absolute',
    left: 0,
    right: 0,
    height: 3,
    backgroundColor: colors.primary,
    shadowColor: colors.primary,
    shadowOpacity: 0.9,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 0 },
  },
  progressText: { color: colors.textSecondary, textAlign: 'center', fontFamily: fonts.bodyMedium, fontSize: 14 },
  aiRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  aiText: { fontFamily: fonts.bodySemiBold, fontSize: 13 },
  aiHint: { fontFamily: fonts.body, fontSize: 13, color: colors.textMuted },
  player: { fontSize: 30, fontFamily: fonts.display, color: colors.text, letterSpacing: -0.6, lineHeight: 34 },
  subtitle: { fontSize: 15, fontFamily: fonts.body, color: colors.textSecondary },
  tags: { flexDirection: 'row', flexWrap: 'wrap', gap: 6, marginTop: 2 },
  tag: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: radius.pill,
    backgroundColor: colors.surfaceRaised,
    borderWidth: 1,
    borderColor: colors.border,
  },
  tagText: { color: colors.text, fontFamily: fonts.bodySemiBold, fontSize: 12 },
  savedBadge: {
    width: 88,
    height: 88,
    borderRadius: 44,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: space.md,
  },
  savedTitle: { fontSize: 26, fontFamily: fonts.display, color: colors.text, textAlign: 'center', letterSpacing: -0.5 },
  savedSubtitle: { color: colors.textSecondary, textAlign: 'center', fontFamily: fonts.body, fontSize: 15, lineHeight: 22 },
  savedValue: { color: colors.primary, fontFamily: fonts.display, fontSize: 22, marginTop: space.xs },
});
