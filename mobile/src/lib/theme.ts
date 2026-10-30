// CardScout design tokens. The app is dark-only (OLED ink): card photos and prices
// pop on near-black, and it matches the "trading terminal" feel of the deals grid.
// Components read tokens from here rather than hardcoding hex values.

export const colors = {
  background: '#06080D',
  surface: '#0E121B',
  surfaceRaised: '#151A26',
  surfacePressed: '#1B2130',
  border: '#232A3A',
  borderStrong: '#323B50',

  text: '#F3F5FA',
  textSecondary: '#9AA3B5',
  textMuted: '#646E83',

  // Signature accent: mint for money / primary actions
  primary: '#5BF2B0',
  primaryPressed: '#3FD996',
  onPrimary: '#04140D',
  primarySoft: '#5BF2B01F',

  danger: '#FF6B7A',
  dangerSoft: '#FF6B7A1A',
  warning: '#FFC861',
  warningSoft: '#FFC8611A',
  success: '#5BF2B0',
  info: '#7AA7FF',
  infoSoft: '#7AA7FF1F',

  overlay: '#000000B3',
} as const;

// Holographic foil, used sparingly: price hero border, photo frames, active tab dot
export const holo = ['#7AF2FF', '#A88BFF', '#FF8BD8', '#FFD38B'] as const;

export const fonts = {
  display: 'SpaceGrotesk_700Bold',
  displayMedium: 'SpaceGrotesk_500Medium',
  body: 'Inter_400Regular',
  bodyMedium: 'Inter_500Medium',
  bodySemiBold: 'Inter_600SemiBold',
  bodyBold: 'Inter_700Bold',
} as const;

export const radius = { sm: 8, md: 12, lg: 18, xl: 24, pill: 999 } as const;
export const space = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

export type Theme = typeof colors;

// Kept as a hook so screens don't need to change if a light theme comes back later
export function useTheme(): Theme {
  return colors;
}

export const statusColors = {
  UNLISTED: { fg: '#B6BED0', bg: '#B6BED01A' },
  LISTED: { fg: colors.info, bg: colors.infoSoft },
  SOLD: { fg: colors.primary, bg: colors.primarySoft },
} as const;

export const statusLabels = {
  UNLISTED: 'Unlisted',
  LISTED: 'Listed',
  SOLD: 'Sold',
} as const;
