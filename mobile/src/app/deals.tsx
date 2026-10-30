import Ionicons from '@expo/vector-icons/Ionicons';
import { StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { ScreenTitle } from '@/components/ui';
import { colors, fonts, radius, space } from '@/lib/theme';

const COMING = [
  { icon: 'trending-down', text: 'Underpriced eBay listings found by the CardScout workers' },
  { icon: 'cash-outline', text: 'Net profit after fees and shipping on every deal' },
  { icon: 'notifications-outline', text: 'Push alerts when a great deal appears' },
] as const;

// Phase 2: underpriced eBay listings from the CardScout workers, with push alerts.
export default function DealsScreen() {
  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <View style={styles.content}>
        <ScreenTitle title="Deals" subtitle="Coming soon" />
        <View style={styles.card}>
          <View style={styles.icon}>
            <Ionicons name="flash" size={28} color={colors.primary} />
          </View>
          <Text style={styles.title}>Buy low, flip smart</Text>
          {COMING.map((item) => (
            <View key={item.text} style={styles.row}>
              <Ionicons name={item.icon} size={18} color={colors.textSecondary} />
              <Text style={styles.rowText}>{item.text}</Text>
            </View>
          ))}
        </View>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  content: { padding: space.lg, gap: space.xl },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    borderWidth: 1,
    borderColor: colors.border,
    padding: space.xl,
    gap: space.md,
  },
  icon: {
    width: 56,
    height: 56,
    borderRadius: radius.lg,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: { fontSize: 22, fontFamily: fonts.display, color: colors.text, marginBottom: space.xs },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.md },
  rowText: { flex: 1, color: colors.textSecondary, fontFamily: fonts.body, fontSize: 15, lineHeight: 21 },
});
