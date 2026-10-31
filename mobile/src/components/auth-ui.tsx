import Ionicons from '@expo/vector-icons/Ionicons';
import { router } from 'expo-router';
import { ComponentProps, ComponentRef, ReactNode, Ref, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, TextInputProps, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { KEYBOARD_DONE_ID } from '@/components/ui';
import { colors, fonts, radius, space } from '@/lib/theme';

type IconName = ComponentProps<typeof Ionicons>['name'];
export type TextInputHandle = ComponentRef<typeof TextInput>;

// Keyboard-safe scrolling page for the auth screens
export function AuthScreen({ children, edges = ['bottom'] }: { children: ReactNode; edges?: ('top' | 'bottom')[] }) {
  return (
    <SafeAreaView style={styles.screen} edges={edges}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView
          contentContainerStyle={styles.content}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode="interactive"
          contentInsetAdjustmentBehavior="automatic">
          {children}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

// Text input with a label, styled like ui.tsx's Field, that accepts a ref (to move focus on "next")
export function AuthInput({
  label,
  hint,
  ref,
  secure,
  ...props
}: TextInputProps & { label: string; hint?: string; ref?: Ref<TextInputHandle>; secure?: boolean }) {
  const [focused, setFocused] = useState(false);
  const [revealed, setRevealed] = useState(false);
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <View>
        <TextInput
          ref={ref}
          placeholderTextColor={colors.textMuted}
          selectionColor={colors.primary}
          keyboardAppearance="dark"
          accessibilityLabel={label}
          inputAccessoryViewID={KEYBOARD_DONE_ID}
          autoCorrect={false}
          secureTextEntry={secure && !revealed}
          {...props}
          onFocus={(e) => {
            setFocused(true);
            props.onFocus?.(e);
          }}
          onBlur={(e) => {
            setFocused(false);
            props.onBlur?.(e);
          }}
          style={[styles.input, secure && { paddingRight: 52 }, focused && styles.inputFocused, props.style]}
        />
        {secure && (
          <Pressable
            onPress={() => setRevealed((r) => !r)}
            hitSlop={8}
            style={styles.reveal}
            accessibilityRole="button"
            accessibilityLabel={revealed ? 'Hide password' : 'Show password'}>
            <Ionicons name={revealed ? 'eye-off-outline' : 'eye-outline'} size={20} color={colors.textSecondary} />
          </Pressable>
        )}
      </View>
      {hint && <Text style={styles.hint}>{hint}</Text>}
    </View>
  );
}

// Full-width "Continue with X" button
export function ProviderButton({
  title,
  icon,
  onPress,
  disabled,
}: {
  title: string;
  icon: IconName;
  onPress: () => void;
  disabled?: boolean;
}) {
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={title}
      accessibilityState={{ disabled: !!disabled }}
      style={({ pressed }) => [styles.provider, pressed && { backgroundColor: colors.surfacePressed }, disabled && { opacity: 0.45 }]}>
      <Ionicons name={icon} size={20} color={colors.text} />
      <Text style={styles.providerText}>{title}</Text>
    </Pressable>
  );
}

export function Divider({ label }: { label: string }) {
  return (
    <View style={styles.divider} accessibilityElementsHidden importantForAccessibility="no-hide-descendants">
      <View style={styles.dividerLine} />
      <Text style={styles.dividerText}>{label}</Text>
      <View style={styles.dividerLine} />
    </View>
  );
}

export function TextLink({ title, onPress, align = 'center' }: { title: string; onPress: () => void; align?: 'center' | 'left' }) {
  return (
    <Pressable onPress={onPress} hitSlop={10} accessibilityRole="link" style={{ alignSelf: align === 'center' ? 'center' : 'flex-start', minHeight: 44, justifyContent: 'center' }}>
      <Text style={styles.link}>{title}</Text>
    </Pressable>
  );
}

// Every secondary auth screen ends with this, so there's always a way back even after a deep link
export function BackToSignIn() {
  return (
    <TextLink
      title="Back to sign in"
      onPress={() => (router.canGoBack() ? router.back() : router.replace('/sign-in'))}
    />
  );
}

export function Notice({ icon, children }: { icon: IconName; children: ReactNode }) {
  return (
    <View style={styles.notice} accessibilityRole="summary">
      <Ionicons name={icon} size={20} color={colors.primary} />
      <Text style={styles.noticeText}>{children}</Text>
    </View>
  );
}

export const authStyles = StyleSheet.create({
  title: { fontSize: 30, fontFamily: fonts.display, color: colors.text, letterSpacing: -0.6 },
  subtitle: { fontSize: 15, fontFamily: fonts.body, color: colors.textSecondary, lineHeight: 21 },
  small: { fontSize: 13, fontFamily: fonts.body, color: colors.textMuted, lineHeight: 19, textAlign: 'center' },
});

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  content: { padding: space.xl, gap: space.lg, paddingBottom: 48, flexGrow: 1 },
  field: { gap: 6 },
  label: { fontSize: 12, fontFamily: fonts.bodySemiBold, color: colors.textSecondary, letterSpacing: 0.3 },
  input: {
    fontSize: 16,
    fontFamily: fonts.body,
    color: colors.text,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    paddingHorizontal: 14,
    paddingVertical: 12,
    minHeight: 50,
  },
  inputFocused: { borderColor: colors.primary, backgroundColor: colors.surfaceRaised },
  reveal: { position: 'absolute', right: 6, top: 0, bottom: 0, width: 44, alignItems: 'center', justifyContent: 'center' },
  hint: { fontSize: 12, color: colors.textMuted, fontFamily: fonts.body },
  provider: {
    minHeight: 52,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceRaised,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: space.sm,
    paddingHorizontal: space.lg,
  },
  providerText: { color: colors.text, fontFamily: fonts.bodySemiBold, fontSize: 16 },
  divider: { flexDirection: 'row', alignItems: 'center', gap: space.md },
  dividerLine: { flex: 1, height: StyleSheet.hairlineWidth, backgroundColor: colors.borderStrong },
  dividerText: { color: colors.textMuted, fontFamily: fonts.bodyMedium, fontSize: 13 },
  link: { color: colors.primary, fontFamily: fonts.bodySemiBold, fontSize: 15 },
  notice: {
    flexDirection: 'row',
    gap: space.md,
    padding: space.md,
    borderRadius: radius.md,
    backgroundColor: colors.primarySoft,
    alignItems: 'flex-start',
  },
  noticeText: { flex: 1, color: colors.text, fontFamily: fonts.body, fontSize: 14, lineHeight: 20 },
});
