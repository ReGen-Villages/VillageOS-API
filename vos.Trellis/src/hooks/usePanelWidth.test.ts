import { describe, it, expect, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { usePanelWidth } from './usePanelWidth';

/** A pointer event as the handle receives it, with the capture calls jsdom does not implement. */
function pointerAt(clientX: number) {
  return {
    clientX,
    pointerId: 1,
    preventDefault: () => {},
    currentTarget: { setPointerCapture: () => {}, releasePointerCapture: () => {} },
  } as unknown as React.PointerEvent;
}

function keyPress(key: string) {
  return { key, preventDefault: () => {} } as unknown as React.KeyboardEvent;
}

describe('usePanelWidth', () => {
  const bounds = { initial: 200, min: 140, max: 480 };

  beforeEach(() => {
    Element.prototype.setPointerCapture = () => {};
    Element.prototype.releasePointerCapture = () => {};
  });

  it('starts at the width the panel was designed to open on', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));
    expect(result.current.width).toBe(200);
  });

  // A handle on the panel's right side widens the panel as the pointer travels right.
  it('follows the pointer away from a panel handled on the right to widen it', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));

    act(() => result.current.handleProperties.onPointerDown(pointerAt(200)));
    act(() => result.current.handleProperties.onPointerMove(pointerAt(290)));

    expect(result.current.width).toBe(290);
  });

  // The details panel is on the other side of the canvas, so the same gesture has to mean the
  // same thing there: dragging its handle towards the middle of the screen widens it.
  it('reads the same drag the other way round for a panel whose handle is on its left', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'left' }));

    act(() => result.current.handleProperties.onPointerDown(pointerAt(500)));
    act(() => result.current.handleProperties.onPointerMove(pointerAt(410)));

    expect(result.current.width).toBe(290);
  });

  it('will not be dragged narrower than its minimum or wider than its maximum', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));

    act(() => result.current.handleProperties.onPointerDown(pointerAt(200)));
    act(() => result.current.handleProperties.onPointerMove(pointerAt(0)));
    expect(result.current.width).toBe(140);

    act(() => result.current.handleProperties.onPointerMove(pointerAt(5000)));
    expect(result.current.width).toBe(480);
  });

  it('ignores pointer travel when no drag is under way, so a passing pointer moves nothing', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));

    act(() => result.current.handleProperties.onPointerMove(pointerAt(400)));

    expect(result.current.width).toBe(200);
  });

  it('stops following once the drag is let go', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));

    act(() => result.current.handleProperties.onPointerDown(pointerAt(200)));
    act(() => result.current.handleProperties.onPointerMove(pointerAt(260)));
    act(() => result.current.handleProperties.onPointerUp(pointerAt(260)));
    act(() => result.current.handleProperties.onPointerMove(pointerAt(400)));

    expect(result.current.width).toBe(260);
  });

  it('lets go when the browser takes the drag away, rather than following an unpressed pointer', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));

    act(() => result.current.handleProperties.onPointerDown(pointerAt(200)));
    act(() => result.current.handleProperties.onPointerMove(pointerAt(260)));
    act(() => result.current.handleProperties.onLostPointerCapture());
    act(() => result.current.handleProperties.onPointerMove(pointerAt(400)));

    expect(result.current.width).toBe(260);
    expect(result.current.isResizing).toBe(false);
  });

  // A pointer drag is not the only way to reach a control, and a separator that only answers a
  // mouse is one a keyboard cannot move at all.
  it('resizes from the keyboard, in both directions', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));

    act(() => result.current.handleProperties.onKeyDown(keyPress('ArrowRight')));
    expect(result.current.width).toBeGreaterThan(200);

    const widened = result.current.width;
    act(() => result.current.handleProperties.onKeyDown(keyPress('ArrowLeft')));
    expect(result.current.width).toBeLessThan(widened);
  });

  // The same rule the pointer follows: on the right-hand panel the arrow that widens it is the
  // one pointing at the canvas, so both panels answer the same key the same way.
  it('reads an arrow key the other way round for a panel whose handle is on its left', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'left' }));

    act(() => result.current.handleProperties.onKeyDown(keyPress('ArrowLeft')));
    expect(result.current.width).toBeGreaterThan(200);

    const widened = result.current.width;
    act(() => result.current.handleProperties.onKeyDown(keyPress('ArrowRight')));
    expect(result.current.width).toBeLessThan(widened);
  });

  it('keeps the keyboard inside the same bounds the pointer is held to', () => {
    const { result } = renderHook(() => usePanelWidth({ initial: 145, min: 140, max: 480, handleSide: 'right' }));

    act(() => result.current.handleProperties.onKeyDown(keyPress('ArrowLeft')));
    act(() => result.current.handleProperties.onKeyDown(keyPress('ArrowLeft')));

    expect(result.current.width).toBe(140);
  });

  it('leaves a key it does not act on to whatever else would handle it', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));

    act(() => result.current.handleProperties.onKeyDown(keyPress('Enter')));

    expect(result.current.width).toBe(200);
  });

  it('says which way it faces, so the handle can carry the right separator role', () => {
    const { result } = renderHook(() => usePanelWidth({ ...bounds, handleSide: 'right' }));

    expect(result.current.handleProperties['aria-orientation']).toBe('vertical');
    expect(result.current.handleProperties['aria-valuenow']).toBe(200);
    expect(result.current.handleProperties['aria-valuemin']).toBe(140);
    expect(result.current.handleProperties['aria-valuemax']).toBe(480);
  });
});
