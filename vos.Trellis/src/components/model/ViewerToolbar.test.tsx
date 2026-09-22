import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ViewerToolbar } from './ViewerToolbar';

function baseProps(overrides = {}) {
  return {
    cameraMode: '3d' as const,
    onCameraModeChange: vi.fn(),
    sectionEnabled: false,
    onSectionEnabledChange: vi.fn(),
    sectionY: 5,
    onSectionYChange: vi.fn(),
    minY: 0,
    maxY: 10,
    ...overrides,
  };
}

describe('ViewerToolbar', () => {
  it('marks the active camera mode button with aria-pressed', () => {
    const props = baseProps({ cameraMode: 'plan' as const });
    render(<ViewerToolbar {...props} />);
    expect(screen.getByTestId('camera-mode-plan').getAttribute('aria-pressed')).toBe('true');
    expect(screen.getByTestId('camera-mode-3d').getAttribute('aria-pressed')).toBe('false');
  });

  it('fires onCameraModeChange on mode button click', () => {
    const props = baseProps();
    render(<ViewerToolbar {...props} />);
    fireEvent.click(screen.getByTestId('camera-mode-plan'));
    expect(props.onCameraModeChange).toHaveBeenCalledWith('plan');
  });

  it('toggles section on click', () => {
    const props = baseProps({ sectionEnabled: false });
    render(<ViewerToolbar {...props} />);
    fireEvent.click(screen.getByTestId('section-toggle'));
    expect(props.onSectionEnabledChange).toHaveBeenCalledWith(true);
  });

  it('section slider is disabled when section cut is off', () => {
    const props = baseProps({ sectionEnabled: false });
    render(<ViewerToolbar {...props} />);
    const slider = screen.getByTestId('section-slider') as HTMLInputElement;
    expect(slider.disabled).toBe(true);
  });

  it('slider emits onSectionYChange with a parsed number', () => {
    const props = baseProps({ sectionEnabled: true, sectionY: 3 });
    render(<ViewerToolbar {...props} />);
    const slider = screen.getByTestId('section-slider');
    fireEvent.change(slider, { target: { value: '7.5' } });
    expect(props.onSectionYChange).toHaveBeenCalledWith(7.5);
  });

  it('respects minY/maxY on the range input', () => {
    const props = baseProps({ minY: -5, maxY: 15 });
    render(<ViewerToolbar {...props} />);
    const slider = screen.getByTestId('section-slider') as HTMLInputElement;
    expect(slider.min).toBe('-5');
    expect(slider.max).toBe('15');
  });
});
