import { useQuery } from '@tanstack/react-query';
import * as Linking from 'expo-linking';
import * as WebBrowser from 'expo-web-browser';
import { useEffect } from 'react';
import { AppState } from 'react-native';

import { jsonInit, request } from './api';

// Subscriptions are paid on Stripe-hosted pages in the browser, not through in-app purchase.
// The API's /billing/* landing pages bounce back to the app's deep link when the user is done.

export type Plan = 'free' | 'pro';

export interface BillingStatus {
  plan: Plan;
  // Stripe subscription status (active, trialing, past_due, canceled...), null if never subscribed
  status: string | null;
  // ISO date; when a cancel-at-period-end subscription stops, otherwise the next renewal
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  // False until the server has Stripe keys; hide the Upgrade button then
  billingEnabled: boolean;
  yearlyAvailable: boolean;
}

export type BillingInterval = 'month' | 'year';

export const BILLING_STATUS_KEY = ['billing-status'] as const;

// Where the landing pages send the browser: exp://…/--/settings in Expo Go, cardscout://settings in a build.
// It points at an existing route so a stray deep link (Android) just lands on Settings.
const returnUrl = () => Linking.createURL('settings');

export const getBillingStatus = () => request<BillingStatus>('/api/billing/status');

// The auth session closes by itself when the landing page redirects to returnUrl. Ephemeral avoids
// iOS's "wants to use … to sign in" prompt, which would be confusing for a payment page.
async function openStripePage(url: string) {
  await WebBrowser.openAuthSessionAsync(url, returnUrl(), { preferEphemeralSession: true });
}

// Resolves once the browser closes (paid, canceled or dismissed); refetch the status afterwards
export async function openCheckout(interval: BillingInterval): Promise<void> {
  const { url } = await request<{ url: string }>('/api/billing/checkout', jsonInit('POST', { interval, returnUrl: returnUrl() }));
  await openStripePage(url);
}

export async function openBillingPortal(): Promise<void> {
  const { url } = await request<{ url: string }>('/api/billing/portal', jsonInit('POST', { returnUrl: returnUrl() }));
  await openStripePage(url);
}

// Billing status that refreshes whenever the app comes back to the foreground (e.g. after paying)
export function useBillingStatus() {
  const query = useQuery({ queryKey: BILLING_STATUS_KEY, queryFn: getBillingStatus, staleTime: 30_000 });
  const { refetch } = query;
  useEffect(() => {
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') void refetch();
    });
    return () => sub.remove();
  }, [refetch]);
  return query;
}
