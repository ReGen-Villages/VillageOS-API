import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { useTranslation } from 'react-i18next';
import i18n from '../../i18n';
import { LanguageSwitcher } from './LanguageSwitcher';

function NavLabel() {
  const { t } = useTranslation();
  return <span data-testid="nav-label">{t('nav.logs')}</span>;
}

function openMenu() {
  fireEvent.click(screen.getByRole('button', { name: /select language/i }));
  return screen.getByRole('listbox');
}

describe('LanguageSwitcher', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('is a collapsed dropdown until opened', () => {
    render(<LanguageSwitcher />);
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(screen.getByRole('button', { name: /select language/i })).toHaveAttribute(
      'aria-expanded',
      'false',
    );
  });

  it('opens to an option per supported language', () => {
    render(<LanguageSwitcher />);
    const names = within(openMenu())
      .getAllByRole('option')
      .map((option) => option.getAttribute('aria-label'));
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

  it('marks the active language as selected', () => {
    render(<LanguageSwitcher />);
    const menu = openMenu();
    expect(within(menu).getByRole('option', { name: 'English' })).toHaveAttribute('aria-selected', 'true');
    expect(within(menu).getByRole('option', { name: 'Español' })).toHaveAttribute('aria-selected', 'false');
  });

  it('choosing a language changes rendered text, closes the menu, and persists the choice', () => {
    render(
      <>
        <LanguageSwitcher />
        <NavLabel />
      </>,
    );

    expect(screen.getByTestId('nav-label').textContent).toBe('Logs');

    fireEvent.click(within(openMenu()).getByRole('option', { name: 'Español' }));

    expect(screen.getByTestId('nav-label').textContent).toBe('Registros');
    expect(localStorage.getItem('vos-language')).toBe('es');
    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('offers both Arabic locales, sharing one translation', () => {
    render(
      <>
        <LanguageSwitcher />
        <NavLabel />
      </>,
    );

    fireEvent.click(within(openMenu()).getByRole('option', { name: 'العربية (الإمارات)' }));

    expect(screen.getByTestId('nav-label').textContent).toBe('السجلات');
    expect(localStorage.getItem('vos-language')).toBe('ar-AE');
  });
});
