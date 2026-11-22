import { enclosesLand, type BoundaryPoint } from '../utils/parcelGeometry';

export interface BorderDrawing {
  readonly mode: 'drawing' | 'editing' | 'done';
  readonly corners: readonly BoundaryPoint[];
}

export type DrawingStep =
  | { readonly kind: 'accepted'; readonly drawing: BorderDrawing }
  | { readonly kind: 'crosses' }
  | { readonly kind: 'tooFewCorners' };

const CROSSES: DrawingStep = { kind: 'crosses' };
const TOO_FEW_CORNERS: DrawingStep = { kind: 'tooFewCorners' };

export function startDrawing(): BorderDrawing {
  return { mode: 'drawing', corners: [] };
}

export function addCorner(drawing: BorderDrawing, corner: BoundaryPoint): DrawingStep {
  const corners = [...drawing.corners, corner];
  return crossesItself(corners, false) ? CROSSES : accepted('drawing', corners);
}

export function undoCorner(drawing: BorderDrawing): BorderDrawing {
  return { mode: 'drawing', corners: drawing.corners.slice(0, -1) };
}

export function finishDrawing(drawing: BorderDrawing): DrawingStep {
  if (!enclosesLand(drawing.corners)) return TOO_FEW_CORNERS;
  return crossesItself(drawing.corners, true) ? CROSSES : accepted('done', drawing.corners);
}

export function startEditing(drawing: BorderDrawing): BorderDrawing {
  return { mode: 'editing', corners: drawing.corners };
}

export function moveCorner(drawing: BorderDrawing, index: number, to: BoundaryPoint): DrawingStep {
  const corners = drawing.corners.map((corner, at) => (at === index ? to : corner));
  return crossesItself(corners, true) ? CROSSES : accepted('editing', corners);
}

export function addCornerOnEdge(drawing: BorderDrawing, edge: number, corner: BoundaryPoint): DrawingStep {
  const corners = [...drawing.corners.slice(0, edge + 1), corner, ...drawing.corners.slice(edge + 1)];
  return crossesItself(corners, true) ? CROSSES : accepted('editing', corners);
}

export function finishEditing(drawing: BorderDrawing): BorderDrawing {
  return { mode: 'done', corners: drawing.corners };
}

function accepted(mode: BorderDrawing['mode'], corners: readonly BoundaryPoint[]): DrawingStep {
  return { kind: 'accepted', drawing: { mode, corners } };
}

// Sides that share a corner always meet there, so only sides with no corner in common are compared.
function crossesItself(corners: readonly BoundaryPoint[], closed: boolean): boolean {
  const sides = corners.length - (closed ? 0 : 1);
  for (let first = 0; first < sides; first += 1) {
    for (let second = first + 1; second < sides; second += 1) {
      const neighbours = second === first + 1 || (closed && first === 0 && second === sides - 1);
      if (neighbours) continue;
      if (sidesCross(corners[first], corners[(first + 1) % corners.length], corners[second], corners[(second + 1) % corners.length])) {
        return true;
      }
    }
  }
  return false;
}

// A border is a few hundred metres across, so its sides are compared as straight lines in degrees.
function sidesCross(a: BoundaryPoint, b: BoundaryPoint, c: BoundaryPoint, d: BoundaryPoint): boolean {
  const turn = (from: BoundaryPoint, to: BoundaryPoint, point: BoundaryPoint) =>
    Math.sign((to.longitude - from.longitude) * (point.latitude - from.latitude) - (to.latitude - from.latitude) * (point.longitude - from.longitude));
  return turn(a, b, c) * turn(a, b, d) < 0 && turn(c, d, a) * turn(c, d, b) < 0;
}
