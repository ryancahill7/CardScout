import { Image } from 'expo-image';
import { StyleSheet, Text, View } from 'react-native';

import { colors, fonts } from '@/lib/theme';

const logo = require('../../assets/images/logo.png');

// CardScout's logo (assets/images/logo.png, generated from logo-source.png) plus the wordmark
export function BrandMark({ size = 'large' }: { size?: 'large' | 'small' }) {
  const s = size === 'large' ? 1 : 0.75;
  return (
    <View style={styles.row} accessible accessibilityRole="image" accessibilityLabel="CardScout">
      <Image source={logo} style={{ width: 52 * s, height: 52 * s }} contentFit="contain" />
      <Text style={[styles.word, { fontSize: 26 * s }]}>
        Card<Text style={{ color: colors.primary }}>Scout</Text>
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  word: { fontFamily: fonts.display, color: colors.text, letterSpacing: -0.6 },
});
