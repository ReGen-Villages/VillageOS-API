import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { useTranslation } from 'react-i18next';
import i18n from '../../i18n';
import { LanguageSwitcher } from './LanguageSwitcher';

function NavLabel() {
  const { t } = useTranslation();
  return <span data-testid="nav-label">{t('nav.logs')}</span>;
}

describe('LanguageSwitcher', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('offers a flag button per supported language', () => {
    render(<LanguageSwitcher />);
    const names = screen.getAllByRole('button').map((button) => button.getAttribute('aria-label'));
    expect(names).toEqual([
      'English',
      'Deutsch',
      'Español',
      'Français',
      'Italiano',
      'Nederlands',
      'العربية (السعودية)',
      'العربية (الإمارات)',
    ]);
  });

  it('marks the active language as pressed', () => {
    render(<LanguageSwitcher />);
    expect(screen.getByRole('button', { name: 'English' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Español' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('clicking a flag changes rendered text and persists the choice', () => {
    render(
      <>
        <LanguageSwitcher />
        <NavLabel />
      </>,
    );

    expect(screen.getByTestId('nav-label').textContent).toBe('Logs');

    fireEvent.click(screen.getByRole('button', { name: 'Español' }));

    expect(screen.getByTestId('nav-label').textContent).toBe('Registros');
    expect(localStorage.getItem('vos-language')).toBe('es');
  });

  it('offers both Arabic locales, sharing one translation', () => {
    render(
      <>
        <LanguageSwitcher />
        <NavLabel />
      </>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'العربية (الإمارات)' }));

    expect(screen.getByTestId('nav-label').textContent).toBe('السجلات');
    expect(localStorage.getItem('vos-language')).toBe('ar-AE');
  });
});
