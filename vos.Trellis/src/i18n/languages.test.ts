import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
  BASE_LANGUAGE,
  detectInitialLanguage,
  directionFor,
  isRightToLeft,
  loadStoredLanguage,
  persistLanguage,
} from './languages';

function setNavigatorLanguage(value: string) {
  Object.defineProperty(navigator, 'language', { value, configurable: true });
}

describe('language helpers', () => {
  const originalLanguage = navigator.language;

  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    setNavigatorLanguage(originalLanguage);
  });

  it('persists and reloads a supported language', () => {
    persistLanguage('es');
    expect(loadStoredLanguage()).toBe('es');
  });

  it('ignores an unsupported stored value', () => {
    localStorage.setItem('vos-language', 'kl');
    expect(loadStoredLanguage()).toBeNull();
  });

  it('prefers the stored choice over the browser language', () => {
    persistLanguage('en');
    setNavigatorLanguage('es-ES');
    expect(detectInitialLanguage()).toBe('en');
  });

  it('falls back to the browser primary subtag when nothing is stored', () => {
    setNavigatorLanguage('es-MX');
    expect(detectInitialLanguage()).toBe('es');
  });

  it('falls back to the base language for an unsupported browser language', () => {
    setNavigatorLanguage('ja-JP');
    expect(detectInitialLanguage()).toBe(BASE_LANGUAGE);
  });

  it('matches a region-coded browser language exactly', () => {
    setNavigatorLanguage('ar-AE');
    expect(detectInitialLanguage()).toBe('ar-AE');
  });

  it('routes another Arabic region to the first Arabic locale by primary subtag', () => {
    setNavigatorLanguage('ar-BH');
    expect(detectInitialLanguage()).toBe('ar-SA');
  });

  it('persists and reloads a region-coded language', () => {
    persistLanguage('ar-SA');
    expect(loadStoredLanguage()).toBe('ar-SA');
  });
});

describe('text direction', () => {
  it('reports Arabic locales as right-to-left', () => {
    expect(isRightToLeft('ar-SA')).toBe(true);
    expect(directionFor('ar-AE')).toBe('rtl');
  });

  it('reports Latin-script locales as left-to-right', () => {
    expect(isRightToLeft('en')).toBe(false);
    expect(directionFor('es')).toBe('ltr');
  });
});
