import { File } from 'expo-file-system';

import { API_KEY, API_URL, isConfigured } from './config';

export type CardStatus = 'UNLISTED' | 'LISTED' | 'SOLD';
export const CARD_STATUSES: CardStatus[] = ['UNLISTED', 'LISTED', 'SOLD'];

export interface CardIdentification {
  player: string | null;
  year: number | null;
  manufacturer: string | null;
  set: string | null;
  cardNumber: string | null;
  parallel: string | null;
  serialNumber: string | null;
  isRookie: boolean;
  isAutograph: boolean;
  isRelic: boolean;
  gradingCompany: string | null;
  grade: number | null;
  conditionNotes: string | null;
  confidence: number;
  suggestedEbayTitle: string;
  // AI's read of how a raw card would grade; null for slabs
  gradeOutlook?: GradeOutlook | null;
  // Grading company cert number from the slab label
  certNumber?: string | null;
}

// Probabilities (sum to 1) that a raw card grades PSA 10 / 9 / 8 / 7 or lower
export interface GradeOutlook {
  gem10: number;
  mint9: number;
  nearMint8: number;
  lower: number;
  summary: string | null;
}

export type PriceTierKey =
  | 'raw'
  | 'g1'
  | 'g2'
  | 'g3'
  | 'g4'
  | 'g5'
  | 'g6'
  | 'g7'
  | 'g8'
  | 'g9'
  | 'g95'
  | 'sgc10'
  | 'cgc10'
  | 'psa10'
  | 'bgs10';

export interface PriceTier {
  key: PriceTierKey;
  label: string;
  price: number | null;
}

export interface CompAlternative {
  productId: string;
  productName: string;
  setName: string;
  score: number;
}

export interface PriceQuote {
  suggestedPrice: number | null;
  selectedTier: PriceTierKey | null;
  netProceeds: number | null;
  compProductId: string | null;
  compProductName: string | null;
  compSetName: string | null;
  compUrl: string | null;
  matchConfidence: number | null;
  isManualPick: boolean;
  pricedAt: string | null;
  tiers: PriceTier[];
  alternatives: CompAlternative[];
  // Sales SportsCardsPro has recorded for the card (all grades); the comps come from these
  salesVolume?: number | null;
}

export interface PriceQuoteRequest {
  player: string | null;
  year: number | null;
  manufacturer: string | null;
  set: string | null;
  cardNumber: string | null;
  parallel: string | null;
  isAutograph: boolean;
  gradingCompany: string | null;
  grade: number | null;
  compProductId?: string | null;
}

export interface IdentifyResponse {
  card: CardIdentification;
  price: PriceQuote;
  // Set when pricing failed but identification succeeded
  priceError: string | null;
  frontImagePath: string;
  // Null for a front-only (show mode) scan
  backImagePath: string | null;
  aiRawJson: string;
}

export interface CardFields {
  player: string | null;
  year: number | null;
  manufacturer: string | null;
  set: string | null;
  cardNumber: string | null;
  parallel: string | null;
  serialNumber: string | null;
  isRookie: boolean;
  isAutograph: boolean;
  isRelic: boolean;
  gradingCompany: string | null;
  grade: number | null;
  certNumber: string | null;
  conditionNotes: string | null;
  ebayTitle: string;
  suggestedPrice: number | null;
  costBasis: number | null;
}

export interface CreateCardRequest extends CardFields {
  confidence: number | null;
  compProductId: string | null;
  status?: CardStatus;
  frontImagePath: string | null;
  backImagePath: string | null;
  aiRawJson: string | null;
  purchaseSource?: string | null;
  purchasedAt?: string | null;
  gradingCost?: number | null;
}

