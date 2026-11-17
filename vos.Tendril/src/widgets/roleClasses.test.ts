import { describe, expect, it } from 'vitest';
import * as roles from './roleClasses';

const widgetSources: Record<string, string> = import.meta.glob(['./*.tsx', '!./*.test.tsx'], { query: '?raw', import: 'default', eager: true });

describe('the colour each role reads', () => {
  it.each([
    ['TEXT_CLASS', 'text', 'text-[color:var(--tendril-text,var(--color-zinc-900))]', 'dark:text-[color:var(--tendril-text,var(--color-zinc-100))]'],
    ['LABEL_CLASS', 'text', 'text-[color:var(--tendril-text,var(--color-zinc-700))]', 'dark:text-[color:var(--tendril-text,var(--color-zinc-200))]'],
    ['MUTED_CLASS', 'muted', 'text-[color:var(--tendril-muted,var(--color-zinc-500))]', 'dark:text-[color:var(--tendril-muted,var(--color-zinc-400))]'],
    ['QUIET_CLASS', 'muted', 'text-[color:var(--tendril-muted,var(--color-zinc-600))]', 'dark:text-[color:var(--tendril-muted,var(--color-zinc-300))]'],
    ['REFUSAL_CLASS', 'refusal', 'text-[color:var(--tendril-refusal,var(--color-red-600))]', 'dark:text-[color:var(--tendril-refusal,var(--color-red-400))]'],
    ['FIELD_BORDER_CLASS', 'field-border', 'border-[color:var(--tendril-field-border,var(--color-zinc-300))]', 'dark:border-[color:var(--tendril-field-border,var(--color-zinc-600))]'],
    ['FIELD_SURFACE_CLASS', 'field', 'bg-[var(--tendril-field,var(--color-white))]', 'dark:bg-[var(--tendril-field,var(--color-zinc-900))]'],
  ] as const)('%s reads --tendril-%s, falling back to today\'s colour in each mode', (name, _role, light, dark) => {
    const classes = roles[name].split(' ');
    expect(classes).toContain(light);
    expect(classes).toContain(dark);
  });

  it('draws the button from its own three properties, the same in both modes', () => {
    expect(roles.ACTION_CLASS.split(' ').sort()).toEqual([
      'bg-[var(--tendril-action,var(--color-emerald-600))]',
      'hover:bg-[var(--tendril-action-hover,var(--color-emerald-700))]',
      'text-[color:var(--tendril-action-text,var(--color-white))]',
    ]);
  });
});

describe('the widgets', () => {
  const files = Object.keys(widgetSources);

  it('are read by this check', () => {
    expect(files).toContain('./ActionList.tsx');
  });

  it.each(files)('%s names no fixed colour, so every colour can be set by the application', (file) => {
    const source = widgetSources[file];
    expect(source.match(/\b(?:text|bg|border)-(?:zinc|red|emerald|white|black)\b[\w-]*/g) ?? []).toEqual([]);
  });
});
