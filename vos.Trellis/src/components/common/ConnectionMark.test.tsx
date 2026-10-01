import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ConnectionMark } from './ConnectionMark';

describe('ConnectionMark', () => {
  // Connecting and lost drawn alike is the fault this mark exists to end: a sign-in would show a
  // failure until its first stream opened.
  it.each([
    ['connecting', 'bg-amber-500'],
    ['live', 'bg-emerald-500'],
    ['lost', 'bg-red-500'],
  ] as const)('draws %s in a colour of its own', (state, colour) => {
    const { container } = render(<ConnectionMark state={state} />);
    expect(container.firstElementChild).toHaveClass(colour);
  });

  it('is decoration beside words that already say the state', () => {
    const { container } = render(<ConnectionMark state="live" />);
    expect(container.firstElementChild).toHaveAttribute('aria-hidden', 'true');
    expect(screen.queryByRole('img')).toBeNull();
  });

  // A mark standing beside a name rather than a state carries the state in colour alone, which a
  // reader who cannot tell the colours apart, or cannot see them, is not told.
  it('says the state itself where nothing beside it does', () => {
    render(<ConnectionMark state="connecting" saysItsState />);
    expect(screen.getByRole('img', { name: 'Connecting…' })).toHaveAttribute('title', 'Connecting…');
  });
});