export interface Card extends CardFields {
  id: number;
  confidence: number | null;
  compProductId: string | null;
  status: CardStatus;
  listedPrice: number | null;
  soldPrice: number | null;
  soldAt: string | null;
  frontImageUrl: string | null;
  backImageUrl: string | null;
  purchaseSource: string | null;
  purchasedAt: string | null;
  gradingCost: number | null;
  saleShippingCost: number | null;
  // Latest comp from the portfolio price worker
  marketPrice: number | null;
  marketPriceAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export type CardPatch = Partial<
  CardFields & {
    status: CardStatus;
    listedPrice: number | null;
    soldPrice: number | null;
    compProductId: string | null;
    purchaseSource: string | null;
    purchasedAt: string | null;
    gradingCost: number | null;
    saleShippingCost: number | null;
  }
>;

export class ApiError extends Error {
  constructor(
    message: string,
    public status?: number,
    // Machine-readable error code from the API, e.g. USAGE_LIMIT / PLAN_LIMIT (HTTP 402)
    public code?: string,
  ) {
    super(message);
  }
}

// Body of an HTTP 402 from the API: a Claude call would pass the monthly AI limit (USAGE_LIMIT),
// or a plan limit such as active watchlists was hit (PLAN_LIMIT)
export interface LimitReached {
  code: 'USAGE_LIMIT' | 'PLAN_LIMIT';
  error: string;
  plan: 'free' | 'pro';
  usedUsd?: number;
  limitUsd?: number;
  resetsAt?: string;
  feature?: string;
  limit?: number;
}

// The root layout registers these: it opens the paywall on a 402 and refreshes the usage meter
// after AI calls, so individual screens don't have to handle either.
const listeners: { onLimitReached?: (info: LimitReached) => void; onAiCall?: () => void } = {};

export function setApiListeners(next: typeof listeners) {
  listeners.onLimitReached = next.onLimitReached;
  listeners.onAiCall = next.onAiCall;
}

export const isUsageLimit = (e: unknown) => e instanceof ApiError && e.status === 402;

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  if (!isConfigured) {
    throw new ApiError('API not configured. Set EXPO_PUBLIC_API_URL and EXPO_PUBLIC_API_KEY in mobile/.env, then restart Expo.');
  }

  let response: Response;
  try {
    response = await fetch(`${API_URL}${path}`, {
      ...init,
      headers: { 'X-Api-Key': API_KEY, Accept: 'application/json', ...init.headers },
    });
  } catch (e) {
    const detail = e instanceof Error ? e.message : String(e);
    console.warn(`fetch ${init.method ?? 'GET'} ${path} failed:`, detail);
    throw new ApiError(`Can't reach the API at ${API_URL} (${detail}). Is it running, and is your phone on the same Wi-Fi?`);
  }

  if (!response.ok) {
    let message = `Request failed (${response.status})`;
    let code: string | undefined;
    try {
      const body = await response.json();
      if (body?.error) message = body.error;
      else if (body?.title) message = body.title;
      if (typeof body?.code === 'string') code = body.code;
      if (response.status === 402 && (code === 'USAGE_LIMIT' || code === 'PLAN_LIMIT')) {
        listeners.onLimitReached?.(body as LimitReached);
      }
    } catch {
      // non-JSON error body; keep the generic message
    }
    if (response.status === 401) message = 'The API rejected the key. Check EXPO_PUBLIC_API_KEY.';
    throw new ApiError(message, response.status, code);
  }
  return response;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

// For endpoints that call Claude: the usage meter is refreshed afterwards, even on failure
// (a refused or malformed answer is still billed)
async function aiRequest<T>(path: string, init: RequestInit): Promise<T> {
  try {
    return await request<T>(path, init);
  } finally {
    listeners.onAiCall?.();
  }
}

// ---- Plans and AI usage ----

export interface Usage {
  plan: 'free' | 'pro';
  usedUsd: number;
  limitUsd: number;
  percentUsed: number;
  resetsAt: string;
  scansThisMonth: number;
}

export interface Plan {
  id: 'free' | 'pro';
  name: string;
  monthlyPriceUsd: number;
  yearlyPriceUsd: number;
  aiMonthlyLimitUsd: number;
  maxWatchlists: number;
  features: string[];
}

export const getUsage = () => request<Usage>('/api/usage');
export const getPlans = () => request<Plan[]>('/api/plans');

function jsonInit(method: string, body: unknown): RequestInit {
  return { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) };
}

// backUri is optional: show mode scans the front only
export function identifyCard(frontUri: string, backUri?: string | null): Promise<IdentifyResponse> {
  const form = new FormData();
  // expo/fetch (the global fetch in SDK 58) rejects RN-style { uri, name, type } parts; it needs real Blobs
  form.append('front', new File(frontUri), 'front.jpg');
  if (backUri) form.append('back', new File(backUri), 'back.jpg');
  return aiRequest<IdentifyResponse>('/api/cards/identify', { method: 'POST', body: form });
}

// Re-prices from card fields (after edits), or pins to a comp the user picked
export const quoteCard = (req: PriceQuoteRequest) => request<PriceQuote>('/api/cards/price', jsonInit('POST', req));

