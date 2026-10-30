import Ionicons from '@expo/vector-icons/Ionicons';
import * as Haptics from 'expo-haptics';
import { LinearGradient } from 'expo-linear-gradient';
import { ComponentProps, ReactNode, useState } from 'react';
import {
  ActivityIndicator,
  Pressable,
  StyleProp,
  StyleSheet,
  Text,
  TextInput,
  TextInputProps,
  View,
  ViewStyle,
} from 'react-native';

import { CardStatus, CARD_STATUSES } from '@/lib/api';
import { colors, fonts, holo, radius, space, statusColors, statusLabels } from '@/lib/theme';

type IconName = ComponentProps<typeof Ionicons>['name'];
type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

export function Button({
  title,
  onPress,
  variant = 'primary',
  icon,
  disabled,
  loading,
  compact,
}: {
  title: string;
  onPress: () => void;
  variant?: ButtonVariant;
  icon?: IconName;
  disabled?: boolean;
  loading?: boolean;
  compact?: boolean;
}) {
  const inactive = disabled || loading;
  const fg =
    variant === 'primary' ? colors.onPrimary : variant === 'danger' ? colors.danger : variant === 'ghost' ? colors.primary : colors.text;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={title}
      accessibilityState={{ disabled: !!inactive, busy: !!loading }}
      onPress={() => {
        if (variant === 'primary') void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        onPress();
      }}
      disabled={inactive}
      style={({ pressed }) => [
        styles.button,
        compact && styles.buttonCompact,
        variant === 'primary' && { backgroundColor: pressed ? colors.primaryPressed : colors.primary },
        variant === 'secondary' && {
          backgroundColor: pressed ? colors.surfacePressed : colors.surfaceRaised,
          borderColor: colors.border,
          borderWidth: 1,
        },
        variant === 'danger' && { backgroundColor: pressed ? colors.dangerSoft : 'transparent', borderColor: colors.danger, borderWidth: 1 },
        variant === 'ghost' && pressed && { backgroundColor: colors.primarySoft },
        pressed && { transform: [{ scale: 0.98 }] },
        inactive && { opacity: 0.45 },
      ]}>
      {loading ? (
        <ActivityIndicator color={fg} />
      ) : (
        <View style={styles.buttonInner}>
          {icon && <Ionicons name={icon} size={compact ? 16 : 19} color={fg} />}
          <Text style={[styles.buttonText, compact && { fontSize: 14 }, { color: fg }]}>{title}</Text>
        </View>
      )}
    </Pressable>
  );
}

// Elevated container for grouping content
export function Surface({ children, style }: { children: ReactNode; style?: StyleProp<ViewStyle> }) {
  return <View style={[styles.surface, style]}>{children}</View>;
}

// Holographic foil border around its children, the app's signature accent
export function HoloFrame({
  children,
  style,
  borderRadius = radius.xl,
  width = 1.5,
}: {
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
  borderRadius?: number;
  width?: number;
}) {
  return (
    <LinearGradient
      colors={holo}
      start={{ x: 0, y: 0 }}
      end={{ x: 1, y: 1 }}
      style={[{ borderRadius, padding: width }, style]}>
      <View style={{ borderRadius: borderRadius - width, backgroundColor: colors.surface, overflow: 'hidden' }}>{children}</View>
    </LinearGradient>
  );
}

export function ErrorBanner({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <View style={styles.banner} accessibilityRole="alert">
      <Ionicons name="alert-circle" size={20} color={colors.danger} />
      <Text style={styles.bannerText}>{message}</Text>
      {onRetry && (
        <Pressable onPress={onRetry} hitSlop={12} accessibilityRole="button" accessibilityLabel="Retry">
          <Text style={styles.bannerRetry}>Retry</Text>
        </Pressable>
      )}
    </View>
  );
}

export function Loading({ label }: { label?: string }) {
  return (
    <View style={styles.center}>
      <ActivityIndicator size="large" color={colors.primary} />
      {label && <Text style={styles.loadingLabel}>{label}</Text>}
    </View>
  );
}

export function Field({ label, hint, ...props }: TextInputProps & { label: string; hint?: string }) {
  const [focused, setFocused] = useState(false);
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        placeholderTextColor={colors.textMuted}
        selectionColor={colors.primary}
        keyboardAppearance="dark"
        accessibilityLabel={label}
        {...props}
        onFocus={(e) => {
          setFocused(true);
          props.onFocus?.(e);
        }}
        onBlur={(e) => {
          setFocused(false);
          props.onBlur?.(e);
        }}
        style={[
          styles.input,
          focused && { borderColor: colors.primary, backgroundColor: colors.surfaceRaised },
          props.multiline && { minHeight: 76, textAlignVertical: 'top', paddingTop: 12 },
          props.style,
        ]}
      />
      {hint && <Text style={styles.hint}>{hint}</Text>}
    </View>
  );
}

