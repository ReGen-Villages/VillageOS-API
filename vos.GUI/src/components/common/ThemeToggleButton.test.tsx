import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

const mockToggle = vi.fn();
let mockTheme: 'light' | 'dark' = 'light';
vi.mock('../../stores/themeStore', () => ({
  useThemeStore: <T,>(selector: (s: { theme: 'light' | 'dark'; toggle: () => void }) => T) =>
    selector({ theme: mockTheme, toggle: mockToggle }),
}));

import { ThemeToggleButton } from './ThemeToggleButton';

describe('ThemeToggleButton', () => {
  beforeEach(() => {
    mockToggle.mockReset();
    mockTheme = 'light';
  });

  it('shows a Moon icon when light is active (preview = switch to dark)', () => {
    mockTheme = 'light';
    render(<ThemeToggleButton />);
    const btn = screen.getByTestId('theme-toggle');
    expect(btn.getAttribute('aria-label')).toBe('Switch to dark mode');
    // lucide-react renders an SVG; sniff the aria-label to confirm the icon
    expect(btn.querySelector('svg.lucide-moon')).not.toBeNull();
  });

  it('shows a Sun icon when dark is active (preview = switch to light)', () => {
    mockTheme = 'dark';
    render(<ThemeToggleButton />);
    const btn = screen.getByTestId('theme-toggle');
    expect(btn.getAttribute('aria-label')).toBe('Switch to light mode');
    expect(btn.querySelector('svg.lucide-sun')).not.toBeNull();
  });

  it('calls themeStore.toggle on click', () => {
    render(<ThemeToggleButton />);
    fireEvent.click(screen.getByTestId('theme-toggle'));
    expect(mockToggle).toHaveBeenCalledTimes(1);
  });
});
