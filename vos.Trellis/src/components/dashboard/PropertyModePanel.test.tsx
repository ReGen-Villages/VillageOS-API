import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

vi.mock('../../api/configurationApi', () => ({
  configurationApi: { getDefaultPropertyMode: vi.fn(), setDefaultPropertyMode: vi.fn() },
}));
vi.mock('../common/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { PropertyModePanel } from './PropertyModePanel';
import { configurationApi } from '../../api/configurationApi';
import { toast } from '../common/toastStore';

beforeEach(() => {
  vi.clearAllMocks();
});

describe('PropertyModePanel', () => {
  it('shows the configured mode once the read completes', async () => {
    vi.mocked(configurationApi.getDefaultPropertyMode).mockResolvedValue({ Mode: 'RingBuffer', RingBufferSize: 100 });

    render(<PropertyModePanel />);

    await waitFor(() => expect(screen.getByDisplayValue('RingBuffer')).toBeInTheDocument());
    expect(screen.getByDisplayValue('100')).toBeInTheDocument();
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('reports a failed read as an error toast instead of hiding it', async () => {
    vi.mocked(configurationApi.getDefaultPropertyMode).mockRejectedValue(new Error('broker unreachable'));

    render(<PropertyModePanel />);

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('broker unreachable'));
    expect(screen.getByText('Not available')).toBeInTheDocument();
  });
});
