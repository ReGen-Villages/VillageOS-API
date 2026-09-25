import { describe, it, expect, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import i18n from '../i18n';
import { useStandalonePageDocument } from './useStandalonePageDocument';

describe('useStandalonePageDocument', () => {
  afterEach(async () => {
    await act(() => i18n.changeLanguage('en'));
  });

  it('names the browser tab in the chosen language, and follows a change of language', async () => {
    await act(() => i18n.changeLanguage('de'));
    renderHook(() => useStandalonePageDocument('publicForm.title'));

    expect(document.title).toBe(i18n.t('publicForm.title', { lng: 'de' }));
    expect(document.title).not.toBe(i18n.t('publicForm.title', { lng: 'en' }));

    await act(() => i18n.changeLanguage('fr'));

    expect(document.title).toBe(i18n.t('publicForm.title', { lng: 'fr' }));
  });

  it('sets the language and reading direction on the page', async () => {
    await act(() => i18n.changeLanguage('ar-SA'));
    renderHook(() => useStandalonePageDocument('explore.title'));

    expect(document.documentElement.getAttribute('lang')).toBe('ar-SA');
    expect(document.documentElement.getAttribute('dir')).toBe('rtl');
  });
});
