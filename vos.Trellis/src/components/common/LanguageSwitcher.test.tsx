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

  it('lists the supported languages', () => {
    render(<LanguageSwitcher />);
    const options = screen.getAllByRole('option').map((o) => o.textContent);
    expect(options).toEqual(['English', 'Deutsch', 'Español', 'Français', 'Italiano', 'Nederlands']);
  });

  it('switching the control changes rendered text and persists the choice', () => {
    render(
      <>
        <LanguageSwitcher />
        <NavLabel />
      </>,
    );

    expect(screen.getByTestId('nav-label').textContent).toBe('Logs');

    fireEvent.change(screen.getByTestId('language-switcher'), { target: { value: 'es' } });

    expect(screen.getByTestId('nav-label').textContent).toBe('Registros');
    expect(localStorage.getItem('vos-language')).toBe('es');
  });
});
