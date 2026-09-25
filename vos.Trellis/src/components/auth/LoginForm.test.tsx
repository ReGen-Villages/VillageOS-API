import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LoginForm } from './LoginForm';

vi.mock('../../stores/themeStore', () => ({
  useThemeStore: <T,>(selector: (s: { theme: 'light' | 'dark'; toggle: () => void }) => T) =>
    selector({ theme: 'light', toggle: () => {} }),
}));

const signIn = {
  onLogin: vi.fn(async () => {}),
  error: null,
  loading: false,
};

// The sidebar footer carries the theme switch on every signed-in page; the screens before
// sign-in are the only ones a person cannot switch from, and they must follow the theme for
// the switch to show anything.
describe('LoginForm', () => {
  it('offers the theme switch on the sign-in form', () => {
    render(<LoginForm {...signIn} availableModels={null} />);
    expect(screen.getByTestId('theme-toggle')).not.toBeNull();
  });

  it('offers the theme switch on the model list', () => {
    render(
      <LoginForm
        {...signIn}
        onSelectModel={vi.fn(async () => {})}
        availableModels={[{ Id: 'a', Name: 'Orchard  (1.0 MB)' } as never]}
      />,
    );
    expect(screen.getByTestId('theme-toggle')).not.toBeNull();
  });

  it('follows the theme instead of fixing a dark background', () => {
    const { container } = render(<LoginForm {...signIn} availableModels={null} />);
    const root = container.firstElementChild as HTMLElement;
    expect(root.className).toMatch(/dark:bg-/);
    expect(root.className).not.toMatch(/bg-gray-900/);
  });
});
