import { act, fireEvent, render, screen } from '@testing-library/react';
import { useRef, useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useClosesOnEscapeOrPressOutside } from './useClosesOnEscapeOrPressOutside';

let frames: FrameRequestCallback[] = [];
const nextFrame = () => act(() => { const due = frames; frames = []; due.forEach((callback) => callback(0)); });

beforeEach(() => {
  frames = [];
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => frames.push(callback));
  vi.stubGlobal('cancelAnimationFrame', () => {});
});

afterEach(() => vi.unstubAllGlobals());

function Popup() {
  const [open, setOpen] = useState(false);
  const element = useRef<HTMLDivElement>(null);
  useClosesOnEscapeOrPressOutside(open, element, () => setOpen(false));
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>open</button>
      <div ref={element}>{open && <span>inside</span>}</div>
      <p>outside</p>
    </>
  );
}

function opened() {
  render(<Popup />);
  fireEvent.click(screen.getByRole('button', { name: 'open' }));
}

describe('useClosesOnEscapeOrPressOutside', () => {
  it('is not closed by a press before the next frame, which is when the press that opened it lands', () => {
    opened();
    fireEvent.pointerDown(screen.getByText('outside'));
    expect(screen.queryByText('inside')).not.toBeNull();
  });

  it('closes on a press outside it', () => {
    opened();
    nextFrame();
    fireEvent.pointerDown(screen.getByText('outside'));
    expect(screen.queryByText('inside')).toBeNull();
  });

  it('stays open on a press inside it', () => {
    opened();
    nextFrame();
    fireEvent.pointerDown(screen.getByText('inside'));
    expect(screen.queryByText('inside')).not.toBeNull();
  });

  it('closes on Escape', () => {
    opened();
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByText('inside')).toBeNull();
  });
});
