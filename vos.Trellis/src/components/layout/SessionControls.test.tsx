import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

const logout = vi.fn();
const switchModel = vi.fn();
vi.mock('../../hooks/useAuth', () => ({
  useAuth: () => ({ logout, switchModel }),
}));

import { SessionControls } from './SessionControls';

describe('SessionControls', () => {
  beforeEach(() => {
    logout.mockReset();
    switchModel.mockReset();
  });

  // The panel lives in the shared sidebar footer, so these controls must be
  // present on every page — not re-implemented per page as they once were.
  it.each([true, false])('renders theme, switch-model, and log-out (collapsed=%s)', (isCollapsed) => {
    render(<SessionControls isCollapsed={isCollapsed} />);
    expect(screen.getByTestId('theme-toggle')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /switch model/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /log out/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /select language/i })).toBeInTheDocument();
  });

  it('invokes switchModel and logout on click', () => {
    render(<SessionControls isCollapsed={false} />);
    fireEvent.click(screen.getByRole('button', { name: /switch model/i }));
    fireEvent.click(screen.getByRole('button', { name: /log out/i }));
    expect(switchModel).toHaveBeenCalledTimes(1);
    expect(logout).toHaveBeenCalledTimes(1);
  });
});
