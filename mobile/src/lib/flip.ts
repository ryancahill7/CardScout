// Buy-or-pass math. Mirrors CardScout.Core/Flip/FlipAnalyzer.cs so show mode can update the
// verdict on every keystroke without a round trip; keep the two in step.
import { FlipAnalysis, GradeOutlook, PriceTier, PriceTierKey, Verdict } from './api';
import { netProceeds } from './money';
import { colors } from './theme';

export interface FlipTargets {
  minProfit: number;
  minProfitPct: number;
}

const STEAL_ROI_PCT = 50;
const FAIR_MAX_PCT_OF_MARKET = 105;
const LOW_CONFIDENCE = 0.7;

const round2 = (n: number) => Math.round(n * 100) / 100;
const round1 = (n: number) => Math.round(n * 10) / 10;

// Offers people actually say out loud: whole dollars under $20, then $5 and $10 steps
export function roundDownNice(value: number) {
  if (value < 1) return round2(value);
  if (value < 20) return Math.floor(value);
  if (value < 100) return Math.floor(value / 5) * 5;
  return Math.floor(value / 10) * 10;
}

export function analyzeFlip(
  marketPrice: number | null | undefined,
  askingPrice: number,
  buyShipping: number,
  targets: FlipTargets,
  matchConfidence?: number | null,
): FlipAnalysis {
  const totalCost = askingPrice + buyShipping;
  if (marketPrice == null || marketPrice <= 0) {
    return {
      marketPrice: null,
      netProceeds: null,
      askingPrice,
      buyShipping,
      totalCost,
      netProfit: null,
      netProfitPct: null,
      pctOfMarket: null,
      maxBuyPrice: null,
      openingOffer: null,
      meetsTargets: false,
      verdict: 'UNKNOWN',
      reasons: ["No market price for this card, so there's nothing to compare against."],
    };
  }

  const net = round2(netProceeds(marketPrice));
  const profit = round2(net - totalCost);
  const roi = totalCost === 0 ? 0 : round1((profit / totalCost) * 100);
  const pctOfMarket = round1((askingPrice / marketPrice) * 100);
  const meets = profit >= targets.minProfit && roi >= targets.minProfitPct;

  const maxTotal = Math.min(net - targets.minProfit, net / (1 + targets.minProfitPct / 100));
  const maxBuy = maxTotal - buyShipping > 0 ? Math.floor((maxTotal - buyShipping) * 100) / 100 : null;
  const opening = maxBuy != null && maxBuy > 0 ? roundDownNice(maxBuy * 0.85) : null;

  const verdict: Verdict =
    meets && roi >= STEAL_ROI_PCT ? 'STEAL' : meets ? 'BUY' : pctOfMarket <= FAIR_MAX_PCT_OF_MARKET ? 'FAIR' : 'OVERPRICED';

  const reasons = [
    `Sells for about $${marketPrice.toFixed(2)}, which nets $${net.toFixed(2)} after eBay fees.`,
    profit >= 0
      ? `Flipping it makes $${profit.toFixed(2)} (${roi}% on $${totalCost.toFixed(2)}).`
      : `Flipping it loses $${(-profit).toFixed(2)}.`,
  ];
  if (!meets && maxBuy != null)
    reasons.push(`To hit your $${targets.minProfit} / ${targets.minProfitPct}% target, pay no more than $${maxBuy.toFixed(2)}.`);
  if (maxBuy == null) reasons.push('The market price is too low to flip at any price after fees.');
  if (matchConfidence != null && matchConfidence < LOW_CONFIDENCE)
    reasons.push(`Comp match is only ${Math.round(matchConfidence * 100)}% sure, so check it's the same card and parallel.`);

  return {
    marketPrice,
    netProceeds: net,
    askingPrice,
    buyShipping,
    totalCost,
    netProfit: profit,
    netProfitPct: roi,
    pctOfMarket,
    maxBuyPrice: maxBuy,
    openingOffer: opening,
    meetsTargets: meets,
    verdict,
    reasons,
  };
}

