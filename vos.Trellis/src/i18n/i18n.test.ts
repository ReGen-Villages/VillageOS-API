import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import i18n, { changeLanguage } from './index';

describe('i18n instance', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('resolves a translated key in the active language', async () => {
    await changeLanguage('es');
    expect(i18n.t('nav.logs')).toBe('Registros');
  });

  it('changeLanguage persists the choice', async () => {
    await changeLanguage('es');
    expect(localStorage.getItem('vos-language')).toBe('es');
  });

  it('falls back to the base locale for a key missing from the target locale', () => {
    // es intentionally omits log.fullLogTitle (draft translation gap).
    expect(i18n.t('log.fullLogTitle', { lng: 'es' })).toBe(
      i18n.t('log.fullLogTitle', { lng: 'en' }),
    );
  });
});
