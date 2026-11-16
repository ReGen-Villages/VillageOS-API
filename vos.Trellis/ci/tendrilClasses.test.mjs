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
});

describe('the class names a stylesheet lacks', () => {
  const stylesheet = '.mt-2{margin-top:.5rem}.hover\\:bg-emerald-700:hover{background:green}.min-w-40{min-width:10rem}';

  it('finds a class written with a variant, as the stylesheet escapes it', () => {
    expect(missingFrom(stylesheet, ['mt-2', 'hover:bg-emerald-700'])).toEqual([]);
  });

  it('reports a class the stylesheet does not carry', () => {
    expect(missingFrom(stylesheet, ['mt-2', 'min-w-0'])).toEqual(['min-w-0']);
  });

  it('does not take a longer class for a shorter one it begins with', () => {
    expect(missingFrom(stylesheet, ['min-w-4'])).toEqual(['min-w-4']);
  });
});
