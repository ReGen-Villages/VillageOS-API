import { describe, it, expect } from 'vitest';
import { drawMarks, markBetween, type Mark } from './markup';

function recordingContext() {
  const calls: string[] = [];
  const context = {
    set fillStyle(value: string) { calls.push(`fillStyle ${value}`); },
    set strokeStyle(value: string) { calls.push(`strokeStyle ${value}`); },
    set lineWidth(value: number) { calls.push(`lineWidth ${value}`); },
    fillRect: (x: number, y: number, width: number, height: number) => calls.push(`fillRect ${x} ${y} ${width} ${height}`),
    strokeRect: (x: number, y: number, width: number, height: number) => calls.push(`strokeRect ${x} ${y} ${width} ${height}`),
  };
  return { context: context as unknown as CanvasRenderingContext2D, calls };
}

describe('a mark drawn on a screenshot', () => {
  it('covers the box between where the drag started and ended, whichever way it went', () => {
    expect(markBetween({ x: 80, y: 60 }, { x: 20, y: 10 }, 'hide')).toEqual({ kind: 'hide', x: 20, y: 10, width: 60, height: 50 });
  });

  it('is nothing when the drag did not move far enough to mean a box', () => {
    expect(markBetween({ x: 20, y: 20 }, { x: 22, y: 21 }, 'highlight')).toBeNull();
  });

  it('hides by filling the box solid, so nothing under it survives in the picture sent', () => {
    const { context, calls } = recordingContext();
    drawMarks(context, [{ kind: 'hide', x: 1, y: 2, width: 30, height: 40 }], 1);
    expect(calls).toContain('fillRect 1 2 30 40');
    expect(calls.some((call) => call.startsWith('strokeRect'))).toBe(false);
  });

  it('highlights by outlining the box, with a line that grows with the picture', () => {
    const { context, calls } = recordingContext();
    const marks: Mark[] = [{ kind: 'highlight', x: 5, y: 5, width: 10, height: 10 }];
    drawMarks(context, marks, 2);
    expect(calls).toContain('strokeRect 5 5 10 10');
    expect(calls).toContain('lineWidth 6');
  });
});
