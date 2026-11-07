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

  it('titles a service log by the name it was sent, not the connection id that names the stream', () => {
    tail.connection = 'live';
    render(<MemoryRouter initialEntries={['/logs?service=3f1c9d2e-0000-4000-8000-000000000001&name=Metabolism']}><LogPage /></MemoryRouter>);
    expect(screen.getByText('Metabolism log')).toBeInTheDocument();
  });
});
