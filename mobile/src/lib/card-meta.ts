import { TCG_GAMES } from './api';

// The fields a card summary needs; works for identify results, saved cards, lot cards and form values
export interface CardMetaInput {
  category?: string | null;
  game?: string | null;
  year?: number | string | null;
  manufacturer?: string | null;
  set?: string | null;
  cardNumber?: string | null;
  parallel?: string | null;
  rarity?: string | null;
  language?: string | null;
}

const present = (v: unknown): v is string | number => v != null && String(v).trim() !== '';

// "2011 · Topps · Update · #US175" for sports, "Pokémon · Base Set · 4/102 · 1st Edition · Rare Holo" for TCG
export function cardMetaParts(c: CardMetaInput): string[] {
  if (c.category === 'tcg') {
    const game = TCG_GAMES.find((g) => g.key === c.game);
    const english = !present(c.language) || /^en(glish)?$/i.test(String(c.language).trim());
    return [
      game && game.key !== 'other' ? game.short : null,
      c.set,
      c.cardNumber,
      c.parallel,
      c.rarity,
      english ? null : c.language,
    ]
      .filter(present)
      .map(String);
  }
  return [c.year, c.manufacturer, c.set, c.parallel, present(c.cardNumber) ? `#${c.cardNumber}` : null].filter(present).map(String);
}

export const cardMeta = (c: CardMetaInput, separator = ' · ') => cardMetaParts(c).join(separator);

// What to call the card when there's no name: "Unknown player" vs "Unknown card"
export const unknownName = (c: { category?: string | null }) => (c.category === 'tcg' ? 'Unknown card' : 'Unknown player');
