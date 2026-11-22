import { describe, expect, it } from 'vitest';
import {
  addCorner,
  addCornerOnEdge,
  applyBorderEdit,
  finishDrawing,
  finishEditing,
  moveCorner,
  startDrawing,
  startEditing,
  undoCorner,
  type BorderDrawing,
  type DrawingStep,
} from './borderDrawing';
import type { BoundaryPoint } from '../utils/parcelGeometry';

const at = (latitude: number, longitude: number): BoundaryPoint => ({ latitude, longitude });

// A unit square, south-west first, anticlockwise.
const square = [at(0, 0), at(0, 1), at(1, 1), at(1, 0)];

function drawn(corners: BoundaryPoint[]): BorderDrawing {
  return corners.reduce<BorderDrawing>((drawing, corner) => accepted(addCorner(drawing, corner)), startDrawing());
}

function accepted(step: DrawingStep): BorderDrawing {
  if (step.kind !== 'accepted') throw new Error(`refused: ${step.kind}`);
  return step.drawing;
}

describe('drawing a border', () => {
  it('starts drawing with no corners', () => {
    expect(startDrawing()).toEqual({ mode: 'drawing', corners: [] });
  });

  it('adds each corner in turn', () => {
    expect(drawn(square.slice(0, 3)).corners).toEqual(square.slice(0, 3));
  });

  it('refuses a corner whose side would cross a side already drawn', () => {
    const open = drawn([at(0, 0), at(0, 2), at(2, 1)]);

    expect(addCorner(open, at(-1, 1)).kind).toBe('crosses');
  });

  it('takes back the last corner on undo, and has nothing to take back with no corners', () => {
    expect(undoCorner(drawn(square.slice(0, 3))).corners).toEqual(square.slice(0, 2));
    expect(undoCorner(startDrawing()).corners).toEqual([]);
  });

  it('finishes from three corners and not from fewer', () => {
    expect(finishDrawing(drawn(square.slice(0, 2))).kind).toBe('tooFewCorners');
    expect(accepted(finishDrawing(drawn(square.slice(0, 3))))).toEqual({ mode: 'done', corners: square.slice(0, 3) });
  });

  it('refuses to finish where the closing side would cross a side already drawn', () => {
    const spiral = drawn([at(0, 0), at(0, 4), at(4, 4), at(4, 1), at(1, 1), at(1, 3), at(2, 3)]);

    expect(finishDrawing(spiral).kind).toBe('crosses');
  });
});

describe('editing a border', () => {
  const editing = startEditing(accepted(finishDrawing(drawn(square))));

  it('starts editing a finished border with its corners', () => {
    expect(editing).toEqual({ mode: 'editing', corners: square });
  });

  it('moves a corner', () => {
    expect(accepted(moveCorner(editing, 2, at(1.5, 1.5))).corners).toEqual([at(0, 0), at(0, 1), at(1.5, 1.5), at(1, 0)]);
  });

  it('refuses a move that would make the border cross itself', () => {
    expect(moveCorner(editing, 2, at(-0.5, 0.5)).kind).toBe('crosses');
  });

  it('adds a corner on a side, after the side it splits', () => {
    expect(accepted(addCornerOnEdge(editing, 1, at(0.5, 1.2))).corners).toEqual([at(0, 0), at(0, 1), at(0.5, 1.2), at(1, 1), at(1, 0)]);
  });

  it('refuses a corner on a side that would make the border cross itself', () => {
    expect(addCornerOnEdge(editing, 1, at(0.5, -0.5)).kind).toBe('crosses');
  });

  it('finishes editing with the corners as they stand', () => {
    const moved = accepted(moveCorner(editing, 2, at(1.5, 1.5)));

    expect(finishEditing(moved)).toEqual({ mode: 'done', corners: moved.corners });
  });
});

describe('an edit made on the map', () => {
  it('places a corner while drawing, under the rule against crossing', () => {
    const open = drawn([at(0, 0), at(0, 2), at(2, 1)]);

    expect(accepted(applyBorderEdit(open, { kind: 'place', corner: at(2, 0) })).corners).toHaveLength(4);
    expect(applyBorderEdit(open, { kind: 'place', corner: at(-1, 1) }).kind).toBe('crosses');
  });

  it('closes on the first corner, as finishing does', () => {
    expect(accepted(applyBorderEdit(drawn(square), { kind: 'close' }))).toEqual({ mode: 'done', corners: square });
    expect(applyBorderEdit(drawn(square.slice(0, 2)), { kind: 'close' }).kind).toBe('tooFewCorners');
  });

  it('moves a corner while editing, and refuses a move that crosses', () => {
    const editing = startEditing(accepted(finishDrawing(drawn(square))));

    expect(accepted(applyBorderEdit(editing, { kind: 'move', index: 2, to: at(1.5, 1.5) })).corners[2]).toEqual(at(1.5, 1.5));
    expect(applyBorderEdit(editing, { kind: 'move', index: 2, to: at(-0.5, 0.5) }).kind).toBe('crosses');
  });

  it('adds a corner dragged out of a side, and refuses one that crosses', () => {
    const editing = startEditing(accepted(finishDrawing(drawn(square))));

    expect(accepted(applyBorderEdit(editing, { kind: 'insert', side: 1, corner: at(0.5, 1.2) })).corners[2]).toEqual(at(0.5, 1.2));
    expect(applyBorderEdit(editing, { kind: 'insert', side: 1, corner: at(0.5, -0.5) }).kind).toBe('crosses');
  });
});
