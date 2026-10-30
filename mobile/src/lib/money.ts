// Mirrors CardScout.Core/Pricing/SellerFees.cs so the app can recompute net
// proceeds instantly when the user types a different price.
export const EBAY_FEE_RATE = 0.1325;
export const EBAY_PER_ORDER_FEE = 0.4;
export const PACKAGING_COST = 0.75;

export const netProceeds = (salePrice: number) => salePrice * (1 - EBAY_FEE_RATE) - EBAY_PER_ORDER_FEE - PACKAGING_COST;

export function formatMoney(value: number | null | undefined, { cents = true } = {}) {
  if (value == null) return '—';
  const sign = value < 0 ? '-' : '';
  const abs = Math.abs(value);
  return `${sign}$${abs.toLocaleString('en-US', {
    minimumFractionDigits: cents ? 2 : 0,
    maximumFractionDigits: cents ? 2 : 0,
  })}`;
}

export function formatSignedMoney(value: number) {
  return `${value >= 0 ? '+' : ''}${formatMoney(value)}`;
}
