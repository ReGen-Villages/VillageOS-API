import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, act } from '@testing-library/react';
import { ActivityFeed } from './ActivityFeed';
import type { ActivityEvent } from '../../types/mycelium';

function makeEvents(n: number): ActivityEvent[] {
  return Array.from({ length: n }, (_, i) => ({
    Type: 'ThingCreated',
    Timestamp: new Date(0).toISOString(),
    Description: `event ${i}`,
  }));
}

describe('ActivityFeed auto-scroll', () => {
  let scrollIntoView: ReturnType<typeof vi.spyOn>;
  let scrollTo: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    // jsdom doesn't implement these; define them so we can observe calls.
    HTMLElement.prototype.scrollIntoView = function () {};
    HTMLElement.prototype.scrollTo = function () {};
    scrollIntoView = vi.spyOn(HTMLElement.prototype, 'scrollIntoView');
    scrollTo = vi.spyOn(HTMLElement.prototype, 'scrollTo');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // Regression guard for the bug where a full feed dragged the rest of the
  // dashboard up: scrollIntoView scrolls every scrollable ancestor (the outer
  // content region), so auto-scroll must instead scroll the feed's own list.
  it('never calls scrollIntoView (which would scroll ancestor regions)', () => {
    const { rerender } = render(<ActivityFeed events={makeEvents(2)} onCollapse={() => {}} />);
    act(() => {
      rerender(<ActivityFeed events={makeEvents(50)} onCollapse={() => {}} />);
    });
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it('scrolls only the feed list element to its own bottom on new events', () => {
    const { rerender } = render(<ActivityFeed events={makeEvents(2)} onCollapse={() => {}} />);
    scrollTo.mockClear();
    act(() => {
      rerender(<ActivityFeed events={makeEvents(50)} onCollapse={() => {}} />);
    });
    expect(scrollTo).toHaveBeenCalled();
    const target = scrollTo.mock.instances[0] as HTMLElement;
    // The scrolled element is the internal overflow-auto list, not the window
    // or the dashboard content region.
    expect(target.classList.contains('overflow-auto')).toBe(true);
  });
});