// Card-show buying: you pay a set share of the sold comp (dealers expect cash offers around
// 70-85% of comps), and the profit is what you'd get reselling it in cash (no fees) or on eBay.
export function analyzeShowBuy(
  marketPrice: number | null | undefined,
  askingPrice: number,
  show: { buyPct: number; resale: 'cash' | 'ebay' },
  matchConfidence?: number | null,
): FlipAnalysis {
  if (marketPrice == null || marketPrice <= 0) return analyzeFlip(null, askingPrice, 0, { minProfit: 0, minProfitPct: 0 });

  const pct = show.buyPct / 100;
  const net = round2(show.resale === 'cash' ? marketPrice : netProceeds(marketPrice));
  const profit = round2(net - askingPrice);
  const roi = askingPrice === 0 ? 0 : round1((profit / askingPrice) * 100);
  const pctOfMarket = round1((askingPrice / marketPrice) * 100);
  const maxBuy = round2(marketPrice * pct);
  const opening = roundDownNice(marketPrice * Math.max(0.5, pct - 0.1));

  const verdict: Verdict =
    askingPrice <= marketPrice * Math.max(0, pct - 0.15)
      ? 'STEAL'
      : askingPrice <= maxBuy
        ? 'BUY'
        : pctOfMarket <= FAIR_MAX_PCT_OF_MARKET
          ? 'FAIR'
          : 'OVERPRICED';

  const reasons = [
    `Sold comps are about $${marketPrice.toFixed(2)}. At ${show.buyPct}% you'd pay up to $${maxBuy.toFixed(2)}.`,
    `Asking is ${Math.round(pctOfMarket)}% of comps.`,
    show.resale === 'cash'
      ? `Resold for cash at comps, that's ${profit >= 0 ? `$${profit.toFixed(2)} profit` : `a $${(-profit).toFixed(2)} loss`} (${roi}%).`
      : `Resold on eBay it nets $${net.toFixed(2)} after fees: ${profit >= 0 ? `$${profit.toFixed(2)} profit` : `a $${(-profit).toFixed(2)} loss`} (${roi}%).`,
  ];
  if (matchConfidence != null && matchConfidence < LOW_CONFIDENCE)
    reasons.push(`Comp match is only ${Math.round(matchConfidence * 100)}% sure, so check it's the same card and grade.`);

  return {
    marketPrice,
    netProceeds: net,
    askingPrice,
    buyShipping: 0,
    totalCost: askingPrice,
    netProfit: profit,
    netProfitPct: roi,
    pctOfMarket,
    maxBuyPrice: maxBuy,
    openingOffer: opening,
    meetsTargets: askingPrice <= maxBuy,
    verdict,
    reasons,
  };
}

export const verdictStyle: Record<Verdict, { label: string; fg: string; bg: string; icon: string; blurb: string }> = {
  STEAL: { label: 'Steal', fg: colors.onPrimary, bg: colors.primary, icon: 'flame', blurb: 'Way under market. Buy it.' },
  BUY: { label: 'Buy', fg: colors.primary, bg: colors.primarySoft, icon: 'checkmark-circle', blurb: 'Within your buy price.' },
  FAIR: { label: 'Fair', fg: colors.warning, bg: colors.warningSoft, icon: 'remove-circle', blurb: 'Around market. Fine to keep, not a flip.' },
  OVERPRICED: { label: 'Pass', fg: colors.danger, bg: colors.dangerSoft, icon: 'close-circle', blurb: 'Priced above market.' },
  UNKNOWN: { label: 'No comp', fg: colors.textSecondary, bg: colors.surfaceRaised, icon: 'help-circle', blurb: 'No market price to judge by.' },
};

// ---- Grading ROI ----

export interface GradingService {
  id: string;
  name: string;
  fee: number;
  turnaround: string;
  // Which tier the service's top grade sells at: PSA 10 has its own comps; SGC 10 / BGS 9.5 trade nearer the 9.5 tier
  gemTier: PriceTierKey;
}

export interface GradeMix {
  gem10: number;
  mint9: number;
  nearMint8: number;
  lower: number;
}

export const GRADE_PRESETS: { key: string; label: string; mix: GradeMix }[] = [
  { key: 'gem', label: 'Gem-looking', mix: { gem10: 0.45, mint9: 0.4, nearMint8: 0.1, lower: 0.05 } },
  { key: 'clean', label: 'Clean', mix: { gem10: 0.15, mint9: 0.5, nearMint8: 0.25, lower: 0.1 } },
  { key: 'average', label: 'Average', mix: { gem10: 0.05, mint9: 0.3, nearMint8: 0.4, lower: 0.25 } },
  { key: 'rough', label: 'Rough', mix: { gem10: 0, mint9: 0.1, nearMint8: 0.3, lower: 0.6 } },
];

export const mixFromOutlook = (o: GradeOutlook): GradeMix => ({
  gem10: o.gem10,
  mint9: o.mint9,
  nearMint8: o.nearMint8,
  lower: o.lower,
});

export interface GradingResult {
  service: GradingService;
  totalCost: number;
  expectedNet: number;
  // Expected net from grading minus the net from selling raw today
  gainVsRaw: number;
  gemPrice: number | null;
  worthIt: boolean;
}

export function analyzeGrading(
  tiers: PriceTier[],
  mix: GradeMix,
  services: GradingService[],
  shippingPerCard: number,
): { rawNet: number | null; results: GradingResult[] } {
  const price = (key: PriceTierKey) => tiers.find((t) => t.key === key)?.price ?? null;
  const raw = price('raw');
  const rawNet = raw != null ? netProceeds(raw) : null;
  // A 7-or-lower slab rarely beats raw by much; fall back to raw when there's no 7 comp
  const low = price('g7') ?? raw ?? 0;
  const nine = price('g9');
  const eight = price('g8');

  const results = services.map((service) => {
    const gem = price(service.gemTier) ?? price('psa10');
    const sell = (p: number | null) => (p != null && p > 0 ? Math.max(0, netProceeds(p)) : 0);
    const expectedNet =
      mix.gem10 * sell(gem) + mix.mint9 * sell(nine ?? low) + mix.nearMint8 * sell(eight ?? low) + mix.lower * sell(low);
    const totalCost = service.fee + shippingPerCard;
    const gainVsRaw = round2(expectedNet - totalCost - (rawNet ?? 0));
    return { service, totalCost, expectedNet: round2(expectedNet), gainVsRaw, gemPrice: gem, worthIt: gainVsRaw > 0 };
  });
  return { rawNet: rawNet == null ? null : round2(rawNet), results };
}
