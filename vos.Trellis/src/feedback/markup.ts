export type MarkKind = 'highlight' | 'hide';

export interface Mark {
  kind: MarkKind;
  x: number;
  y: number;
  width: number;
  height: number;
}

interface Point {
  x: number;
  y: number;
}

/** Smaller than this is a click or a shaking hand, not a box anybody meant to draw. */
const SMALLEST_SIDE = 4;

const HIGHLIGHT_COLOUR = '#f59e0b';
const HIDE_COLOUR = '#000000';
const HIGHLIGHT_LINE = 3;

export function markBetween(start: Point, end: Point, kind: MarkKind): Mark | null {
  const width = Math.abs(end.x - start.x);
  const height = Math.abs(end.y - start.y);
  if (width < SMALLEST_SIDE && height < SMALLEST_SIDE) return null;
  return { kind, x: Math.min(start.x, end.x), y: Math.min(start.y, end.y), width, height };
}

/** Draws marks in picture pixels. `scale` is picture pixels per screen pixel, so an outline reads
 *  the same width on screen whatever size the picture is. */
export function drawMarks(context: CanvasRenderingContext2D, marks: readonly Mark[], scale: number): void {
  for (const mark of marks) {
    if (mark.kind === 'hide') {
      context.fillStyle = HIDE_COLOUR;
      context.fillRect(mark.x, mark.y, mark.width, mark.height);
    } else {
      context.strokeStyle = HIGHLIGHT_COLOUR;
      context.lineWidth = HIGHLIGHT_LINE * scale;
      context.strokeRect(mark.x, mark.y, mark.width, mark.height);
    }
  }
}