export function quoteRequestFrom(fields: CardFields, compProductId?: string | null): PriceQuoteRequest {
  return {
    player: fields.player,
    year: fields.year,
    manufacturer: fields.manufacturer,
    set: fields.set,
    cardNumber: fields.cardNumber,
    parallel: fields.parallel,
    isAutograph: fields.isAutograph,
    gradingCompany: fields.gradingCompany,
    grade: fields.grade,
    compProductId: compProductId ?? null,
  };
}

export const createCard = (card: CreateCardRequest) => request<Card>('/api/cards', jsonInit('POST', card));

export const getCards = (status?: CardStatus) =>
  request<Card[]>(`/api/cards${status ? `?status=${status}` : ''}`);

export const getCard = (id: number) => request<Card>(`/api/cards/${id}`);

export const updateCard = (id: number, patch: CardPatch) => request<Card>(`/api/cards/${id}`, jsonInit('PATCH', patch));

// For <Image source={...}>: image endpoints need the API key header too
export function imageSource(relativeUrl: string | null) {
  if (!relativeUrl) return null;
  return { uri: `${API_URL}${relativeUrl}`, headers: { 'X-Api-Key': API_KEY } };
}

// ---- Market: what the card is listed for on eBay right now (asking prices, not sales) ----

export interface MarketListing {
  itemId: string;
  title: string;
  price: number;
  shipping: number | null;
  listingType: 'AUCTION' | 'FIXED_PRICE';
  endTime: string | null;
  url: string;
  imageUrl: string | null;
}

export interface MarketSnapshot {
  query: string;
  activeCount: number;
  lowestBuyItNow: number | null;
  medianBuyItNow: number | null;
  highestBuyItNow: number | null;
  auctionCount: number;
  cheapest: MarketListing[];
}

export const getMarket = (req: PriceQuoteRequest) => request<MarketSnapshot>('/api/cards/market', jsonInit('POST', req));

// ---- Lots ----

export interface LotCard {
  player: string | null;
  year: number | null;
  manufacturer: string | null;
  set: string | null;
  cardNumber: string | null;
  parallel: string | null;
  isRookie: boolean;
  isAutograph: boolean;
  gradingCompany: string | null;
  grade: number | null;
  confidence: number;
  position: string | null;
}

export type Verdict = 'STEAL' | 'BUY' | 'FAIR' | 'OVERPRICED' | 'UNKNOWN';

export interface FlipAnalysis {
  marketPrice: number | null;
  netProceeds: number | null;
  askingPrice: number;
  buyShipping: number;
  totalCost: number;
  netProfit: number | null;
  netProfitPct: number | null;
  pctOfMarket: number | null;
  maxBuyPrice: number | null;
  openingOffer: number | null;
  meetsTargets: boolean;
  verdict: Verdict;
  reasons: string[];
}

export interface LotAnalysis {
  cards: { card: LotCard; price: PriceQuote; netProceeds: number | null; priceError: string | null }[];
  notes: string | null;
  totalMarketValue: number;
  pricedCount: number;
  flip: FlipAnalysis | null;
  photoPath: string;
}

export function analyzeLot(
  photoUri: string,
  opts: { askingPrice?: number | null; shipping?: number | null; minProfit: number; minProfitPct: number },
): Promise<LotAnalysis> {
  const form = new FormData();
  form.append('photo', new File(photoUri), 'lot.jpg');
  if (opts.askingPrice != null) form.append('askingPrice', String(opts.askingPrice));
  if (opts.shipping != null) form.append('shipping', String(opts.shipping));
  form.append('minProfit', String(opts.minProfit));
  form.append('minProfitPct', String(opts.minProfitPct));
  return aiRequest<LotAnalysis>('/api/lots/analyze', { method: 'POST', body: form });
}

// ---- Watchlists ----

export type WatchlistListingType = 'ANY' | 'AUCTION' | 'FIXED_PRICE';

export interface WatchlistInput {
  name: string;
  query: string;
  listingType: WatchlistListingType;
  maxPrice: number | null;
  minProfit: number;
  minProfitPct: number;
  gradedOnly: boolean;
  notify: boolean;
  isActive: boolean;
}

export interface Watchlist extends WatchlistInput {
  id: number;
  lastRunAt: string | null;
  createdAt: string;
}

