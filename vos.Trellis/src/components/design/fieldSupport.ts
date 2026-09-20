import type { TFunction } from 'i18next';
import { formatPropertyValue } from '../../utils/formatters';
import type { BindingFieldKey, Offering, WidgetFieldKey } from '../../utils/widgetSchema';
import type { Offered } from './OfferedField';
import type { BindingContext } from './bindingContext';

/** The words a text field offers, by what the schema says fills it. */
export function offeredFor(offers: Offering | undefined, context: BindingContext, kind: string | undefined, t: TFunction): Offered[] {
  switch (offers) {
    case 'kinds':
      return context.offers.kinds.map((value) => ({ value }));
    case 'property':
      return kind
        ? context.offers.propertiesOf(kind).map((property) => ({
            value: property.name,
            caption: [
              property.declaredBy !== kind ? t('design.offers.declaredBy', { kind: property.declaredBy }) : '',
              property.example !== undefined && property.example !== '' ? t('design.offers.example', { value: formatPropertyValue(property.example) }) : '',
            ].filter(Boolean).join(' · '),
          }))
        : [];
    case 'state':
      return kind ? context.statesOf(kind).map((value) => ({ value })) : [];
    case 'thing':
      return context.rowKind || context.offers.compareKind ? [{ value: '$scope' }] : [];
    case 'endpoint':
      return context.endpoints.map((subdomain) => ({ value: `/api/endpoints/${subdomain}` }));
    case 'predicate':
      return context.offers.predicates.map((value) => ({ value }));
    case 'rowKey':
      return (context.rowKeys ?? []).map((value) => ({ value }));
    default:
      return [];
  }
}

/** The object with one key written, or taken away where the value is nothing. */
export function withKey<T extends Record<string, unknown>>(held: T, key: string, value: unknown): T {
  const next = { ...held } as Record<string, unknown>;
  if (value === undefined) delete next[key];
  else next[key] = value;
  return next as T;
}

export const smallButtonClass =
  'inline-flex items-center gap-1 rounded-md border border-zinc-200 bg-white px-2 py-0.5 text-[11px] font-semibold text-zinc-600 hover:text-zinc-900 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-100';

/** The indent a binding's or a list's parts stand at, under the control that holds them. */
export const nestedClass = 'mt-1 ml-1 border-l-2 border-zinc-200 dark:border-zinc-700 pl-2 space-y-2';

/** Which family of words a field's label is read from. */
export type FieldFamily = 'widgetField' | 'bindingField';

export function labelFor(family: FieldFamily, key: string, t: TFunction): string {
  return family === 'widgetField' ? t(`design.widgetField.${key as WidgetFieldKey}`) : t(`design.bindingField.${key as BindingFieldKey}`);
}
