import { useId } from 'react';
import type { AskedValue } from '../../../types/dashboard';
import type { Row } from '../../../api/dashboardApi';
import type { Entered } from './writeRequest';

const INPUT_TYPES = { text: 'text', number: 'number', datetime: 'datetime-local' } as const;

const FIELD_CLASS =
  'w-full rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-2 py-1 text-sm text-zinc-900 dark:text-zinc-100';

/** How many rows of a list stand open at once. Enough to choose a few out of without scrolling for
 *  the first one, and short enough that the field beside it stays on the same card. */
const MULTICHOICE_ROWS = 6;

/** What a row reads as in a list somebody chooses from: its name, and beside it the values the
 *  field says it shows. The field decides, so a roster shows what somebody is choosing on rather
 *  than everything its rows happen to carry. */
function nameAndDetail(row: Row, shows: string[]): string {
  const name = String(row.name ?? '');
  const detail = shows.map((key) => String(row[key] ?? '')).filter((value) => value !== '');
  return detail.length === 0 ? name : `${name} · ${detail.join(' · ')}`;
}

/** The values a widget asks a person for, each drawn as the input its kind names. A choice offers
 *  the Things its binding listed by name, with nothing chosen until somebody chooses — a field that
 *  defaulted to the first name would record against it by accident. A field taking several names
 *  at once is the same list, left open so several can be chosen without holding a key down. */
export function AskedFields({
  fields,
  options,
  entered,
  onChange,
  disabled,
  inline,
}: {
  fields: AskedValue[];
  /** The rows each field's `options` resolved to, in the order of `fields`. */
  options: Row[][];
  entered: Entered;
  onChange: (entered: Entered) => void;
  disabled?: boolean;
  /** Drawn beside the row they belong to rather than as a stacked form. */
  inline?: boolean;
}) {
  const prefix = useId();
  return (
    <>
      {fields.map((field, i) => {
        const id = `${prefix}-${field.key}`;
        // A field's kind decides which shape it holds — a list for the one taking several names, a
        // string for every other — so each reads its own and leaves anything else empty.
        const held = entered[field.key];
        const chosenNames = Array.isArray(held) ? held : [];
        const typed = typeof held === 'string' ? held : '';
        const rows = options[i] ?? [];
        const set = (next: string | string[]) => onChange({ ...entered, [field.key]: next });
        return (
          <label key={field.key} htmlFor={id} className={inline ? 'flex items-center gap-1.5 text-xs' : 'block text-sm'}>
            <span className={inline ? 'text-zinc-500 dark:text-zinc-400' : 'block text-xs text-zinc-500 dark:text-zinc-400'}>{field.label}</span>
            {field.kind === 'multichoice' ? (
              <select
                id={id}
                multiple
                size={MULTICHOICE_ROWS}
                value={chosenNames}
                disabled={disabled}
                onChange={(event) => set([...event.target.selectedOptions].map((option) => option.value))}
                className={FIELD_CLASS}
              >
                {rows.map((row) => (
                  <option key={String(row.id)} value={String(row.name ?? '')}>
                    {nameAndDetail(row, field.shows ?? [])}
                  </option>
                ))}
              </select>
            ) : field.kind === 'choice' ? (
              <select
                id={id}
                value={typed}
                disabled={disabled}
                onChange={(event) => set(event.target.value)}
                className={inline ? `${FIELD_CLASS} w-auto` : FIELD_CLASS}
              >
                <option value="">—</option>
                {rows.map((row) => (
                  <option key={String(row.id)} value={String(row.name ?? '')}>
                    {String(row.name ?? '')}
                  </option>
                ))}
              </select>
            ) : (
              <input
                id={id}
                type={INPUT_TYPES[field.kind ?? 'text']}
                value={typed}
                disabled={disabled}
                onChange={(event) => set(event.target.value)}
                className={inline ? `${FIELD_CLASS} w-auto` : FIELD_CLASS}
              />
            )}
          </label>
        );
      })}
    </>
  );
}
