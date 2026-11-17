/**
 * The colours Tendril draws with, one CSS custom property per role. An application sets a property to
 * theme every widget, and sets it again under its own dark-mode rule where the dark colour differs;
 * a property left unset keeps the colour given here as the fallback for each mode.
 */

export const TEXT_CLASS =
  'text-[color:var(--tendril-text,var(--color-zinc-900))] dark:text-[color:var(--tendril-text,var(--color-zinc-100))]';

export const LABEL_CLASS =
  'text-[color:var(--tendril-text,var(--color-zinc-700))] dark:text-[color:var(--tendril-text,var(--color-zinc-200))]';

export const MUTED_CLASS =
  'text-[color:var(--tendril-muted,var(--color-zinc-500))] dark:text-[color:var(--tendril-muted,var(--color-zinc-400))]';

export const QUIET_CLASS =
  'text-[color:var(--tendril-muted,var(--color-zinc-600))] dark:text-[color:var(--tendril-muted,var(--color-zinc-300))]';

export const REFUSAL_CLASS =
  'text-[color:var(--tendril-refusal,var(--color-red-600))] dark:text-[color:var(--tendril-refusal,var(--color-red-400))]';

export const ACTION_CLASS =
  'bg-[var(--tendril-action,var(--color-emerald-600))] text-[color:var(--tendril-action-text,var(--color-white))] hover:bg-[var(--tendril-action-hover,var(--color-emerald-700))]';

export const FIELD_BORDER_CLASS =
  'border-[color:var(--tendril-field-border,var(--color-zinc-300))] dark:border-[color:var(--tendril-field-border,var(--color-zinc-600))]';

export const FIELD_SURFACE_CLASS =
  'bg-[var(--tendril-field,var(--color-white))] dark:bg-[var(--tendril-field,var(--color-zinc-900))]';
