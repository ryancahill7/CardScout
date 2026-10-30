import { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';

import { CardFields } from '@/lib/api';
import { space } from '@/lib/theme';
import { Field, SectionTitle, Surface, ToggleChip } from './ui';

// Text inputs hold strings; numbers are parsed when the form is submitted.
export interface CardFormValues {
  player: string;
  year: string;
  manufacturer: string;
  set: string;
  cardNumber: string;
  parallel: string;
  serialNumber: string;
  isRookie: boolean;
  isAutograph: boolean;
  isRelic: boolean;
  gradingCompany: string;
  grade: string;
  conditionNotes: string;
  ebayTitle: string;
  suggestedPrice: string;
  costBasis: string;
}

const str = (v: string | number | null | undefined) => (v == null ? '' : String(v));

export function toFormValues(c: Omit<CardFields, 'ebayTitle'> & { ebayTitle: string }): CardFormValues {
  return {
    player: str(c.player),
    year: str(c.year),
    manufacturer: str(c.manufacturer),
    set: str(c.set),
    cardNumber: str(c.cardNumber),
    parallel: str(c.parallel),
    serialNumber: str(c.serialNumber),
    isRookie: c.isRookie,
    isAutograph: c.isAutograph,
    isRelic: c.isRelic,
    gradingCompany: str(c.gradingCompany),
    grade: str(c.grade),
    conditionNotes: str(c.conditionNotes),
    ebayTitle: c.ebayTitle,
    suggestedPrice: c.suggestedPrice == null ? '' : c.suggestedPrice.toFixed(2),
    costBasis: c.costBasis == null ? '' : c.costBasis.toFixed(2),
  };
}

export type ParseResult = { ok: true; fields: CardFields } | { ok: false; error: string };

export function parseNumber(value: string): number | null | undefined {
  const trimmed = value.replace(/[$,]/g, '').trim();
  if (trimmed === '') return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? n : undefined; // undefined = invalid
}

export function fromFormValues(v: CardFormValues): ParseResult {
  const text = (s: string) => (s.trim() === '' ? null : s.trim());

  const title = v.ebayTitle.trim();
  if (title.length === 0) return { ok: false, error: 'The eBay title is required.' };
  if (title.length > 80) return { ok: false, error: 'The eBay title must be 80 characters or fewer.' };

  const numbers = {
    year: parseNumber(v.year),
    grade: parseNumber(v.grade),
    suggestedPrice: parseNumber(v.suggestedPrice),
    costBasis: parseNumber(v.costBasis),
  };
  for (const [name, value] of Object.entries(numbers)) {
    if (value === undefined) return { ok: false, error: `${labelFor(name)} must be a number.` };
  }
  if (numbers.year != null && !Number.isInteger(numbers.year)) return { ok: false, error: 'Year must be a whole number.' };

  return {
    ok: true,
    fields: {
      player: text(v.player),
      year: numbers.year ?? null,
      manufacturer: text(v.manufacturer),
      set: text(v.set),
      cardNumber: text(v.cardNumber)?.replace(/^#/, '') ?? null,
      parallel: text(v.parallel),
      serialNumber: text(v.serialNumber),
      isRookie: v.isRookie,
      isAutograph: v.isAutograph,
      isRelic: v.isRelic,
      gradingCompany: text(v.gradingCompany)?.toUpperCase() ?? null,
      grade: numbers.grade ?? null,
      conditionNotes: text(v.conditionNotes),
      ebayTitle: title,
      suggestedPrice: numbers.suggestedPrice ?? null,
      costBasis: numbers.costBasis ?? null,
    },
  };
}

function labelFor(name: string) {
  return { year: 'Year', grade: 'Grade', suggestedPrice: 'Your price', costBasis: 'Your cost' }[name] ?? name;
}

export function CardForm({
  values,
  onChange,
}: {
  values: CardFormValues;
  onChange: (values: CardFormValues) => void;
}) {
  const set = <K extends keyof CardFormValues>(key: K) => (value: CardFormValues[K]) =>
    onChange({ ...values, [key]: value });

  return (
    <View style={{ gap: space.lg }}>
      <Surface>
        <SectionTitle>Listing</SectionTitle>
        <Field
          label="eBay title"
          value={values.ebayTitle}
          onChangeText={set('ebayTitle')}
          multiline
          maxLength={80}
          hint={`${values.ebayTitle.length}/80`}
        />
        <Row>
          <Field
            label="Your price"
            value={values.suggestedPrice}
            onChangeText={set('suggestedPrice')}
            keyboardType="decimal-pad"
            placeholder="No comp"
          />
          <Field
            label="Your cost"
            value={values.costBasis}
            onChangeText={set('costBasis')}
            keyboardType="decimal-pad"
            placeholder="Optional"
          />
        </Row>
      </Surface>

      <Surface>
        <SectionTitle>Card</SectionTitle>
        <Field label="Player" value={values.player} onChangeText={set('player')} autoCapitalize="words" />
        <Row ratio={[1, 2]}>
          <Field label="Year" value={values.year} onChangeText={set('year')} keyboardType="number-pad" />
          <Field label="Manufacturer" value={values.manufacturer} onChangeText={set('manufacturer')} />
        </Row>
        <Field label="Set" value={values.set} onChangeText={set('set')} />
        <Row>
          <Field label="Card #" value={values.cardNumber} onChangeText={set('cardNumber')} autoCapitalize="characters" />
          <Field label="Serial #" value={values.serialNumber} onChangeText={set('serialNumber')} placeholder="e.g. 23/99" />
        </Row>
        <Field label="Parallel / variant" value={values.parallel} onChangeText={set('parallel')} placeholder="Base" />
        <View style={styles.chips}>
          <ToggleChip label="Rookie" icon="star-outline" value={values.isRookie} onChange={set('isRookie')} />
          <ToggleChip label="Auto" icon="create-outline" value={values.isAutograph} onChange={set('isAutograph')} />
          <ToggleChip label="Relic" icon="shirt-outline" value={values.isRelic} onChange={set('isRelic')} />
        </View>
      </Surface>

      <Surface>
        <SectionTitle>Grading & condition</SectionTitle>
        <Row>
          <Field
            label="Grader"
            value={values.gradingCompany}
            onChangeText={set('gradingCompany')}
            autoCapitalize="characters"
            placeholder="Raw"
          />
          <Field label="Grade" value={values.grade} onChangeText={set('grade')} keyboardType="decimal-pad" placeholder="—" />
        </Row>
        <Field label="Condition notes" value={values.conditionNotes} onChangeText={set('conditionNotes')} multiline />
      </Surface>
    </View>
  );
}

function Row({ children, ratio = [1, 1] }: { children: ReactNode[]; ratio?: number[] }) {
  return (
    <View style={styles.row}>
      {children.map((child, i) => (
        <View key={i} style={{ flex: ratio[i] ?? 1 }}>
          {child}
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: space.md },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
});
