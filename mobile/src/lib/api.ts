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
}

export type PriceTierKey = 'raw' | 'g7' | 'g8' | 'g9' | 'g95' | 'psa10' | 'bgs10';

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
  backImagePath: string;
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
  createdAt: string;
  updatedAt: string;
}

export type CardPatch = Partial<
  CardFields & { status: CardStatus; listedPrice: number | null; soldPrice: number | null; compProductId: string | null }
>;

export class ApiError extends Error {
  constructor(message: string, public status?: number) {
    super(message);
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
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
    try {
      const body = await response.json();
      if (body?.error) message = body.error;
      else if (body?.title) message = body.title;
    } catch {
      // non-JSON error body; keep the generic message
    }
    if (response.status === 401) message = 'The API rejected the key. Check EXPO_PUBLIC_API_KEY.';
    throw new ApiError(message, response.status);
  }

  return (await response.json()) as T;
}

function jsonInit(method: string, body: unknown): RequestInit {
  return { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) };
}

export function identifyCard(frontUri: string, backUri: string): Promise<IdentifyResponse> {
  const form = new FormData();
  // expo/fetch (the global fetch in SDK 58) rejects RN-style { uri, name, type } parts; it needs real Blobs
  form.append('front', new File(frontUri), 'front.jpg');
  form.append('back', new File(backUri), 'back.jpg');
  return request<IdentifyResponse>('/api/cards/identify', { method: 'POST', body: form });
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
