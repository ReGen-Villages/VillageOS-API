import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import type { FormOptions } from '../api/intakeApi';
import { useFormOptions } from './useFormOptions';

const formOptions = vi.hoisted(() => vi.fn<() => Promise<FormOptions>>());
vi.mock('../api/intakeApi', () => ({ intakeApi: { formOptions: () => formOptions() } }));

const ANSWERED = { categories: [] } as unknown as FormOptions;

beforeEach(() => {
  formOptions.mockReset();
});

describe('useFormOptions', () => {
  it('holds what the intake service answered, and hands it to the caller once', async () => {
    formOptions.mockResolvedValue(ANSWERED);
    const onAnswered = vi.fn();

    const { result } = renderHook(() => useFormOptions(onAnswered));

    await waitFor(() => expect(result.current.options).toBe(ANSWERED));
    expect(result.current.unreachable).toBe(false);
    expect(onAnswered).toHaveBeenCalledExactlyOnceWith(ANSWERED);
  });

  it('says the service is unreachable when the read fails', async () => {
    formOptions.mockRejectedValue(new Error('offline'));

    const { result } = renderHook(() => useFormOptions());

    await waitFor(() => expect(result.current.unreachable).toBe(true));
    expect(result.current.options).toBeNull();
  });

  it('ignores an answer that arrives after the page stopped asking', async () => {
    let answer: (options: FormOptions) => void = () => undefined;
    formOptions.mockReturnValue(new Promise((resolve) => (answer = resolve)));
    const onAnswered = vi.fn();

    const { unmount } = renderHook(() => useFormOptions(onAnswered));
    unmount();
    answer(ANSWERED);
    await Promise.resolve();

    expect(onAnswered).not.toHaveBeenCalled();
  });
});
