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

  it('resolves both Arabic regions through the shared ar translation', () => {
    expect(i18n.t('nav.logs', { lng: 'ar-SA' })).toBe('السجلات');
    expect(i18n.t('nav.logs', { lng: 'ar-AE' })).toBe('السجلات');
  });

  it('applies Arabic plural categories to counts', () => {
    expect(i18n.t('log.lineCount', { lng: 'ar-SA', count: 1 })).toBe('سطر واحد');
    expect(i18n.t('log.lineCount', { lng: 'ar-SA', count: 2 })).toBe('سطران');
    expect(i18n.t('log.lineCount', { lng: 'ar-SA', count: 3 })).toBe('3 أسطر');
    expect(i18n.t('log.lineCount', { lng: 'ar-SA', count: 11 })).toBe('11 سطرًا');
  });
});
