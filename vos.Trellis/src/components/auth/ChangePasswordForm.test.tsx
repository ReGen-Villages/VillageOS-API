import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ChangePasswordForm } from './ChangePasswordForm';

vi.mock('../../stores/themeStore', () => ({
  useThemeStore: <T,>(selector: (s: { theme: 'light' | 'dark'; toggle: () => void }) => T) =>
    selector({ theme: 'light', toggle: () => {} }),
}));

// Shown between signing in and reaching the application, so it is a sign-in screen too.
describe('ChangePasswordForm', () => {
  const props = { onChangePassword: vi.fn(async () => {}), error: null, loading: false, username: 'admin' };

  it('offers the theme switch', () => {
    render(<ChangePasswordForm {...props} />);
    expect(screen.getByTestId('theme-toggle')).not.toBeNull();
  });

  it('follows the theme instead of fixing a dark background', () => {
    const { container } = render(<ChangePasswordForm {...props} />);
    const root = container.firstElementChild as HTMLElement;
    expect(root.className).toMatch(/dark:bg-/);
    expect(root.className).not.toMatch(/bg-gray-900/);
  });
});