export type HitStatus = 'NEW' | 'SAVED' | 'DISMISSED' | 'BOUGHT';

export interface WatchlistHit {
  id: number;
  watchlistId: number;
  watchlistName: string | null;
  itemId: string;
  title: string;
  listingType: 'AUCTION' | 'FIXED_PRICE' | null;
  price: number;
  shippingCost: number | null;
  shippingEstimated: boolean;
  endTime: string | null;
  listingUrl: string | null;
  imageUrl: string | null;
  compProductName: string | null;
  compSetName: string | null;
  compPrice: number | null;
  compTier: PriceTierKey | null;
  matchConfidence: number | null;
  netProfit: number | null;
  netProfitPct: number | null;
  maxBuyPrice: number | null;
  isProfitable: boolean;
  status: HitStatus;
  createdAt: string;
}

export const getWatchlists = () => request<Watchlist[]>('/api/watchlists');
export const createWatchlist = (w: WatchlistInput) => request<Watchlist>('/api/watchlists', jsonInit('POST', w));
export const updateWatchlist = (id: number, w: WatchlistInput) =>
  request<Watchlist>(`/api/watchlists/${id}`, jsonInit('PUT', w));
export const deleteWatchlist = (id: number) => request<void>(`/api/watchlists/${id}`, { method: 'DELETE' });

export function getHits(opts: { status?: HitStatus; all?: boolean; watchlistId?: number } = {}) {
  const params = new URLSearchParams();
  if (opts.status) params.set('status', opts.status);
  if (opts.all) params.set('all', 'true');
  if (opts.watchlistId) params.set('watchlistId', String(opts.watchlistId));
  const qs = params.toString();
  return request<WatchlistHit[]>(`/api/watchlists/hits${qs ? `?${qs}` : ''}`);
}

export const updateHitStatus = (id: number, status: HitStatus) =>
  request<WatchlistHit>(`/api/watchlists/hits/${id}`, jsonInit('PATCH', { status }));

// ---- Market deals (the shared deal finder that watches WATCHED_PLAYERS) ----

export interface MarketDeal {
  itemId: string;
  title: string;
  listingType: string | null;
  price: number;
  shippingCost: number | null;
  condition: string | null;
  endTime: string | null;
  url: string;
  compName: string | null;
  compSetName: string | null;
  compUrl: string | null;
  compPrice: number;
  discountPct: number;
  netProfit: number;
  netProfitPct: number;
  matchConfidence: number;
  foundAt: string;
}

export const getMarketDeals = () => request<MarketDeal[]>('/api/deals');

// ---- Push ----

export const registerPushToken = (token: string, platform: string) =>
  request<void>('/api/push-tokens', jsonInit('POST', { token, platform }));
export const sendTestPush = () => request<{ sent: number }>('/api/push-tokens/test', { method: 'POST' });

// ---- Portfolio ----

export interface Portfolio {
  held: { count: number; pricedCount: number; marketValue: number; costBasis: number; unrealizedProfit: number };
  sold: { count: number; revenue: number; fees: number; shipping: number; costBasis: number; netProfit: number };
  monthly: { month: string; sold: number; revenue: number; netProfit: number; spent: number }[];
  movers: {
    cardId: number;
    title: string;
    player: string | null;
    from: number;
    to: number;
    changePct: number;
    frontImageUrl: string | null;
  }[];
}

export const getPortfolio = () => request<Portfolio>('/api/portfolio');

export async function getPortfolioCsv(): Promise<string> {
  const response = await send('/api/portfolio/export', { headers: { Accept: 'text/csv' } });
  return response.text();
}

// ---- Listing kit & price history ----

export interface ListingKit {
  title: string;
  category: string;
  condition: string;
  itemSpecifics: { name: string; value: string }[];
  description: string;
  pricing: {
    marketPrice: number | null;
    buyItNowPrice: number | null;
    autoAcceptOffer: number | null;
    autoDeclineOffer: number | null;
    breakEvenPrice: number | null;
    netAtBuyItNow: number | null;
    profitAtBuyItNow: number | null;
    format: string;
    strategy: string;
  };
  sellUrl: string;
}

export const getListingKit = (id: number) => request<ListingKit>(`/api/cards/${id}/listing-kit`);
export const getCardHistory = (id: number) =>
  request<{ price: number; capturedAt: string }[]>(`/api/cards/${id}/history`);
