import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ConnectionState } from '../types/connection';

const tail: { connection: ConnectionState } = { connection: 'live' };
vi.mock('../hooks/useLogTail', () => ({
  useLogTail: () => ({ lines: [], connection: tail.connection, clear: () => {} }),
}));

import { LogPage } from './LogPage';

describe('LogPage', () => {
  it.each([
    ['connecting', 'Connecting…'],
    ['live', 'Streaming'],
    ['lost', 'Reconnecting'],
  ] as const)('says a %s log stream in words that fit it', (connection, words) => {
    tail.connection = connection;
    render(<MemoryRouter><LogPage /></MemoryRouter>);
    expect(screen.getByText(words)).toBeInTheDocument();
  });
});
