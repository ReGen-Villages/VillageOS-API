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
function baseKeys(obj: unknown, prefix = ''): Set<string> {
  const out = new Set<string>();
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
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
});
