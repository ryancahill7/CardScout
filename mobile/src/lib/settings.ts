import { File, Paths } from 'expo-file-system';
import { useSyncExternalStore } from 'react';

import { FlipTargets, GradingService } from './flip';

// App preferences, saved to a JSON file on the device
export interface Settings {
  // Profit bar for show mode and lots (watchlists have their own per list)
  targets: FlipTargets;
  // Grading fees change often, so they're editable; defaults are rough list prices
  gradingServices: GradingService[];
  // Shipping + insurance to the grader, per card
  gradingShipping: number;
  // Card shows: the share of the sold comp you're willing to pay, and how you'll resell
  show: ShowSettings;
}

export interface ShowSettings {
  // e.g. 80 = pay up to 80% of comps
  buyPct: number;
  // 'cash' = you resell in person with no fees; 'ebay' = fees come off the comp
  resale: 'cash' | 'ebay';
}

export const DEFAULT_SETTINGS: Settings = {
  targets: { minProfit: 5, minProfitPct: 25 },
  gradingServices: [
    { id: 'psa-value', name: 'PSA Value', fee: 32.99, turnaround: '~65 business days', gemTier: 'psa10' },
    { id: 'psa-regular', name: 'PSA Regular', fee: 74.99, turnaround: '~10 business days', gemTier: 'psa10' },
    { id: 'sgc', name: 'SGC Standard', fee: 22, turnaround: '~15 business days', gemTier: 'g95' },
    { id: 'bgs', name: 'BGS Base', fee: 25, turnaround: '~30 business days', gemTier: 'g95' },
  ],
  gradingShipping: 5,
  show: { buyPct: 80, resale: 'cash' },
};

const file = new File(Paths.document, 'cardscout-settings.json');

function load(): Settings {
  try {
    if (!file.exists) return DEFAULT_SETTINGS;
    const saved = JSON.parse(file.textSync()) as Partial<Settings>;
    return {
      ...DEFAULT_SETTINGS,
      ...saved,
      targets: { ...DEFAULT_SETTINGS.targets, ...saved.targets },
      show: { ...DEFAULT_SETTINGS.show, ...saved.show },
    };
  } catch (e) {
    console.warn('Could not read settings, using defaults', e);
    return DEFAULT_SETTINGS;
  }
}

let current: Settings | null = null;
const listeners = new Set<() => void>();

function get(): Settings {
  current ??= load();
  return current;
}

export function updateSettings(patch: Partial<Settings>) {
  current = { ...get(), ...patch };
  listeners.forEach((l) => l());
  const json = JSON.stringify(current);
  try {
    if (!file.exists) file.create();
    void file.write(json).catch((e) => console.warn('Could not save settings', e));
  } catch (e) {
    console.warn('Could not save settings', e);
  }
}

export function useSettings(): Settings {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    get,
    get,
  );
}
