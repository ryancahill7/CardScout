import { Linking } from 'react-native';

import { API_KEY, API_URL } from './config';

// Placeholder for the Stripe billing client; the real one replaces this file with the same exports.

export interface BillingStatus {
  plan: 'free' | 'pro';
  status: string;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
}

const FREE: BillingStatus = { plan: 'free', status: 'none', currentPeriodEnd: null, cancelAtPeriodEnd: false };

async function call(path: string, init: RequestInit = {}): Promise<Response> {
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: { 'X-Api-Key': API_KEY, Accept: 'application/json', ...init.headers },
  });
  if (!response.ok) {
    let message = `Request failed (${response.status})`;
    try {
      const body = await response.json();
      if (body?.error) message = body.error;
    } catch {
      // keep the generic message
    }
    throw new Error(message);
  }
  return response;
}

// Starts a Stripe Checkout session on the API and opens it in the browser
export async function openCheckout(interval: 'month' | 'year'): Promise<void> {
  const response = await call('/api/billing/checkout', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ interval }),
  });
  const { url } = (await response.json()) as { url?: string };
  if (!url) throw new Error('Checkout is not available yet.');
  await Linking.openURL(url);
}

export async function getBillingStatus(): Promise<BillingStatus> {
  try {
    const response = await call('/api/billing/status');
    return (await response.json()) as BillingStatus;
  } catch {
    return FREE;
  }
}
