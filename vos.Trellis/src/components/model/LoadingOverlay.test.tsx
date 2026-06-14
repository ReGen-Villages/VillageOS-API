import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LoadingOverlay } from './LoadingOverlay';

describe('LoadingOverlay', () => {
  it('renders the stage name and rounded percent', () => {
    render(<LoadingOverlay stage="parsing" progress={0.37} />);
    expect(screen.getByText(/parsing/i)).toBeInTheDocument();
    expect(screen.getByText(/37%/)).toBeInTheDocument();
  });

  it('clamps progress above 1 to 100%', () => {
    render(<LoadingOverlay stage="done" progress={3.4} />);
    expect(screen.getByText(/100%/)).toBeInTheDocument();
  });

  it('clamps negative progress to 0%', () => {
    render(<LoadingOverlay stage="starting" progress={-0.5} />);
    expect(screen.getByText(/0%/)).toBeInTheDocument();
  });

  it('sets the bar width to the percent', () => {
    render(<LoadingOverlay stage="generating" progress={0.62} />);
    const bar = screen.getByTestId('fragments-loading-bar') as HTMLDivElement;
    expect(bar.style.width).toBe('62%');
  });

  it('exposes an accessible label for the status region', () => {
    render(<LoadingOverlay stage="decompressing" progress={0.1} />);
    expect(screen.getByRole('status', { name: /loading model: decompressing/i })).toBeInTheDocument();
  });
});
