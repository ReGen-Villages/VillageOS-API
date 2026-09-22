import { useId } from 'react';
import clsx from 'clsx';
import { useWrittenWhenLeft } from '../../hooks/useWrittenWhenLeft';
import { Field } from './DesignFields';

export interface Offered {
  value: string;
  caption?: string;
}

/**
 * A text field that offers the model's own words under it and still takes a name it could not
 * offer. The browser's own list does the type-ahead, so the field stays a field: Enter takes what is
 * typed, and a word the model does not hold is written as it was typed, because the model may hold
 * names the readings cannot see.
 */
export function OfferedField({
  label, value, offered = [], mono, onCommit,
}: { label: string; value: string; offered?: Offered[]; mono?: boolean; onCommit: (value: string) => void }) {
  const listId = useId();
  const written = useWrittenWhenLeft(value, onCommit);
  return (
    <Field title={label}>
      {({ id }) => (
        <>
          <input
            {...written}
            id={id}
            list={listId}
            autoComplete="off"
            className={clsx(
              'mt-1 w-full rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-2 py-1 text-sm text-zinc-900 dark:text-zinc-100',
              mono && 'font-mono',
            )}
          />
          <datalist id={listId}>
            {offered.map((option) => (
              <option key={option.value} value={option.value} label={option.caption} />
            ))}
          </datalist>
        </>
      )}
    </Field>
  );
}
