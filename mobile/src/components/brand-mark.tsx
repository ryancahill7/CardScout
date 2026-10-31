import Ionicons from '@expo/vector-icons/Ionicons';
import { StyleSheet, Text, View } from 'react-native';

import { HoloFrame } from '@/components/ui';
import { colors, fonts } from '@/lib/theme';

// CardScout's mark: a small trading card with a holo-foil edge and the scout lens, plus the wordmark.
// Drawn in code until there's a real logo asset.
export function BrandMark({ size = 'large' }: { size?: 'large' | 'small' }) {
  const s = size === 'large' ? 1 : 0.75;
  return (
    <View style={styles.row} accessible accessibilityRole="image" accessibilityLabel="CardScout">
      <View style={{ transform: [{ rotate: '-8deg' }] }}>
        <HoloFrame borderRadius={7 * s} width={2}>
          <View style={[styles.card, { width: 34 * s, height: 46 * s }]}>
            <Ionicons name="search" size={18 * s} color={colors.primary} />
          </View>
        </HoloFrame>
      </View>
      <Text style={[styles.word, { fontSize: 26 * s }]}>
        Card<Text style={{ color: colors.primary }}>Scout</Text>
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 14 },
  card: { alignItems: 'center', justifyContent: 'center', backgroundColor: colors.surfaceRaised },
  word: { fontFamily: fonts.display, color: colors.text, letterSpacing: -0.6 },
});
