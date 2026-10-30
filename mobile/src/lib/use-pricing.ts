import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';

import { CardFormValues, fromFormValues } from '@/components/card-form';
import { PriceQuote, quoteCard, quoteRequestFrom } from './api';

// The form fields that change which comp a card matches; editing any of them makes the quote stale
export const pricingKey = (v: CardFormValues) =>
  [v.player, v.year, v.manufacturer, v.set, v.cardNumber, v.parallel, v.isAutograph, v.gradingCompany, v.grade]
    .map((x) => String(x).trim().toLowerCase())
    .join('|');

// Live price quote for a card form: re-price after edits, or pin to a comp the user picked.
export function useCardPricing(form: CardFormValues | null, onPriced?: (quote: PriceQuote) => void) {
  const [quote, setQuote] = useState<PriceQuote | null>(null);
  const [pricedKey, setPricedKey] = useState<string | null>(null);
  const [seedError, setSeedError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: ({ values, compProductId }: { values: CardFormValues; compProductId?: string | null }) => {
      const parsed = fromFormValues(values);
      if (!parsed.ok) throw new Error(parsed.error);
      return quoteCard(quoteRequestFrom(parsed.fields, compProductId));
    },
    onSuccess: (result, { values }) => {
      setQuote(result);
      setPricedKey(pricingKey(values));
      onPriced?.(result);
    },
  });

  return {
    quote,
    isPending: mutation.isPending,
    error: mutation.isError ? mutation.error.message : seedError,
    stale: !!form && pricedKey != null && pricingKey(form) !== pricedKey,
    // Seed with a quote that came from elsewhere (the identify response)
    seed(initial: PriceQuote, values: CardFormValues, error?: string | null) {
      setQuote(initial);
      setSeedError(error ?? null);
      setPricedKey(pricingKey(values));
    },
    reprice(compProductId?: string | null) {
      if (!form) return;
      setSeedError(null);
      mutation.mutate({ values: form, compProductId });
    },
    reset() {
      setQuote(null);
      setPricedKey(null);
      setSeedError(null);
      mutation.reset();
    },
  };
}
