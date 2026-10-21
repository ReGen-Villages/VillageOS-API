import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';

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

  it('offers the modes the platform reports, not a list of its own', async () => {
    vi.mocked(configurationApi.getDefaultPropertyMode).mockResolvedValue({
      Mode: 'FullHistory',
      AvailableModes: ['FullHistory', 'RingBuffer', 'SampledByObservations', 'SampledByTime', 'CurrentOnly'],
    });

    render(<PropertyModePanel />);

    await waitFor(() => expect(screen.getByDisplayValue('FullHistory')).toBeInTheDocument());
    const offered = screen.getAllByRole('option').map((option) => option.textContent);
    expect(offered).toEqual(['FullHistory', 'RingBuffer', 'SampledByObservations', 'SampledByTime', 'CurrentOnly']);
  });

  it('asks for a slot in seconds when sampling by time, and sends it', async () => {
    vi.mocked(configurationApi.getDefaultPropertyMode).mockResolvedValue({
      Mode: 'FullHistory', SampleSeconds: 60,
      AvailableModes: ['FullHistory', 'SampledByObservations', 'SampledByTime'],
    });
    vi.mocked(configurationApi.setDefaultPropertyMode).mockResolvedValue({ Mode: 'SampledByTime', SampleSeconds: 30 });

    render(<PropertyModePanel />);
    await waitFor(() => expect(screen.getByDisplayValue('FullHistory')).toBeInTheDocument());

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'SampledByTime' } });
    fireEvent.change(screen.getByDisplayValue('60'), { target: { value: '30' } });
    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(configurationApi.setDefaultPropertyMode)
      .toHaveBeenCalledWith('SampledByTime', undefined, undefined, 30));
  });

  it('asks for a rate when sampling by observations', async () => {
    vi.mocked(configurationApi.getDefaultPropertyMode).mockResolvedValue({
      Mode: 'SampledByObservations', SampleRate: 10, SampleSeconds: 60,
      AvailableModes: ['SampledByObservations', 'SampledByTime'],
    });

    render(<PropertyModePanel />);

    await waitFor(() => expect(screen.getByDisplayValue('10')).toBeInTheDocument());
    expect(screen.queryByDisplayValue('60')).not.toBeInTheDocument();
  });

  it('reports a failed read as an error toast instead of hiding it', async () => {
    vi.mocked(configurationApi.getDefaultPropertyMode).mockRejectedValue(new Error('broker unreachable'));

    render(<PropertyModePanel />);

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('broker unreachable'));
    expect(screen.getByText('Not available')).toBeInTheDocument();
  });
});
