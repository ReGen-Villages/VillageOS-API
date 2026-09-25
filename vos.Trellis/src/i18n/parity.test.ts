import { describe, it, expect } from 'vitest';
import { en } from './locales/en';
import { de } from './locales/de';
import { es } from './locales/es';
import { fr } from './locales/fr';
import { it as itLocale } from './locales/it';
import { nl } from './locales/nl';
import { ar } from './locales/ar';

/** Every leaf key path in an object, with i18next plural suffixes stripped so a
 *  `_one` / `_other` pair collapses to its base key. A locale is at parity when it
 *  supplies every base key the English source does. */
function baseKeys(object: unknown, prefix = ''): Set<string> {
  const out = new Set<string>();
  for (const [key, value] of Object.entries(object as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === 'object') {
      for (const k of baseKeys(value, path)) out.add(k);
    } else {
      out.add(path.replace(/_(zero|one|two|few|many|other)$/, ''));
    }
  }
  return out;
}

const LOCALES = { de, es, fr, it: itLocale, nl, ar };

describe('locale parity with the English base', () => {
  const enKeys = baseKeys(en);

  for (const [name, locale] of Object.entries(LOCALES)) {
    it(`${name} supplies every base key and no unknown key`, () => {
      const keys = baseKeys(locale);
      const missing = [...enKeys].filter((k) => !keys.has(k));
      const unknown = [...keys].filter((k) => !enKeys.has(k));
      expect({ missing, unknown }).toEqual({ missing: [], unknown: [] });
    });
  }

  it('ar gives every counted phrase all six of its plural forms', () => {
    // Parity collapses the forms to one key, so a phrase with only `_one` and `_other` passes it and
    // then reads "3 سطر" where Arabic says "3 أسطر".
    const counted = [...leafPaths(en)].filter((path) => path.endsWith('_other')).map((path) => path.slice(0, -'_other'.length));
    const arabic = leafPaths(ar);
    const missing = counted.flatMap((base) =>
      ['zero', 'one', 'two', 'few', 'many', 'other'].map((form) => `${base}_${form}`).filter((path) => !arabic.has(path)));

    expect(missing).toEqual([]);
  });
});

function leafPaths(object: unknown, prefix = ''): Set<string> {
  const out = new Set<string>();
  for (const [key, value] of Object.entries(object as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === 'object') for (const leaf of leafPaths(value, path)) out.add(leaf);
    else out.add(path);
  }
  return out;
}
