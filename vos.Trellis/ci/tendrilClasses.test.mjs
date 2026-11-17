import { describe, expect, it } from 'vitest';
import { classNamesIn, missingFrom } from './tendrilClasses.mjs';

describe('the class names a source file asks for', () => {
  it('reads them from a quoted, braced or templated className, including one chosen at run time', () => {
    const source = [
      '<span className="min-w-40 text-sm">',
      "<p className={'mt-2 dark:text-zinc-400'}>",
      '<div className={`flex flex-col ${shown ? \'overflow-y-auto\' : \'\'}`} style={capStyle(rows)}>',
    ].join('\n');

    expect(classNamesIn(source).sort()).toEqual(['dark:text-zinc-400', 'flex', 'flex-col', 'min-w-40', 'mt-2', 'overflow-y-auto', 'text-sm']);
  });

  it('reads them from a constant named for the classes it holds, which a className then names', () => {
    const source = [
      "const FIELD_CLASS =\n  'w-full rounded bg-white';",
      "export const MUTED_CLASS = 'text-[color:var(--tendril-muted,var(--color-zinc-500))]';",
      "const LABEL = 'Reader';",
      '<input className={FIELD_CLASS} />',
    ].join('\n');

    expect(classNamesIn(source).sort()).toEqual(['bg-white', 'rounded', 'text-[color:var(--tendril-muted,var(--color-zinc-500))]', 'w-full']);
  });
});

describe('the class names a stylesheet lacks', () => {
  const stylesheet = '.mt-2{margin-top:.5rem}.hover\\:bg-emerald-700:hover{background:green}.min-w-40{min-width:10rem}';

  it('finds a class written with a variant, as the stylesheet escapes it', () => {
    expect(missingFrom(stylesheet, ['mt-2', 'hover:bg-emerald-700'])).toEqual([]);
  });

  it('reports a class the stylesheet does not carry', () => {
    expect(missingFrom(stylesheet, ['mt-2', 'min-w-0'])).toEqual(['min-w-0']);
  });

  it('finds a class whose value names a custom property with a fallback, as the stylesheet escapes it', () => {
    const themed = '.text-\\[color\\:var\\(--tendril-muted\\,var\\(--color-zinc-500\\)\\)\\]{color:var(--tendril-muted,var(--color-zinc-500))}';

    expect(missingFrom(themed, ['text-[color:var(--tendril-muted,var(--color-zinc-500))]'])).toEqual([]);
  });

  it('does not take a longer class for a shorter one it begins with', () => {
    expect(missingFrom(stylesheet, ['min-w-4'])).toEqual(['min-w-4']);
  });
});
