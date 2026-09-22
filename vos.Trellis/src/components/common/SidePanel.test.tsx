import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { SidePanel } from './SidePanel';

const LABELS = { expand: 'Show it', collapse: 'Hide it', resize: 'Drag to resize it' };
const BOUNDS = { initial: 240, min: 160, max: 600 };

function panel(side: 'left' | 'right') {
  return render(
    <div>
      <SidePanel side={side} bounds={BOUNDS} labels={LABELS} name="The panel">
        <p>what it holds</p>
      </SidePanel>
    </div>,
  );
}

describe('SidePanel', () => {
  it('says what it holds, so a page carrying three of these is three a reader can tell apart', () => {
    panel('left');

    expect(screen.getByRole('heading', { name: 'The panel' })).toBeInTheDocument();
  });

  it('drops the heading with the body, a rail having no room for it', () => {
    panel('left');

    fireEvent.click(screen.getByRole('button', { name: 'Hide it' }));

    expect(screen.queryByRole('heading', { name: 'The panel' })).toBeNull();
  });

  it('folds to a rail and leaves the control that opens it again on that rail', () => {
    panel('left');

    fireEvent.click(screen.getByRole('button', { name: 'Hide it' }));

    expect(screen.queryByText('what it holds')).toBeNull();
    expect(screen.getByRole('button', { name: 'Show it' })).toBeInTheDocument();
  });

  it('takes the separator away with the panel, there being no handle left to drag', () => {
    panel('left');

    expect(screen.getByRole('separator', { name: 'Drag to resize it' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Hide it' }));
    expect(screen.queryByRole('separator')).toBeNull();
  });

  // The handle belongs on the side facing the work, which is the far side from the window frame
  // whichever way the panel stands. Getting this the wrong way round puts a panel's handle against the window frame,
  // where dragging it does nothing anybody wanted.
  it('puts the separator on the side facing the work, whichever side the panel stands on', () => {
    const { unmount } = panel('left');
    const onTheLeft = screen.getByRole('complementary', { name: 'The panel' });
    expect(onTheLeft.nextElementSibling).toBe(screen.getByRole('separator'));
    unmount();

    panel('right');
    const onTheRight = screen.getByRole('complementary', { name: 'The panel' });
    expect(onTheRight.previousElementSibling).toBe(screen.getByRole('separator'));
  });

  it('shows the controls a caller puts beside the fold control, and folds them away with the body', () => {
    render(
      <SidePanel side="right" bounds={BOUNDS} labels={LABELS} name="The panel" headerControls={<button type="button">Close</button>}>
        <p>what it holds</p>
      </SidePanel>,
    );

    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Hide it' }));
    expect(screen.queryByRole('button', { name: 'Close' })).toBeNull();
  });
});
