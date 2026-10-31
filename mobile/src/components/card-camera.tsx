import Ionicons from '@expo/vector-icons/Ionicons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as Haptics from 'expo-haptics';
import * as ImagePicker from 'expo-image-picker';
import { useFocusEffect } from 'expo-router';
import { useCallback, useRef, useState } from 'react';
import { Linking, Pressable, StyleSheet, Text, View, ViewStyle } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { colors, fonts, radius, space } from '@/lib/theme';
import { Button, ErrorBanner, Loading } from './ui';

export interface CapturedPhoto {
  uri: string;
  width: number;
  height: number;
}

// card: guided front-then-back scan. quick: one front photo (show mode). lot: several cards in one wide shot.
export type CameraMode = 'card' | 'quick' | 'lot';

// Full-screen guided capture for one side of a card, with a photo-library fallback.
// hint is a status line under the instructions (e.g. "Checking card type…"); onSkip adds a
// Skip button (the back photo is optional).
export function CardCamera({
  side,
  onCaptured,
  onCancel,
  mode = 'card',
  hint,
  onSkip,
}: {
  side: 'front' | 'back';
  onCaptured: (photo: CapturedPhoto) => void;
  onCancel?: () => void;
  mode?: CameraMode;
  hint?: string | null;
  onSkip?: () => void;
}) {
  const insets = useSafeAreaInsets();
  const [permission, requestPermission] = useCameraPermissions();
  const cameraRef = useRef<CameraView>(null);
  const [ready, setReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [focused, setFocused] = useState(true);

  // Only one camera preview can run at a time; unmount it when the tab loses focus
  useFocusEffect(
    useCallback(() => {
      setFocused(true);
      return () => {
        setFocused(false);
        setReady(false);
      };
    }, []),
  );

  async function takePicture() {
    if (!ready || !cameraRef.current || busy) return;
    setBusy(true);
    setError(null);
    void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    try {
      const photo = await cameraRef.current.takePictureAsync({ quality: 0.9 });
      onCaptured({ uri: photo.uri, width: photo.width, height: photo.height });
    } catch (e) {
      setError(`Couldn't take the photo: ${e instanceof Error ? e.message : String(e)}`);
    } finally {
      setBusy(false);
    }
  }

  async function pickFromLibrary() {
    setError(null);
    const result = await ImagePicker.launchImageLibraryAsync({ mediaTypes: 'images', quality: 1 });
    if (!result.canceled && result.assets[0]) {
      const asset = result.assets[0];
      onCaptured({ uri: asset.uri, width: asset.width, height: asset.height });
    }
  }

  if (!permission) return <Loading />;

  if (!permission.granted) {
    return (
      <View style={[styles.permission, { paddingTop: insets.top + space.xl }]}>
        <View style={styles.permissionIcon}>
          <Ionicons name="scan" size={40} color={colors.primary} />
        </View>
        <Text style={styles.permissionTitle}>Point, shoot, price</Text>
        <Text style={styles.permissionBody}>
          CardScout photographs a card, identifies it (sports cards, Pokémon, Magic, Yu-Gi-Oh! and more), and pulls live
          market comps.
        </Text>
        <View style={{ alignSelf: 'stretch', gap: space.md, marginTop: space.lg }}>
          {permission.canAskAgain ? (
            <Button title="Allow camera" icon="camera" onPress={requestPermission} />
          ) : (
            <Button title="Open Settings" icon="settings-outline" onPress={() => Linking.openSettings()} />
          )}
          <Button title="Choose from library" icon="images-outline" variant="secondary" onPress={pickFromLibrary} />
          {onCancel && <Button title="Cancel" variant="ghost" onPress={onCancel} />}
        </View>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      {focused && (
        <CameraView ref={cameraRef} style={StyleSheet.absoluteFill} facing="back" onCameraReady={() => setReady(true)} />
      )}

      <View style={[styles.topBar, { paddingTop: insets.top + space.sm }]}>
        <View style={styles.steps}>
          {mode === 'card' ? (
            (['front', 'back'] as const).map((s, i) => (
              <View key={s} style={[styles.step, s === side && styles.stepActive]}>
                <Text style={[styles.stepText, s === side && { color: colors.onPrimary }]}>
                  {i + 1} · {s === 'front' ? 'Front' : 'Back'}
                </Text>
              </View>
            ))
          ) : (
            <View style={[styles.step, styles.stepActive]}>
              <Text style={[styles.stepText, { color: colors.onPrimary }]}>{mode === 'lot' ? 'Lot photo' : 'Quick scan · front'}</Text>
            </View>
          )}
        </View>
        {onCancel && (
          <Pressable
            onPress={onCancel}
            style={styles.glassButton}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel="Close camera">
            <Ionicons name="close" size={22} color="#FFFFFF" />
          </Pressable>
        )}
      </View>

      <View style={styles.overlay} pointerEvents="none">
        <View style={mode === 'lot' ? styles.lotGuide : styles.guide}>
          <Corner style={{ top: 0, left: 0 }} />
          <Corner style={{ top: 0, right: 0, transform: [{ rotate: '90deg' }] }} />
          <Corner style={{ bottom: 0, right: 0, transform: [{ rotate: '180deg' }] }} />
          <Corner style={{ bottom: 0, left: 0, transform: [{ rotate: '270deg' }] }} />
        </View>
        <Text style={styles.instructions}>
          {mode === 'lot' ? 'Fit every card in the frame' : `Line up the ${side} of the card`}
        </Text>
        <Text style={styles.subInstructions}>
          {mode === 'lot' ? 'Fronts up · no overlap · avoid glare' : 'Fill the frame · avoid glare · keep it flat'}
        </Text>
        {hint ? (
          <View style={styles.hint}>
            <Text style={styles.hintText}>{hint}</Text>
          </View>
        ) : null}
      </View>

      <View style={[styles.controls, { paddingBottom: Math.max(insets.bottom, space.lg) + space.sm }]}>
        {error && <ErrorBanner message={error} />}
        <View style={styles.controlRow}>
          <Pressable
            onPress={pickFromLibrary}
            style={styles.glassButtonLarge}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel="Choose from library">
            <Ionicons name="images-outline" size={24} color="#FFFFFF" />
          </Pressable>
          <Pressable
            onPress={takePicture}
            disabled={!ready || busy}
            accessibilityRole="button"
            accessibilityLabel={`Take ${side} photo`}
            style={({ pressed }) => [
              styles.shutter,
              (pressed || busy || !ready) && { opacity: 0.6, transform: [{ scale: 0.94 }] },
            ]}>
            <View style={styles.shutterInner} />
          </Pressable>
          {onSkip ? (
            <Pressable
              onPress={onSkip}
              style={styles.glassButtonLarge}
              hitSlop={8}
              accessibilityRole="button"
              accessibilityLabel={`Skip the ${side} photo`}>
              <Text style={styles.skipText}>Skip</Text>
            </Pressable>
          ) : (
            <View style={styles.glassButtonLarge} />
          )}
        </View>
      </View>
    </View>
  );
}

// One L-shaped corner of the framing guide; rotated for the other three
function Corner({ style }: { style: ViewStyle }) {
  return <View style={[styles.corner, style]} />;
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#000' },
  // In normal flow (not floating over the preview) so the guide below can never overlap it
  topBar: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: space.lg,
    paddingBottom: space.sm,
    minHeight: 44,
  },
  steps: { flexDirection: 'row', gap: space.sm },
  step: { paddingHorizontal: 12, paddingVertical: 7, borderRadius: radius.pill, backgroundColor: '#FFFFFF24' },
  stepActive: { backgroundColor: colors.primary },
  stepText: { color: '#FFFFFFDD', fontFamily: fonts.bodySemiBold, fontSize: 13 },
  glassButton: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: '#FFFFFF24',
    alignItems: 'center',
    justifyContent: 'center',
  },
  glassButtonLarge: {
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: '#FFFFFF1F',
    alignItems: 'center',
    justifyContent: 'center',
  },
  overlay: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: space.sm, paddingVertical: space.md },
  // Standard trading card ratio is 2.5" x 3.5". Sized from the height left between the top
  // bar and the controls (width follows from the ratio) so it fits on any screen shape.
  guide: { height: '72%', aspectRatio: 2.5 / 3.5, marginBottom: space.md },
  lotGuide: { width: '92%', aspectRatio: 3 / 4, marginBottom: space.md },
  corner: {
    position: 'absolute',
    width: 36,
    height: 36,
    borderTopWidth: 4,
    borderLeftWidth: 4,
    borderColor: colors.primary,
    borderTopLeftRadius: 18,
  },
  instructions: {
    color: '#FFFFFF',
    fontSize: 20,
    fontFamily: fonts.display,
    textShadowColor: '#000',
    textShadowRadius: 8,
  },
  subInstructions: {
    color: '#FFFFFFCC',
    fontSize: 14,
    fontFamily: fonts.bodyMedium,
    textShadowColor: '#000',
    textShadowRadius: 8,
  },
  hint: {
    marginTop: space.xs,
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: radius.pill,
    backgroundColor: colors.overlay,
  },
  hintText: { color: '#FFFFFF', fontFamily: fonts.bodySemiBold, fontSize: 13 },
  skipText: { color: '#FFFFFF', fontFamily: fonts.bodySemiBold, fontSize: 14 },
  controls: { paddingHorizontal: space.xl, paddingTop: space.lg, gap: space.md, backgroundColor: '#00000080' },
  controlRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  shutter: {
    width: 80,
    height: 80,
    borderRadius: 40,
    borderWidth: 4,
    borderColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
  },
  shutterInner: { width: 62, height: 62, borderRadius: 31, backgroundColor: colors.primary },
  permission: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: space.xl,
    gap: space.md,
    backgroundColor: colors.background,
  },
  permissionIcon: {
    width: 88,
    height: 88,
    borderRadius: radius.xl,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: space.sm,
  },
  permissionTitle: { fontSize: 28, fontFamily: fonts.display, color: colors.text, letterSpacing: -0.5 },
  permissionBody: { color: colors.textSecondary, textAlign: 'center', fontFamily: fonts.body, fontSize: 15, lineHeight: 22 },
});
