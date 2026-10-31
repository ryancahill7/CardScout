import { StyleSheet, View } from 'react-native';

import { colors } from '@/lib/theme';

// Tiny bar-style trend of market price snapshots; green if the latest is up from the first
export function Sparkline({ values, height = 36 }: { values: number[]; height?: number }) {
  if (values.length < 2) return null;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min || 1;
  const up = values[values.length - 1] >= values[0];
  const color = up ? colors.primary : colors.danger;
  // Keep at most ~40 bars so each stays visible
  const step = Math.ceil(values.length / 40);
  const shown = values.filter((_, i) => i % step === 0 || i === values.length - 1);

  return (
    <View style={[styles.row, { height }]} accessibilityLabel={`Price trend ${up ? 'up' : 'down'}`}>
      {shown.map((v, i) => (
        <View
          key={i}
          style={[styles.bar, { height: `${15 + ((v - min) / range) * 85}%`, backgroundColor: color, opacity: 0.35 + (0.65 * i) / shown.length }]}
        />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'flex-end', gap: 2 },
  bar: { flex: 1, borderRadius: 2 },
});