// Pill toggle for card attributes (Rookie, Auto, Relic)
export function ToggleChip({
  label,
  icon,
  value,
  onChange,
}: {
  label: string;
  icon: IconName;
  value: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <Pressable
      accessibilityRole="switch"
      accessibilityLabel={label}
      accessibilityState={{ checked: value }}
      onPress={() => {
        void Haptics.selectionAsync();
        onChange(!value);
      }}
      style={({ pressed }) => [
        styles.chip,
        value && { backgroundColor: colors.primarySoft, borderColor: colors.primary },
        pressed && { opacity: 0.8 },
      ]}>
      <Ionicons name={value ? 'checkmark-circle' : icon} size={16} color={value ? colors.primary : colors.textSecondary} />
      <Text style={[styles.chipText, value && { color: colors.primary }]}>{label}</Text>
    </Pressable>
  );
}

export function StatusPill({ status }: { status: CardStatus }) {
  const c = statusColors[status];
  return (
    <View style={[styles.pill, { backgroundColor: c.bg }]}>
      <View style={[styles.pillDot, { backgroundColor: c.fg }]} />
      <Text style={[styles.pillText, { color: c.fg }]}>{statusLabels[status]}</Text>
    </View>
  );
}

// Segmented control; pass includeAll to add an "All" option (value undefined)
export function StatusPicker({
  value,
  onChange,
  includeAll,
}: {
  value: CardStatus | undefined;
  onChange: (s: CardStatus | undefined) => void;
  includeAll?: boolean;
}) {
  const options: (CardStatus | undefined)[] = includeAll ? [undefined, ...CARD_STATUSES] : CARD_STATUSES;
  return (
    <View style={styles.segmented} accessibilityRole="tablist">
      {options.map((option) => {
        const selected = option === value;
        const label = option ? statusLabels[option] : 'All';
        return (
          <Pressable
            key={option ?? 'ALL'}
            accessibilityRole="tab"
            accessibilityLabel={label}
            accessibilityState={{ selected }}
            onPress={() => {
              if (!selected) void Haptics.selectionAsync();
              onChange(option);
            }}
            style={[styles.segment, selected && styles.segmentSelected]}>
            <Text style={[styles.segmentText, selected && { color: colors.text }]}>{label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

// Small uppercase label above a group of content
export function SectionTitle({ children, right }: { children: string; right?: ReactNode }) {
  return (
    <View style={styles.sectionRow}>
      <Text style={styles.sectionTitle} accessibilityRole="header">
        {children}
      </Text>
      {right}
    </View>
  );
}

// Large screen title used in place of the navigation header
export function ScreenTitle({ title, subtitle, right }: { title: string; subtitle?: string; right?: ReactNode }) {
  return (
    <View style={styles.screenTitleRow}>
      <View style={{ flex: 1 }}>
        <Text style={styles.screenTitle} accessibilityRole="header">
          {title}
        </Text>
        {subtitle && <Text style={styles.screenSubtitle}>{subtitle}</Text>}
      </View>
      {right}
    </View>
  );
}

export const styles = StyleSheet.create({
  button: {
    minHeight: 54,
    borderRadius: radius.lg,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: space.lg,
  },
  buttonCompact: { minHeight: 44, borderRadius: radius.md, paddingHorizontal: space.md },
  buttonInner: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  buttonText: { fontSize: 16, fontFamily: fonts.bodySemiBold, letterSpacing: 0.2 },
  surface: {
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    borderWidth: 1,
    borderColor: colors.border,
    padding: space.lg,
    gap: space.md,
  },
  banner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    padding: space.md,
    borderRadius: radius.md,
    backgroundColor: colors.dangerSoft,
    borderWidth: 1,
    borderColor: '#FF6B7A55',
  },
  bannerText: { color: colors.danger, flex: 1, fontFamily: fonts.bodyMedium, fontSize: 14, lineHeight: 20 },
  bannerRetry: { color: colors.danger, fontFamily: fonts.bodyBold, fontSize: 14 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: space.xl, backgroundColor: colors.background },
  loadingLabel: { color: colors.textSecondary, marginTop: space.md, fontFamily: fonts.bodyMedium, fontSize: 15 },
  field: { gap: 6 },
  label: { fontSize: 12, fontFamily: fonts.bodySemiBold, color: colors.textSecondary, letterSpacing: 0.3 },
  input: {
    fontSize: 16,
    fontFamily: fonts.body,
    color: colors.text,
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    paddingHorizontal: 14,
    paddingVertical: 12,
    minHeight: 48,
  },
  hint: { fontSize: 12, color: colors.textMuted, fontFamily: fonts.body, alignSelf: 'flex-end' },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    minHeight: 44,
    paddingHorizontal: 14,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.background,
  },
  chipText: { color: colors.textSecondary, fontFamily: fonts.bodySemiBold, fontSize: 14 },
  pill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    borderRadius: radius.pill,
    paddingHorizontal: 10,
    paddingVertical: 4,
    alignSelf: 'flex-start',
  },
  pillDot: { width: 6, height: 6, borderRadius: 3 },
  pillText: { fontSize: 12, fontFamily: fonts.bodySemiBold },
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
  sectionRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  sectionTitle: {
    fontSize: 12,
    fontFamily: fonts.bodySemiBold,
    color: colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 1.2,
  },
  screenTitleRow: { flexDirection: 'row', alignItems: 'flex-end', gap: space.md },
  screenTitle: { fontSize: 34, fontFamily: fonts.display, color: colors.text, letterSpacing: -0.8 },
  screenSubtitle: { fontSize: 15, fontFamily: fonts.body, color: colors.textSecondary, marginTop: 2 },
});
