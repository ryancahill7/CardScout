import { useQuery } from '@tanstack/react-query';

import { getPlans, getUsage, LimitReached, Plan } from './api';
import { isConfigured } from './config';

export const USAGE_QUERY_KEY = ['usage'] as const;

// Share of the monthly AI limit at which free users see the warning banner (matches Billing:WarnAtPercent)
export const WARN_AT_PERCENT = 80;

export function useUsage() {
  return useQuery({ queryKey: USAGE_QUERY_KEY, queryFn: getUsage, enabled: isConfigured, staleTime: 60_000 });
}

// Shown if /api/plans can't be reached, so the paywall always renders. The API's PlanCatalog is the source of truth.
const FALLBACK_PLANS: Plan[] = [
  {
    id: 'free',
    name: 'Free',
    monthlyPriceUsd: 0,
    yearlyPriceUsd: 0,
    aiMonthlyLimitUsd: 2,
    maxWatchlists: 3,
    features: [
      'AI card and lot scans up to a monthly limit',
      'Live market prices for every grade',
      'Show mode flip verdicts',
      'Deals feed and active eBay listings',
      '3 eBay watchlists with alerts',
      'Collection and P&L tracking',
    ],
  },
  {
    id: 'pro',
    name: 'Pro',
    monthlyPriceUsd: 14.99,
    yearlyPriceUsd: 119.99,
    aiMonthlyLimitUsd: 20,
    maxWatchlists: 25,
    features: [
      'Unlimited AI card scans (fair use)',
      'Most accurate AI model for identification',
      'Unlimited lot and binder-page analysis',
      '25 eBay watchlists with instant alerts',
      'Portfolio price alerts',
      'Everything in Free',
    ],
  },
];

export function usePlans() {
  const query = useQuery({ queryKey: ['plans'], queryFn: getPlans, enabled: isConfigured, staleTime: 3_600_000 });
  return query.data && query.data.length > 0 ? query.data : FALLBACK_PLANS;
}

export function formatResetDate(iso: string | undefined) {
  if (!iso) return 'the 1st of next month';
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });
}

// Paywall route params for a 402 from the API
export function paywallParams(info: LimitReached): Record<string, string> {
  return {
    reason: info.code === 'PLAN_LIMIT' ? 'plan' : 'limit',
    message: info.error,
    resetsAt: info.resetsAt ?? '',
    plan: info.plan,
  };
}
