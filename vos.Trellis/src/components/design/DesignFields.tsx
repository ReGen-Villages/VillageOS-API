import { useId } from 'react';
import clsx from 'clsx';
import { useWrittenWhenLeft } from '../../hooks/useWrittenWhenLeft';

/**
 * The fields the properties panel is drawn with. Each writes as it is left — on blur or Enter —
 * so a half-typed name never reaches the specification and the canvas never redraws per keystroke.
 */

const inputClass =
  'mt-1 w-full rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-2 py-1 text-sm text-zinc-900 dark:text-zinc-100';

export function Field({ title, hint, children }: { title: string; hint?: string; children: (ids: { id: string; describedBy?: string }) => React.ReactNode }) {
  const id = useId();
  const hintId = hint ? `${id}-hint` : undefined;
  return (
    <div>
      <label htmlFor={id} className="block text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
        {title}
      </label>
      {children({ id, describedBy: hintId })}
      {hint && (
        <p id={hintId} className="mt-0.5 text-[11px] text-zinc-400 dark:text-zinc-500">
          {hint}
        </p>
      )}
    </div>
  );
}

export function TextField({ label, hint, value, mono, onCommit }: { label: string; hint?: string; value: string; mono?: boolean; onCommit: (value: string) => void }) {
  const written = useWrittenWhenLeft(value, onCommit);
  return (
    <Field title={label} hint={hint}>
      {({ id, describedBy }) => <input {...written} id={id} aria-describedby={describedBy} className={clsx(inputClass, mono && 'font-mono')} />}
    </Field>
  );
}

export function NumberField({ label, hint, value, onCommit }: { label: string; hint?: string; value: unknown; onCommit: (value: number | undefined) => void }) {
  return (
    <TextField
      label={label}
      hint={hint}
      value={typeof value === 'number' ? String(value) : ''}
      mono
      onCommit={(text) => {
        const parsed = Number(text.trim());
        onCommit(text.trim() === '' || Number.isNaN(parsed) ? undefined : parsed);
      }}
    />
  );
}

export function ChoiceField({
  label, hint, value, options, emptyLabel, onCommit,
}: { label: string; hint?: string; value: string; options: readonly string[]; emptyLabel: string; onCommit: (value: string) => void }) {
  return (
    <Field title={label} hint={hint}>
      {({ id, describedBy }) => (
        <select id={id} aria-describedby={describedBy} value={value} onChange={(event) => onCommit(event.target.value)} className={inputClass}>
          <option value="">{emptyLabel}</option>
          {options.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      )}
    </Field>
  );
}

export function SwitchField({ label, hint, value, onCommit }: { label: string; hint?: string; value: boolean; onCommit: (value: boolean) => void }) {
  const id = useId();
  return (
    <div className="flex items-center gap-2">
      <input id={id} type="checkbox" checked={value} onChange={(event) => onCommit(event.target.checked)} />
      <label htmlFor={id} className="text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400" title={hint}>
        {label}
      </label>
    </div>
  );
}
