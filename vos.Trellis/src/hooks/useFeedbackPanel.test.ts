import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import i18n from '../i18n';
import { useFeedbackPanel } from './useFeedbackPanel';
import { mountFeedback } from '../feedback/feedbackPanel';
import { feedbackApi } from '../api/feedbackApi';

vi.mock('../feedback/feedbackPanel', () => ({ mountFeedback: vi.fn() }));
vi.mock('../api/feedbackApi', () => ({ feedbackApi: { offered: vi.fn(), send: vi.fn() } }));

const panel = { open: vi.fn(), setLanguage: vi.fn(), unmount: vi.fn() };

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(mountFeedback).mockReturnValue(panel);
  vi.mocked(feedbackApi.offered).mockReturnValue(true);
});

describe('the report panel in the console', () => {
  it('is mounted for a signed-in person as Trellis, sending through the relay', () => {
    renderHook(() => useFeedbackPanel(true));

    expect(mountFeedback).toHaveBeenCalledTimes(1);
    const options = vi.mocked(mountFeedback).mock.calls[0][0];
    expect(options.application).toBe('Trellis');
    expect(options.language).toBe(i18n.language);
    expect(options.submit).toBe(feedbackApi.send);
  });

  it('is not mounted when nobody is signed in, or a deployment switched it off', () => {
    renderHook(() => useFeedbackPanel(false));
    vi.mocked(feedbackApi.offered).mockReturnValue(false);
    renderHook(() => useFeedbackPanel(true));

    expect(mountFeedback).not.toHaveBeenCalled();
  });

  it('is taken down when the person signs out', () => {
    const { rerender } = renderHook(({ signedIn }) => useFeedbackPanel(signedIn), { initialProps: { signedIn: true } });

    rerender({ signedIn: false });

    expect(panel.unmount).toHaveBeenCalledTimes(1);
  });

  it('follows the console into another language', async () => {
    const before = i18n.language;
    renderHook(() => useFeedbackPanel(true));
    try {
      await i18n.changeLanguage('nl');
      expect(panel.setLanguage).toHaveBeenCalledWith('nl');
    } finally {
      await i18n.changeLanguage(before);
    }
  });
});
